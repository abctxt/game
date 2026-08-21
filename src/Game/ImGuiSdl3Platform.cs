using System.Numerics;
using ImGuiNET;
using SDL3;


namespace Game;

/// <summary>
/// SDL3 platform backend for ImGui.NET, adapted from
/// https://github.com/behindcurtain3/SDL3-ImGui
/// </summary>
internal sealed class ImGuiSdl3Platform : IDisposable
{
    private const byte MouseButtonLeft = 1;
    private const byte MouseButtonMiddle = 2;
    private const byte MouseButtonRight = 3;
    private const byte MouseButtonX1 = 4;
    private const byte MouseButtonX2 = 5;

    private readonly nint _window;
    private readonly uint _windowId;
    private uint _mouseWindowId;
    private int _mousePendingLeaveFrame;
    private int _mouseButtonsDown;

    public ImGuiSdl3Platform(nint window)
    {
        _window = window;
        _windowId = SDL.GetWindowID(window);

        var io = ImGui.GetIO();
        io.BackendFlags |= ImGuiBackendFlags.HasSetMousePos;

        var viewport = ImGui.GetMainViewport();
        viewport.PlatformHandle = (nint)_windowId;
    }

    public void Dispose()
    { }

    public void NewFrame()
    {
        var io = ImGui.GetIO();

        SDL.GetWindowSize(_window, out var width, out var height);
        if (SDL.GetWindowFlags(_window).HasFlag(SDL.WindowFlags.Minimized)) {
            width = 0;
            height = 0;
        }

        SDL.GetWindowSizeInPixels(_window, out var displayWidth, out var displayHeight);
        io.DisplaySize = new Vector2(width, height);
        if (width > 0 && height > 0) {
            io.DisplayFramebufferScale = new Vector2((float)displayWidth / width, (float)displayHeight / height);
        }

        if (_mousePendingLeaveFrame > 0 && _mousePendingLeaveFrame >= ImGui.GetFrameCount()) {
            _mouseWindowId = 0;
            _mousePendingLeaveFrame = 0;
            io.AddMousePosEvent(-float.MaxValue, -float.MaxValue);
        }

        UpdateMouseData();
    }

    public bool ProcessEvent(SDL.Event ev)
    {
        var io = ImGui.GetIO();

        switch ((SDL.EventType)ev.Type) {
        case SDL.EventType.MouseMotion:
            if (!IsOurWindow(ev.Motion.WindowID)) {
                return false;
            }

            io.AddMouseSourceEvent(ev.Motion.Which == SDL.TouchMouseID ? ImGuiMouseSource.TouchScreen : ImGuiMouseSource.Mouse);
            io.AddMousePosEvent(ev.Motion.X, ev.Motion.Y);
            return true;

        case SDL.EventType.MouseWheel:
            if (!IsOurWindow(ev.Wheel.WindowID)) {
                return false;
            }

            io.AddMouseSourceEvent(ev.Wheel.Which == SDL.TouchMouseID ? ImGuiMouseSource.TouchScreen : ImGuiMouseSource.Mouse);
            io.AddMouseWheelEvent(-ev.Wheel.X, ev.Wheel.Y);
            return true;

        case SDL.EventType.MouseButtonDown:
        case SDL.EventType.MouseButtonUp:
            if (!IsOurWindow(ev.Button.WindowID)) {
                return false;
            }

            var mouseButton = ev.Button.Button switch {
                MouseButtonLeft => 0,
                MouseButtonRight => 1,
                MouseButtonMiddle => 2,
                MouseButtonX1 => 3,
                MouseButtonX2 => 4,
                _ => -1,
            };
            if (mouseButton < 0) {
                return false;
            }

            var pressed = (SDL.EventType)ev.Type == SDL.EventType.MouseButtonDown;
            io.AddMouseSourceEvent(ev.Button.Which == SDL.TouchMouseID ? ImGuiMouseSource.TouchScreen : ImGuiMouseSource.Mouse);
            io.AddMouseButtonEvent(mouseButton, pressed);
            _mouseButtonsDown = pressed
                ? _mouseButtonsDown | (1 << mouseButton)
                : _mouseButtonsDown & ~(1 << mouseButton);
            return true;

        case SDL.EventType.TextInput:
            if (!IsOurWindow(ev.Text.WindowID)) {
                return false;
            }

            unsafe {
                var text = ev.Text.Text;
                if (text != IntPtr.Zero) {
                    ImGuiNative.ImGuiIO_AddInputCharactersUTF8(io.NativePtr, (byte*)text);
                }
            }

            return true;

        case SDL.EventType.KeyDown:
        case SDL.EventType.KeyUp:
            if (!IsOurWindow(ev.Key.WindowID)) {
                return false;
            }

            UpdateKeyModifiers(ev.Key.Mod);
            var key = MapKey(ev.Key.Key);
            io.AddKeyEvent(key, (SDL.EventType)ev.Type == SDL.EventType.KeyDown);
            io.SetKeyEventNativeData(key, (int)ev.Key.Key, (int)ev.Key.Scancode, (int)ev.Key.Scancode);
            return true;

        case SDL.EventType.WindowMouseEnter:
            if (!IsOurWindow(ev.Window.WindowID)) {
                return false;
            }

            _mouseWindowId = ev.Window.WindowID;
            _mousePendingLeaveFrame = 0;
            return true;

        case SDL.EventType.WindowMouseLeave:
            if (!IsOurWindow(ev.Window.WindowID)) {
                return false;
            }

            _mousePendingLeaveFrame = ImGui.GetFrameCount() + 1;
            return true;

        case SDL.EventType.WindowFocusGained:
        case SDL.EventType.WindowFocusLost:
            if (!IsOurWindow(ev.Window.WindowID)) {
                return false;
            }

            io.AddFocusEvent((SDL.EventType)ev.Type == SDL.EventType.WindowFocusGained);
            return true;

        default:
            return false;
        }
    }

    private bool IsOurWindow(uint windowId) => windowId == 0 || windowId == _windowId;

    private void UpdateMouseData()
    {
        var io = ImGui.GetIO();
        if (SDL.GetKeyboardFocus() != _window) {
            return;
        }

        if (io.WantSetMousePos) {
            SDL.WarpMouseInWindow(_window, io.MousePos.X, io.MousePos.Y);
        } else if (_mouseWindowId == 0 && _mouseButtonsDown == 0) {
            SDL.GetGlobalMouseState(out var x, out var y);
            SDL.GetWindowPosition(_window, out var windowX, out var windowY);
            io.AddMousePosEvent(x - windowX, y - windowY);
        }
    }

    private static void UpdateKeyModifiers(SDL.Keymod keymods)
    {
        var io = ImGui.GetIO();
        io.AddKeyEvent(ImGuiKey.ModCtrl, keymods.HasFlag(SDL.Keymod.Ctrl));
        io.AddKeyEvent(ImGuiKey.ModShift, keymods.HasFlag(SDL.Keymod.Shift));
        io.AddKeyEvent(ImGuiKey.ModAlt, keymods.HasFlag(SDL.Keymod.Alt));
        io.AddKeyEvent(ImGuiKey.ModSuper, keymods.HasFlag(SDL.Keymod.GUI));
    }

    private static ImGuiKey MapKey(SDL.Keycode keycode) => keycode switch {
        SDL.Keycode.Tab => ImGuiKey.Tab,
        SDL.Keycode.Left => ImGuiKey.LeftArrow,
        SDL.Keycode.Right => ImGuiKey.RightArrow,
        SDL.Keycode.Up => ImGuiKey.UpArrow,
        SDL.Keycode.Down => ImGuiKey.DownArrow,
        SDL.Keycode.Pageup => ImGuiKey.PageUp,
        SDL.Keycode.Pagedown => ImGuiKey.PageDown,
        SDL.Keycode.Home => ImGuiKey.Home,
        SDL.Keycode.End => ImGuiKey.End,
        SDL.Keycode.Insert => ImGuiKey.Insert,
        SDL.Keycode.Delete => ImGuiKey.Delete,
        SDL.Keycode.Backspace => ImGuiKey.Backspace,
        SDL.Keycode.Space => ImGuiKey.Space,
        SDL.Keycode.Return => ImGuiKey.Enter,
        SDL.Keycode.Escape => ImGuiKey.Escape,
        SDL.Keycode.Apostrophe => ImGuiKey.Apostrophe,
        SDL.Keycode.Comma => ImGuiKey.Comma,
        SDL.Keycode.Minus => ImGuiKey.Minus,
        SDL.Keycode.Period => ImGuiKey.Period,
        SDL.Keycode.Slash => ImGuiKey.Slash,
        SDL.Keycode.Semicolon => ImGuiKey.Semicolon,
        SDL.Keycode.Equals => ImGuiKey.Equal,
        SDL.Keycode.LeftBracket => ImGuiKey.LeftBracket,
        SDL.Keycode.Backslash => ImGuiKey.Backslash,
        SDL.Keycode.RightBracket => ImGuiKey.RightBracket,
        SDL.Keycode.Grave => ImGuiKey.GraveAccent,
        SDL.Keycode.Capslock => ImGuiKey.CapsLock,
        SDL.Keycode.ScrollLock => ImGuiKey.ScrollLock,
        SDL.Keycode.NumLockClear => ImGuiKey.NumLock,
        SDL.Keycode.PrintScreen => ImGuiKey.PrintScreen,
        SDL.Keycode.Pause => ImGuiKey.Pause,
        SDL.Keycode.LCtrl => ImGuiKey.LeftCtrl,
        SDL.Keycode.LShift => ImGuiKey.LeftShift,
        SDL.Keycode.LAlt => ImGuiKey.LeftAlt,
        SDL.Keycode.LGUI => ImGuiKey.LeftSuper,
        SDL.Keycode.RCtrl => ImGuiKey.RightCtrl,
        SDL.Keycode.RShift => ImGuiKey.RightShift,
        SDL.Keycode.RAlt => ImGuiKey.RightAlt,
        SDL.Keycode.RGUI => ImGuiKey.RightSuper,
        SDL.Keycode.Application => ImGuiKey.Menu,
        SDL.Keycode.Alpha0 => ImGuiKey._0,
        SDL.Keycode.Alpha1 => ImGuiKey._1,
        SDL.Keycode.Alpha2 => ImGuiKey._2,
        SDL.Keycode.Alpha3 => ImGuiKey._3,
        SDL.Keycode.Alpha4 => ImGuiKey._4,
        SDL.Keycode.Alpha5 => ImGuiKey._5,
        SDL.Keycode.Alpha6 => ImGuiKey._6,
        SDL.Keycode.Alpha7 => ImGuiKey._7,
        SDL.Keycode.Alpha8 => ImGuiKey._8,
        SDL.Keycode.Alpha9 => ImGuiKey._9,
        SDL.Keycode.A => ImGuiKey.A,
        SDL.Keycode.B => ImGuiKey.B,
        SDL.Keycode.C => ImGuiKey.C,
        SDL.Keycode.D => ImGuiKey.D,
        SDL.Keycode.E => ImGuiKey.E,
        SDL.Keycode.F => ImGuiKey.F,
        SDL.Keycode.G => ImGuiKey.G,
        SDL.Keycode.H => ImGuiKey.H,
        SDL.Keycode.I => ImGuiKey.I,
        SDL.Keycode.J => ImGuiKey.J,
        SDL.Keycode.K => ImGuiKey.K,
        SDL.Keycode.L => ImGuiKey.L,
        SDL.Keycode.M => ImGuiKey.M,
        SDL.Keycode.N => ImGuiKey.N,
        SDL.Keycode.O => ImGuiKey.O,
        SDL.Keycode.P => ImGuiKey.P,
        SDL.Keycode.Q => ImGuiKey.Q,
        SDL.Keycode.R => ImGuiKey.R,
        SDL.Keycode.S => ImGuiKey.S,
        SDL.Keycode.T => ImGuiKey.T,
        SDL.Keycode.U => ImGuiKey.U,
        SDL.Keycode.V => ImGuiKey.V,
        SDL.Keycode.W => ImGuiKey.W,
        SDL.Keycode.X => ImGuiKey.X,
        SDL.Keycode.Y => ImGuiKey.Y,
        SDL.Keycode.Z => ImGuiKey.Z,
        SDL.Keycode.F1 => ImGuiKey.F1,
        SDL.Keycode.F2 => ImGuiKey.F2,
        SDL.Keycode.F3 => ImGuiKey.F3,
        SDL.Keycode.F4 => ImGuiKey.F4,
        SDL.Keycode.F5 => ImGuiKey.F5,
        SDL.Keycode.F6 => ImGuiKey.F6,
        SDL.Keycode.F7 => ImGuiKey.F7,
        SDL.Keycode.F8 => ImGuiKey.F8,
        SDL.Keycode.F9 => ImGuiKey.F9,
        SDL.Keycode.F10 => ImGuiKey.F10,
        SDL.Keycode.F11 => ImGuiKey.F11,
        SDL.Keycode.F12 => ImGuiKey.F12,
        SDL.Keycode.Kp0 => ImGuiKey.Keypad0,
        SDL.Keycode.Kp1 => ImGuiKey.Keypad1,
        SDL.Keycode.Kp2 => ImGuiKey.Keypad2,
        SDL.Keycode.Kp3 => ImGuiKey.Keypad3,
        SDL.Keycode.Kp4 => ImGuiKey.Keypad4,
        SDL.Keycode.Kp5 => ImGuiKey.Keypad5,
        SDL.Keycode.Kp6 => ImGuiKey.Keypad6,
        SDL.Keycode.Kp7 => ImGuiKey.Keypad7,
        SDL.Keycode.Kp8 => ImGuiKey.Keypad8,
        SDL.Keycode.Kp9 => ImGuiKey.Keypad9,
        SDL.Keycode.KpPeriod => ImGuiKey.KeypadDecimal,
        SDL.Keycode.KpDivide => ImGuiKey.KeypadDivide,
        SDL.Keycode.KpMultiply => ImGuiKey.KeypadMultiply,
        SDL.Keycode.KpMinus => ImGuiKey.KeypadSubtract,
        SDL.Keycode.KpPlus => ImGuiKey.KeypadAdd,
        SDL.Keycode.KpEnter => ImGuiKey.KeypadEnter,
        SDL.Keycode.KpEquals => ImGuiKey.KeypadEqual,
        SDL.Keycode.AcBack => ImGuiKey.AppBack,
        SDL.Keycode.AcForward => ImGuiKey.AppForward,
        _ => ImGuiKey.None,
    };
}
