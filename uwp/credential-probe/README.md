# UWP Credential Locker probe

This standalone app tests the Windows PasswordVault API with one uniquely named
synthetic credential. It stores the value, reads it back and attempts to remove
only that test credential. No Microsoft account credentials are read or used.
If cleanup fails, the dialog reports that the synthetic value may remain in the
probe app's own credential locker.

The x64 UWP probe reported a successful round trip and cleanup on the desktop.
The ARM32 probe compiled and passed the 15063 strict link audit. Successful
operation on Windows 10 Mobile remains to be tested. A working locker is one
prerequisite for optional Microsoft sign-in, not evidence that sign-in works.

`App.cpp` is designed for the same C++/CX UWP host settings and SDK 15063 as
`uwp/host/build-game.cmd`. Package it under its own identity with generated
logos and the VCLibs dependency; never run the test against the real game
package identity or use an actual access or refresh token as its test value.
