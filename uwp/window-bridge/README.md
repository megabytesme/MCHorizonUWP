# CoreWindow and winit bridge

`CoreWindowEvents` subscribes to the host's CoreWindow and passes size, DPI,
focus, visibility, close, keyboard, UTF-16 character, pointer, wheel and raw
mouse movement events to a C callback on the UI thread. Event coordinates and
dimensions are physical pixels. The host must destroy the adapter before
releasing the CoreWindow. UTF-16 character events may be surrogate pairs, and
CoreWindow does not supply them while an IME owns keyboard input.

The experimental UWP backend in `third_party/winit` consumes that event stream,
returns a WinRT window handle and Windows display handle, and runs Horizon's
`ApplicationHandler` on the CoreWindow dispatcher thread. The host provides
`horizon_uwp_window_handle`, `horizon_uwp_pump_events`,
`horizon_uwp_pop_event`, `horizon_uwp_get_metrics`,
`horizon_uwp_set_cursor_visible` and `horizon_uwp_set_cursor_position`.
`HORIZON_UWP_GAME_LIB` selects this mode in the native build script.

This is still experimental. Many desktop window operations cannot be applied
to a UWP CoreWindow; keyboard scan-code and modifier coverage, IME, cursor
confinement and application suspension need additional work. The event queue
is bounded and discards its oldest event if 4095 events accumulate. The UI
loop currently polls the dispatcher every millisecond instead of implementing
all winit wait and wake semantics. The UWP backend has not yet been validated
with every supported Horizon input or multiple windows.

The x64 `uwp/winit-probe` uses this backend to create a wgpu surface through
ANGLE/D3D11. A strict UWP link succeeded and its `queue.present` path wrote
`frame-presented.txt` in the probe app's LocalState. The probe is not Horizon.
