# UWP controllers

The `uwp-controller` feature uses Horizon's `gilrs` Windows.Gaming.Input
backend. Standard `Gamepad` controllers use their mapped controls; other
controllers exposed as `RawGameController` use the bundled SDL mapping
database or gilrs' generic fallback. There is no controller allowlist. A
supplemental mapping uses the X2 layout measured on the Lumia 950 XL. Unknown
layouts may still need mapping, and system-reserved buttons may not reach the game.

Input feeds the same controller settings, action bindings, deadzones and menus
as desktop Horizon. Standard Gamepad vibration is used when available;
device-specific HID effects are separate. The old native `Gamepad.cpp` bridge
is retained for historical experiments; the game no longer links it.

The UWP build uses a local `gilrs-core` compatibility patch for Windows 10
15063, where `NonRoamableId` is unavailable. Runtime validation on the target
OS and real controllers is still needed. Device vibration is a separate mod.
