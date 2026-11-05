//! Shared UWP controller discovery and mapping.
// The bundled SDL database covers many controllers. This row supplements it
// with the X2 layout measured on Windows 10 Mobile. Unknown controllers still
// use gilrs' generic mapping; they are not filtered by vendor or product ID.
const EXTRA_SDL_MAPPINGS: &str = concat!(
    "03000000ac050000063b000000000000,GameSir X2 Type-C,",
    "a:b1,b:b0,x:b4,y:b3,back:b10,start:b11,guide:b12,",
    "leftshoulder:b6,rightshoulder:b7,leftstick:b13,rightstick:b14,",
    "lefttrigger:b8,righttrigger:b9,leftx:a0,lefty:a1,rightx:a2,righty:a3,",
    "dpup:h0.1,dpright:h0.2,dpdown:h0.4,dpleft:h0.8,platform:Windows,\n",
);

pub fn new_gilrs() -> Result<gilrs::Gilrs, gilrs::Error> {
    gilrs::GilrsBuilder::new()
        .add_mappings(EXTRA_SDL_MAPPINGS)
        .build()
}

/// Live mapped readings for the launcher controller test.
pub struct ControllerTest {
    gilrs: gilrs::Gilrs,
    last_active: Option<gilrs::GamepadId>,
}

impl ControllerTest {
    pub fn new() -> Result<Self, gilrs::Error> {
        Ok(Self {
            gilrs: new_gilrs()?,
            last_active: None,
        })
    }

    /// Navigation for raw controllers. Standard Windows Gamepads already send
    /// native XAML navigation keys and must not be handled a second time.
    pub fn navigation(&mut self) -> u32 {
        self.navigation_impl(false)
    }

    /// The game's loading menu owns all controller input, including Gamepad.
    pub fn navigation_all(&mut self) -> u32 {
        self.navigation_impl(true)
    }

    pub fn into_gilrs(self) -> (gilrs::Gilrs, Option<gilrs::GamepadId>) {
        (self.gilrs, self.last_active)
    }

    fn navigation_impl(&mut self, include_standard: bool) -> u32 {
        while self.gilrs.next_event().is_some() {}
        let mut keys = 0;
        for (id, pad) in self.gilrs.gamepads() {
            if !include_standard && pad.uuid() == [0; 16] {
                continue;
            }
            let x = pad.value(gilrs::Axis::LeftStickX);
            let y = pad.value(gilrs::Axis::LeftStickY);
            let scroll_y = pad.value(gilrs::Axis::RightStickY);
            if x.abs() > 0.6
                || y.abs() > 0.6
                || scroll_y.abs() > 0.6
                || [
                    gilrs::Button::South,
                    gilrs::Button::East,
                    gilrs::Button::DPadUp,
                    gilrs::Button::DPadDown,
                    gilrs::Button::DPadLeft,
                    gilrs::Button::DPadRight,
                ]
                .into_iter()
                .any(|button| pad.is_pressed(button))
            {
                self.last_active = Some(id);
            }
            if pad.is_pressed(gilrs::Button::DPadUp) || y > 0.6 {
                keys |= 1;
            }
            if pad.is_pressed(gilrs::Button::DPadDown) || y < -0.6 {
                keys |= 2;
            }
            if pad.is_pressed(gilrs::Button::DPadLeft) || x < -0.6 {
                keys |= 4;
            }
            if pad.is_pressed(gilrs::Button::DPadRight) || x > 0.6 {
                keys |= 8;
            }
            if pad.is_pressed(gilrs::Button::South) {
                keys |= 16;
            }
            if pad.is_pressed(gilrs::Button::East) {
                keys |= 32;
            }
            if scroll_y > 0.6 {
                keys |= 64;
            }
            if scroll_y < -0.6 {
                keys |= 128;
            }
            if include_standard {
                for (button, bit) in [
                    (gilrs::Button::West, 256),
                    (gilrs::Button::North, 512),
                    (gilrs::Button::Start, 1024),
                    (gilrs::Button::LeftTrigger2, 2048),
                    (gilrs::Button::RightTrigger, 4096),
                ] {
                    if pad.is_pressed(button) {
                        keys |= bit;
                    }
                }
            }
        }
        keys
    }

    pub fn read(&mut self) -> String {
        while self.gilrs.next_event().is_some() {}
        let mut output = String::new();
        for (_, pad) in self.gilrs.gamepads() {
            use std::fmt::Write;
            let mapping = if pad.mapping_source() == gilrs::MappingSource::SdlMappings {
                "database"
            } else {
                "automatic"
            };
            let _ = writeln!(output, "{} ({} mapping)", pad.name(), mapping);
            if let (Some(vendor), Some(product)) = (pad.vendor_id(), pad.product_id()) {
                let (vendor_name, product_name) = crate::usb_ids::lookup(vendor, product);
                let _ = writeln!(output, "Reported hardware ID: {vendor:04X}:{product:04X}");
                if let Some(name) = vendor_name {
                    let _ = writeln!(output, "USB ID vendor entry: {name}");
                }
                if let Some(name) = product_name {
                    let _ = writeln!(output, "USB ID product entry: {name}");
                }
            }
            let buttons = [
                ("South", gilrs::Button::South),
                ("East", gilrs::Button::East),
                ("West", gilrs::Button::West),
                ("North", gilrs::Button::North),
                ("L1", gilrs::Button::LeftTrigger),
                ("R1", gilrs::Button::RightTrigger),
                ("L2", gilrs::Button::LeftTrigger2),
                ("R2", gilrs::Button::RightTrigger2),
                ("Select", gilrs::Button::Select),
                ("Start", gilrs::Button::Start),
                ("L3", gilrs::Button::LeftThumb),
                ("R3", gilrs::Button::RightThumb),
                ("Up", gilrs::Button::DPadUp),
                ("Down", gilrs::Button::DPadDown),
                ("Left", gilrs::Button::DPadLeft),
                ("Right", gilrs::Button::DPadRight),
            ];
            let pressed: Vec<_> = buttons
                .iter()
                .filter(|(_, b)| pad.is_pressed(*b))
                .map(|(n, _)| *n)
                .collect();
            let _ = writeln!(
                output,
                "Pressed: {}",
                if pressed.is_empty() {
                    "none".into()
                } else {
                    pressed.join(", ")
                }
            );
            let _ = writeln!(
                output,
                "Left stick: {:.2}, {:.2}\nRight stick: {:.2}, {:.2}\nTriggers: {:.2}, {:.2}\n",
                pad.value(gilrs::Axis::LeftStickX),
                pad.value(gilrs::Axis::LeftStickY),
                pad.value(gilrs::Axis::RightStickX),
                pad.value(gilrs::Axis::RightStickY),
                pad.button_data(gilrs::Button::LeftTrigger2)
                    .map_or(0.0, |v| v.value()),
                pad.button_data(gilrs::Button::RightTrigger2)
                    .map_or(0.0, |v| v.value())
            );
        }
        if output.is_empty() {
            output.push_str("No controller connected. Connect one and press a button; this screen updates automatically.");
        }
        output
    }
}
