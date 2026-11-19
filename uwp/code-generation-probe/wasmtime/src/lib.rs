use wasmtime::{Config, Engine, Instance, Module, Store, StoreLimits, StoreLimitsBuilder, Trap};

fn probe() -> Result<(), i32> {
    let mut config = Config::new();
    config
        .signals_based_traps(false)
        .memory_reservation(64 * 1024)
        .memory_guard_size(0)
        .consume_fuel(true);
    config.target("pulley64").map_err(|_| 1)?;
    let engine = Engine::new(&config).map_err(|_| 2)?;
    let module = Module::new(
        &engine,
        r#"(module
        (memory (export "memory") 1 2)
        (func (export "answer") (result i32) i32.const 42)
        (func (export "oob") (result i32) i32.const 65536 i32.load)
        (func (export "spin") (loop br 0)))"#,
    )
    .map_err(|_| 3)?;
    let limits = StoreLimitsBuilder::new().memory_size(65536).build();
    let mut store = Store::new(&engine, limits);
    store.limiter(|state: &mut StoreLimits| state);
    store.set_fuel(1000).map_err(|_| 4)?;
    let instance = Instance::new(&mut store, &module, &[]).map_err(|_| 5)?;
    let answer = instance
        .get_typed_func::<(), i32>(&mut store, "answer")
        .map_err(|_| 6)?;
    for _ in 0..10 {
        if answer.call(&mut store, ()).map_err(|_| 7)? != 42 {
            return Err(8);
        }
    }
    let memory = instance.get_memory(&mut store, "memory").ok_or(9)?;
    if memory.grow(&mut store, 1).is_ok() {
        return Err(10);
    }
    let oob = instance
        .get_typed_func::<(), i32>(&mut store, "oob")
        .map_err(|_| 11)?;
    let actual = oob
        .call(&mut store, ())
        .err()
        .and_then(|e| e.downcast::<Trap>().ok());
    if actual != Some(Trap::MemoryOutOfBounds) {
        return Err(12);
    }
    let spin = instance
        .get_typed_func::<(), ()>(&mut store, "spin")
        .map_err(|_| 13)?;
    let actual = spin
        .call(&mut store, ())
        .err()
        .and_then(|e| e.downcast::<Trap>().ok());
    if actual != Some(Trap::OutOfFuel) {
        return Err(14);
    }
    Ok(())
}

#[unsafe(no_mangle)]
pub extern "C" fn wasmtime_uwp_probe() -> i32 {
    probe().err().unwrap_or(0)
}
