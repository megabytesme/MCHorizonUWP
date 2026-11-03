# Device vibration native mod

This optional build-time UWP mod vibrates the device itself for game events.
Its **Device vibrations** setting and the mod's own on/off toggle are in
Options > Mods; neither changes the controller-vibration setting. Pulses are
limited to 250 ms, and disabling the mod, disabling its setting, losing focus
or exiting cancels pending phone requests.

The host uses the Windows Phone vibration API when present, then tries the
Windows 10 haptics API for supported devices with a built-in vibration motor.
Unsupported devices and denied access are ignored. The original phone probe
was tested on the Lumia; the integrated mod and tablet fallback still require
physical-device validation. The host must be built with
`HORIZON_UWP_DEVICE_VIBRATION=1` when the feature is enabled.
