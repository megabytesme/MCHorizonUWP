using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

public sealed class ScratchKeyboardInput {
    delegate bool EnumCallback(IntPtr window, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumCallback callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr window, EnumCallback callback, IntPtr param);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window, int command);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr window, StringBuilder buffer, int capacity);
    [DllImport("user32.dll")] static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] static extern IntPtr GetKeyboardLayout(uint thread);
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr window);
    [DllImport("user32.dll")] static extern void SwitchToThisWindow(IntPtr window, bool altTab);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool GetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPlacement(IntPtr window, ref WindowPlacement placement);
    [DllImport("user32.dll", SetLastError = true)]
    static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr window, out Rect rect);
    [DllImport("user32.dll", SetLastError = true)] static extern bool PostMessageW(IntPtr window, uint message, UIntPtr wparam, IntPtr lparam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)] static extern short VkKeyScanExW(char character, IntPtr layout);
    [DllImport("user32.dll")] static extern uint MapVirtualKeyExW(uint key, uint map, IntPtr layout);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint length, Input[] events, int size);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [StructLayout(LayoutKind.Sequential)] public struct Rect {
        public int Left, Top, Right, Bottom;
    }
    [StructLayout(LayoutKind.Sequential)] struct WindowPlacement {
        public uint Length, Flags, ShowCommand;
        public int MinX, MinY, MaxX, MaxY;
        public Rect Normal;
    }
    WindowPlacement priorWindowPlacement;
    bool savedWindowPlacement;
    [StructLayout(LayoutKind.Sequential)] struct KeyboardEvent {
        public ushort VirtualKey, ScanCode;
        public uint Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Sequential)] struct MouseEvent {
        public int X, Y;
        public uint Data, Flags, Time;
        public UIntPtr ExtraInfo;
    }
    [StructLayout(LayoutKind.Explicit)] struct InputEvent {
        [FieldOffset(0)] public KeyboardEvent Keyboard;
        [FieldOffset(0)] public MouseEvent Mouse;
    }
    [StructLayout(LayoutKind.Sequential)] struct Input {
        public uint Type;
        public InputEvent Event;
    }
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint source, uint destination, bool attach);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr window);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    readonly IntPtr frame;
    readonly IntPtr game;

    public static void EnablePhysicalPixels() { SetProcessDpiAwarenessContext(new IntPtr(-4)); }

    static int FramePreference(IntPtr window, IntPtr foreground) {
        if (window == foreground) return 3;
        var name = new StringBuilder(128);
        GetClassName(window, name, name.Capacity);
        return name.ToString() == "ApplicationFrameWindow" ? 2 : 1;
    }

    public ScratchKeyboardInput(int processId) {
        IntPtr foundFrame = IntPtr.Zero, foundGame = IntPtr.Zero;
        IntPtr foreground = GetForegroundWindow();
        int best = 0;
        EnumWindows((window, _) => {
            if (!IsWindowVisible(window)) return true;
            int score = FramePreference(window, foreground);
            if (score <= best) return true;
            uint owner;
            GetWindowThreadProcessId(window, out owner);
            if (owner == processId) {
                foundFrame = window;
                foundGame = window;
                best = score;
                return true;
            }
            EnumChildWindows(window, (child, unused) => {
                GetWindowThreadProcessId(child, out owner);
                if (owner != processId || !IsWindowVisible(child)) return true;
                foundFrame = window;
                foundGame = child;
                best = score;
                return false;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        if (foundFrame == IntPtr.Zero) throw new InvalidOperationException("Could not find a visible scratch game window.");
        frame = foundFrame;
        game = foundGame;
    }

    IntPtr Layout() {
        uint process;
        return GetKeyboardLayout(GetWindowThreadProcessId(game, out process));
    }
    public string LayoutName() { return ((uint)Layout().ToInt64()).ToString("X8"); }
    public int Character(char character) {
        short mapping = VkKeyScanExW(character, Layout());
        if (mapping == -1) throw new InvalidOperationException("Character has no key in the active layout.");
        return (ushort)mapping;
    }
    public void Focus() {
        if (IsIconic(frame)) ShowWindow(frame, 9);
        if (GetForegroundWindow() != frame && !SetForegroundWindow(frame)) {
            uint owner;
            uint foregroundThread = GetWindowThreadProcessId(GetForegroundWindow(), out owner);
            uint currentThread = GetCurrentThreadId();
            if (foregroundThread != 0 && AttachThreadInput(currentThread, foregroundThread, true)) {
                try {
                    BringWindowToTop(frame);
                    SetForegroundWindow(frame);
                }
                finally { AttachThreadInput(currentThread, foregroundThread, false); }
            }
            if (GetForegroundWindow() != frame) SwitchToThisWindow(frame, true);
        }
        Thread.Sleep(300);
        CheckForeground();
    }
    public void Minimize() {
        CheckForeground();
        ShowWindow(frame, 6);
        Thread.Sleep(350);
        if (GetForegroundWindow() == frame) throw new InvalidOperationException("The game did not lose foreground focus.");
    }
    public void ResizeForTest(int width, int height) {
        CheckForeground();
        if (savedWindowPlacement) throw new InvalidOperationException("Restore the previous test placement first.");
        priorWindowPlacement.Length = (uint)Marshal.SizeOf(typeof(WindowPlacement));
        if (!GetWindowPlacement(frame, ref priorWindowPlacement))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        savedWindowPlacement = true;
        ShowWindow(frame, 9);
        // Change only this window's size, keeping its position and z-order.
        if (!SetWindowPos(frame, IntPtr.Zero, 0, 0, width, height, 2 | 4 | 16))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }
    public void RestoreTestPlacement() {
        if (!savedWindowPlacement) return;
        if (!SetWindowPlacement(frame, ref priorWindowPlacement))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        savedWindowPlacement = false;
    }
    public void CheckForeground() {
        IntPtr foreground = GetForegroundWindow();
        if (foreground != frame && GetAncestor(foreground, 2) != frame)
            throw new InvalidOperationException("Keyboard test stopped because its own game window is no longer in the foreground.");
    }
    public IntPtr FrameHandle() { return frame; }
    public void Close() {
        if (!PostMessageW(frame, 0x0010, UIntPtr.Zero, IntPtr.Zero))
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The scratch window could not be closed.");
    }
    public Rect Bounds() {
        CheckForeground();
        Rect rect;
        if (!GetWindowRect(frame, out rect)) throw new InvalidOperationException("Could not read scratch game bounds.");
        return rect;
    }
    public uint Scan(int virtualKey) {
        uint scan = MapVirtualKeyExW((uint)virtualKey, 4, Layout());
        if (scan == 0) throw new InvalidOperationException("Virtual key has no physical scan code.");
        return scan;
    }
    public void Key(uint scan, bool down, bool cleanup) {
        if (!cleanup) CheckForeground();
        else if (down) throw new ArgumentException("Cleanup may only release a key.");
        uint flags = 8;
        if ((scan & 0xff00) == 0xe000) flags |= 1;
        if (!down) flags |= 2;
        var keyboard = new KeyboardEvent { ScanCode = (ushort)(scan & 0xff), Flags = flags };
        var input = new Input { Type = 1, Event = new InputEvent { Keyboard = keyboard } };
        if (SendInput(1, new[] { input }, Marshal.SizeOf(typeof(Input))) != 1)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "The foreground input injection failed.");
    }
}
