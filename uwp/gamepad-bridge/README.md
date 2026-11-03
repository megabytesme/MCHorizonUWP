# UWP controllers

The optional `uwp-controller` feature reads `Windows.Gaming.Input.Gamepad`
through the native host and feeds Horizon's existing controller mappings,
deadzones, menu controls and vibration setting. The active controller changes
when another connected controller is used. Motor pulses are time-limited and
are stopped on focus loss, disconnect and shutdown.

The host must be built with `HORIZON_UWP_GAMEPAD=1` when this feature is enabled.
The bridge supports Windows Gaming Input gamepads; availability and physical
controller behavior depend on the device and have not yet been tested on the
target phone. Device vibration is a separate optional native mod.
