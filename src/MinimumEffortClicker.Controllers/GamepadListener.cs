using MinimumEffortClicker.Core;
using SDL;
using static SDL.SDL3;

namespace MinimumEffortClicker.Controllers;

/// <summary>
/// Reads every connected game controller through SDL3 on its own thread and reports button presses: face buttons,
/// bumpers, D-pad, View/Menu, stick clicks, the Steam Deck's four back buttons, and the triggers and stick
/// directions as buttons once pushed far enough. It also hears controllers while another window is in front, since
/// the clicker is used beside a game. Reading only: nothing here sends input.
/// </summary>
public sealed unsafe class GamepadListener : IDisposable
{
    // Triggers and sticks count as pressed past the first value and as released below the second, so a value
    // resting near one threshold cannot flicker.
    private const short TriggerDown = 16000, TriggerUp = 12000, StickDown = 20000, StickUp = 14000;

    private readonly Thread thread;
    private volatile bool stopping;
    private readonly Dictionary<SDL_JoystickID, nint> open = [];
    // Which buttons each controller holds, and how many controllers hold each button: with two controllers the
    // button is down while either holds it.
    private readonly Dictionary<SDL_JoystickID, HashSet<ControllerButton>> held = [];
    private readonly Dictionary<ControllerButton, int> holders = [];
    private readonly object gate = new();
    private string[] connected = [];

    /// <summary>A button went down (true) or up (false). Raised on the controller thread.</summary>
    public event Action<ControllerButton, bool>? ButtonChanged;
    /// <summary>A controller was connected or removed. Raised on the controller thread.</summary>
    public event Action? DevicesChanged;

    /// <summary>The names of the connected controllers.</summary>
    public IReadOnlyList<string> Connected { get { lock (gate) return connected; } }
    /// <summary>Why controllers cannot be read (SDL could not start), or null.</summary>
    public string? Error { get; private set; }

    public GamepadListener()
    {
        thread = new Thread(Run) { IsBackground = true, Name = "Controller input" };
        thread.Start();
    }

    private void Run()
    {
        try
        {
            SDL_SetHint("SDL_JOYSTICK_ALLOW_BACKGROUND_EVENTS", "1");
            if (!SDL_Init(SDL_InitFlags.SDL_INIT_GAMEPAD)) { Error = "Controllers cannot be read: " + SDL_GetError(); DevicesChanged?.Invoke(); return; }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            Error = "Controllers cannot be read: the SDL3 library is missing."; DevicesChanged?.Invoke(); return;
        }
        try
        {
            SDL_Event e;
            while (!stopping)
                if (SDL_WaitEventTimeout(&e, 100)) Handle(e);
        }
        finally
        {
            foreach (var gamepad in open.Values) SDL_CloseGamepad((SDL_Gamepad*)gamepad);
            open.Clear();
            SDL_Quit();
        }
    }

    private void Handle(SDL_Event e)
    {
        switch ((SDL_EventType)e.type)
        {
            case SDL_EventType.SDL_EVENT_GAMEPAD_ADDED:
                if (!open.ContainsKey(e.gdevice.which) && SDL_OpenGamepad(e.gdevice.which) is var gamepad && gamepad != null)
                { open[e.gdevice.which] = (nint)gamepad; RefreshNames(); }
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_REMOVED:
                if (open.Remove(e.gdevice.which, out var removed))
                {
                    // A controller that disappears lets go of everything it held.
                    if (held.Remove(e.gdevice.which, out var buttons)) foreach (var button in buttons) Release(button);
                    SDL_CloseGamepad((SDL_Gamepad*)removed); RefreshNames();
                }
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_DOWN or SDL_EventType.SDL_EVENT_GAMEPAD_BUTTON_UP:
                if (ButtonOf((SDL_GamepadButton)e.gbutton.button) is { } pressed) Set(e.gbutton.which, pressed, e.gbutton.down);
                break;
            case SDL_EventType.SDL_EVENT_GAMEPAD_AXIS_MOTION:
                Axis(e.gaxis.which, (SDL_GamepadAxis)e.gaxis.axis, e.gaxis.value);
                break;
        }
    }

    private void Axis(SDL_JoystickID which, SDL_GamepadAxis axis, short value)
    {
        switch (axis)
        {
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFT_TRIGGER: Threshold(which, ControllerButton.LeftTrigger, value, TriggerDown, TriggerUp); break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHT_TRIGGER: Threshold(which, ControllerButton.RightTrigger, value, TriggerDown, TriggerUp); break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTX: Stick(which, ControllerButton.LeftStickLeft, ControllerButton.LeftStickRight, value); break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_LEFTY: Stick(which, ControllerButton.LeftStickUp, ControllerButton.LeftStickDown, value); break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTX: Stick(which, ControllerButton.RightStickLeft, ControllerButton.RightStickRight, value); break;
            case SDL_GamepadAxis.SDL_GAMEPAD_AXIS_RIGHTY: Stick(which, ControllerButton.RightStickUp, ControllerButton.RightStickDown, value); break;
        }
    }

    // SDL's sticks point negative for up and left.
    private void Stick(SDL_JoystickID which, ControllerButton negative, ControllerButton positive, short value)
    {
        Threshold(which, negative, value == short.MinValue ? short.MaxValue : (short)-value, StickDown, StickUp);
        Threshold(which, positive, value, StickDown, StickUp);
    }

    private void Threshold(SDL_JoystickID which, ControllerButton button, short value, short down, short up)
    {
        bool isDown = held.TryGetValue(which, out var buttons) && buttons.Contains(button);
        if (!isDown && value >= down) Set(which, button, true);
        else if (isDown && value <= up) Set(which, button, false);
    }

    private void Set(SDL_JoystickID which, ControllerButton button, bool down)
    {
        if (!held.TryGetValue(which, out var buttons)) held[which] = buttons = [];
        if (down ? !buttons.Add(button) : !buttons.Remove(button)) return;
        if (down) { holders[button] = holders.GetValueOrDefault(button) + 1; if (holders[button] == 1) Raise(button, true); }
        else Release(button);
    }

    private void Release(ControllerButton button)
    {
        int left = holders.GetValueOrDefault(button) - 1;
        if (left > 0) { holders[button] = left; return; }
        holders.Remove(button);
        Raise(button, false);
    }

    private void Raise(ControllerButton button, bool down)
    {
        // A listener that throws must not end controller reading for everything else.
        try { ButtonChanged?.Invoke(button, down); }
        catch (Exception) { }
    }

    private void RefreshNames()
    {
        var names = open.Values.Select(g => SDL_GetGamepadName((SDL_Gamepad*)g) ?? "Controller").ToArray();
        lock (gate) connected = names;
        try { DevicesChanged?.Invoke(); }
        catch (Exception) { }
    }

    private static ControllerButton? ButtonOf(SDL_GamepadButton button) => button switch
    {
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_SOUTH => ControllerButton.A,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_EAST => ControllerButton.B,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_WEST => ControllerButton.X,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_NORTH => ControllerButton.Y,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_BACK => ControllerButton.View,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_START => ControllerButton.Menu,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_STICK => ControllerButton.LeftStickPress,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_STICK => ControllerButton.RightStickPress,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_SHOULDER => ControllerButton.LeftBumper,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_SHOULDER => ControllerButton.RightBumper,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_UP => ControllerButton.DPadUp,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_DOWN => ControllerButton.DPadDown,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_LEFT => ControllerButton.DPadLeft,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_DPAD_RIGHT => ControllerButton.DPadRight,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE1 => ControllerButton.L4,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE1 => ControllerButton.R4,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_LEFT_PADDLE2 => ControllerButton.L5,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_RIGHT_PADDLE2 => ControllerButton.R5,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_MISC1 => ControllerButton.Share,
        SDL_GamepadButton.SDL_GAMEPAD_BUTTON_TOUCHPAD => ControllerButton.Touchpad,
        // The Guide / Steam button belongs to the system and Steam; it is never bound.
        _ => null
    };

    public void Dispose()
    {
        stopping = true;
        if (Thread.CurrentThread != thread) thread.Join(TimeSpan.FromSeconds(2));
    }
}
