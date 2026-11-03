//! Built-in UWP source mods registered through Horizon's native hook API.
use crate::mod_api::{self, Hooks, Host, ModsEvent, NativeMod, SettingsClickEvent, SettingsEvent};

pub(crate) const PICKER_ID: &str = "horizon_uwp_picker";
struct Picker;
impl Hooks for Picker {
    fn mods(&self, _: &mut dyn Host, event: &mut ModsEvent<'_>) {
        event.mods.push(NativeMod {
            id: PICKER_ID.into(),
            title: "UWP File Picker".into(),
            description: "Import content mods and resource packs using the Windows file picker."
                .into(),
            keys: Vec::new(),
        });
    }
}

/// The platform folder action enters the built-in native picker mod.
pub(crate) fn open_folder(path: &std::path::Path) -> bool {
    let kind = match path.file_name().and_then(|name| name.to_str()) {
        Some("horizon-mods") => 1,
        Some("resourcepacks") | Some("datapacks") => 2,
        _ => return false,
    };
    horizon_uwp_picker::pick(path, kind)
}

#[cfg(feature = "native-touch-mod")]
struct Touch;
#[cfg(feature = "native-touch-mod")]
impl Hooks for Touch {
    fn mods(&self, _: &mut dyn Host, event: &mut ModsEvent<'_>) {
        event.mods.push(NativeMod {
            id: horizon_touch_native::MOD_ID.into(),
            title: horizon_touch_native::MOD_TITLE.into(),
            description: horizon_touch_native::MOD_DESCRIPTION.into(),
            keys: Vec::new(),
        });
    }
}

#[cfg(feature = "native-device-vibration-mod")]
struct DeviceVibration;
#[cfg(feature = "native-device-vibration-mod")]
impl Hooks for DeviceVibration {
    fn mods(&self, _: &mut dyn Host, event: &mut ModsEvent<'_>) {
        event.mods.push(NativeMod {
            id: horizon_device_vibration_native::MOD_ID.into(),
            title: horizon_device_vibration_native::MOD_TITLE.into(),
            description: horizon_device_vibration_native::MOD_DESCRIPTION.into(),
            keys: Vec::new(),
        });
    }
    fn settings(&self, host: &mut dyn Host, event: &mut SettingsEvent<'_>) {
        if event.mod_id == horizon_device_vibration_native::MOD_ID {
            let on = horizon_device_vibration_native::enabled(
                host.setting(event.mod_id, horizon_device_vibration_native::ENABLED_KEY)
                    .as_deref(),
            );
            event.buttons.push(format!(
                "Device vibrations: {}",
                if on { "On" } else { "Off" }
            ));
        }
    }
    fn settings_click(&self, host: &mut dyn Host, event: &mut SettingsClickEvent<'_>) {
        if event.mod_id == horizon_device_vibration_native::MOD_ID && event.button == 0 {
            let on = horizon_device_vibration_native::enabled(
                host.setting(event.mod_id, horizon_device_vibration_native::ENABLED_KEY)
                    .as_deref(),
            );
            host.set_setting(
                event.mod_id,
                horizon_device_vibration_native::ENABLED_KEY,
                if on { "false" } else { "true" },
            );
            if on {
                horizon_device_vibration_native::cancel();
            }
        }
    }
}

pub(crate) fn install() -> crate::Result<()> {
    let mut mods: Vec<Box<dyn Hooks>> = vec![Box::new(Picker)];
    #[cfg(feature = "native-touch-mod")]
    mods.push(Box::new(Touch));
    #[cfg(feature = "native-device-vibration-mod")]
    mods.push(Box::new(DeviceVibration));
    mod_api::MODS
        .set(mods.into())
        .map_err(|_| "native UWP mods are already installed")?;
    mod_api::name_mods(&[
        PICKER_ID,
        #[cfg(feature = "native-touch-mod")]
        horizon_touch_native::MOD_ID,
        #[cfg(feature = "native-device-vibration-mod")]
        horizon_device_vibration_native::MOD_ID,
    ]);
    Ok(())
}
