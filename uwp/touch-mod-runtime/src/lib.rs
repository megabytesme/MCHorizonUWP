//! Narrow, fuel-limited host for separately installed touch-control modules.
//! The guest is data loaded by the application; no native code is generated.
use wasmi::{
    core::F32, Caller, Config, Engine, Instance, Linker, Module, Store, StoreLimits,
    StoreLimitsBuilder,
};
const MAX_MODULE: usize = 1024 * 1024;
const MAX_OUTPUT: usize = 64;
const FUEL: u64 = 100_000;
#[derive(Clone, Debug, PartialEq)]
pub struct Action {
    pub id: i32,
    pub value: f32,
}
#[derive(Clone, Debug, PartialEq)]
pub struct Control {
    pub id: i32,
    pub x: f32,
    pub y: f32,
    pub radius: f32,
}
struct State {
    limits: StoreLimits,
    actions: Vec<Action>,
    controls: Vec<Control>,
    invalid: bool,
}
pub struct TouchModule {
    store: Store<State>,
    instance: Instance,
    issued: u64,
}
impl TouchModule {
    pub fn load(bytes: &[u8]) -> Result<Self, String> {
        if bytes.is_empty() || bytes.len() > MAX_MODULE {
            return Err("invalid module size".into());
        }
        let mut config = Config::default();
        config.consume_fuel(true);
        let engine = Engine::new(&config);
        let module = Module::new(&engine, &mut &bytes[..]).map_err(|e| e.to_string())?;
        let limits = StoreLimitsBuilder::new()
            .memory_size(4 * 1024 * 1024)
            .table_elements(1024)
            .instances(1)
            .memories(1)
            .tables(1)
            .build();
        let mut store = Store::new(
            &engine,
            State {
                limits,
                actions: Vec::new(),
                controls: Vec::new(),
                invalid: false,
            },
        );
        store.limiter(|s| &mut s.limits);
        store.add_fuel(FUEL).map_err(|e| format!("{e:?}"))?;
        let mut linker = Linker::new(&engine);
        linker
            .func_wrap(
                "horizon_touch_v1",
                "action",
                |mut c: Caller<'_, State>, id: i32, value: F32| {
                    let value: f32 = value.into();
                    let s = c.data_mut();
                    if !(1..=13).contains(&id)
                        || !value.is_finite()
                        || value.abs() > 4096.0
                        || s.actions.len() >= MAX_OUTPUT
                    {
                        s.invalid = true;
                        return;
                    }
                    s.actions.push(Action { id, value });
                },
            )
            .map_err(|e| e.to_string())?;
        linker
            .func_wrap(
                "horizon_touch_v1",
                "control",
                |mut c: Caller<'_, State>, id: i32, x: F32, y: F32, radius: F32| {
                    let (x, y, radius): (f32, f32, f32) = (x.into(), y.into(), radius.into());
                    let s = c.data_mut();
                    if !(1..=13).contains(&id) && id != 21
                        || ![x, y, radius]
                            .iter()
                            .all(|v| v.is_finite() && v.abs() <= 32768.0)
                        || radius <= 0.0
                        || s.controls.len() >= MAX_OUTPUT
                    {
                        s.invalid = true;
                        return;
                    }
                    s.controls.push(Control { id, x, y, radius });
                },
            )
            .map_err(|e| e.to_string())?;
        let instance = linker
            .instantiate(&mut store, &module)
            .map_err(|e| e.to_string())?
            .start(&mut store)
            .map_err(|e| e.to_string())?;
        let version = instance
            .get_typed_func::<(), i32>(&store, "mod_api_version")
            .map_err(|e| e.to_string())?
            .call(&mut store, ())
            .map_err(|e| e.to_string())?;
        if version != 1
            || store.data().invalid
            || !store.data().actions.is_empty()
            || !store.data().controls.is_empty()
        {
            return Err("unsupported or misbehaving touch module".into());
        }
        instance
            .get_typed_func::<(i32, i32, F32, F32, F32, F32), i32>(&store, "touch")
            .map_err(|e| e.to_string())?;
        instance
            .get_typed_func::<(F32, F32), ()>(&store, "draw")
            .map_err(|e| e.to_string())?;
        instance
            .get_typed_func::<(), ()>(&store, "reset")
            .map_err(|e| e.to_string())?;
        Ok(Self {
            store,
            instance,
            issued: FUEL,
        })
    }
    fn budget(&mut self) -> Result<(), String> {
        let used = self.store.fuel_consumed().ok_or("fuel disabled")?;
        let left = self.issued.checked_sub(used).ok_or("fuel accounting")?;
        self.store
            .consume_fuel(left)
            .map_err(|e| format!("{e:?}"))?;
        self.store.add_fuel(FUEL).map_err(|e| format!("{e:?}"))?;
        self.issued = self.issued.checked_add(FUEL).ok_or("fuel overflow")?;
        let s = self.store.data_mut();
        s.actions.clear();
        s.controls.clear();
        s.invalid = false;
        Ok(())
    }
    pub fn touch(
        &mut self,
        id: i32,
        phase: i32,
        x: f32,
        y: f32,
        w: f32,
        h: f32,
    ) -> Result<(bool, Vec<Action>), String> {
        if id < 0
            || !(0..=3).contains(&phase)
            || ![x, y, w, h].iter().all(|v| v.is_finite())
            || w <= 0.0
            || h <= 0.0
        {
            return Err("invalid input".into());
        }
        self.budget()?;
        let f = self
            .instance
            .get_typed_func::<(i32, i32, F32, F32, F32, F32), i32>(&self.store, "touch")
            .map_err(|e| e.to_string())?;
        let claimed = f
            .call(
                &mut self.store,
                (id, phase, x.into(), y.into(), w.into(), h.into()),
            )
            .map_err(|e| e.to_string())?;
        let s = self.store.data_mut();
        if s.invalid || !s.controls.is_empty() || !(0..=1).contains(&claimed) {
            return Err("invalid guest output".into());
        }
        Ok((claimed == 1, std::mem::take(&mut s.actions)))
    }
    pub fn draw(&mut self, w: f32, h: f32) -> Result<Vec<Control>, String> {
        if !w.is_finite() || !h.is_finite() || w <= 0.0 || h <= 0.0 {
            return Err("invalid viewport".into());
        }
        self.budget()?;
        let f = self
            .instance
            .get_typed_func::<(F32, F32), ()>(&self.store, "draw")
            .map_err(|e| e.to_string())?;
        f.call(&mut self.store, (w.into(), h.into()))
            .map_err(|e| e.to_string())?;
        let s = self.store.data_mut();
        if s.invalid || !s.actions.is_empty() {
            return Err("invalid guest output".into());
        }
        Ok(std::mem::take(&mut s.controls))
    }
    pub fn reset(&mut self) -> Result<Vec<Action>, String> {
        self.budget()?;
        self.instance
            .get_typed_func::<(), ()>(&self.store, "reset")
            .map_err(|e| e.to_string())?
            .call(&mut self.store, ())
            .map_err(|e| e.to_string())?;
        let s = self.store.data_mut();
        if s.invalid || !s.controls.is_empty() {
            return Err("invalid guest output".into());
        }
        Ok(std::mem::take(&mut s.actions))
    }
}
