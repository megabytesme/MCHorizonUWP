# Experimental UWP XAudio2 output

This opt-in backend connects the existing Horizon software mixer to the UWP
host's XAudio2 source voice. It uses stereo 32-bit float samples at 48 kHz and
three 1,024-frame buffers. The standard UWP build remains silent unless the
`uwp-audio` feature is enabled and the corresponding host object is linked.

Build `uwp/game-bridge` for the UWP target with `uwp-audio` in addition to the
renderer features, then set `HORIZON_UWP_XAUDIO=1` while running
`uwp/host/build-game.cmd`. Both settings are required; mixing an audio-enabled
Rust library with an ordinary host fails at link time. XAudio2 is supplied by
Windows 10, with the SDK's `xaudio2.lib` linked by the strict host linker.
Minecraft's sound event metadata and audio files are separate resources and
are not included in this source tree.

The backend currently opens only the default output. Device changes, voice
errors and user-selected output devices do not yet trigger a stream restart.
The user confirmed that sounds and gameplay worked on the Lumia 950 XL with
an experimental ARM32 package. Output-device changes remain untested.
