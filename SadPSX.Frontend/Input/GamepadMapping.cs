using SadPSX.Core.Controllers;
using SDL3;

namespace SadPSX.Frontend.Input;

internal sealed record GamepadMapping(
    GamepadBinding Select = GamepadBinding.Back,
    GamepadBinding L3 = GamepadBinding.LeftStick,
    GamepadBinding R3 = GamepadBinding.RightStick,
    GamepadBinding Start = GamepadBinding.Start,
    GamepadBinding Up = GamepadBinding.DPadUp,
    GamepadBinding Right = GamepadBinding.DPadRight,
    GamepadBinding Down = GamepadBinding.DPadDown,
    GamepadBinding Left = GamepadBinding.DPadLeft,
    GamepadBinding L2 = GamepadBinding.LeftTrigger,
    GamepadBinding R2 = GamepadBinding.RightTrigger,
    GamepadBinding L1 = GamepadBinding.LeftShoulder,
    GamepadBinding R1 = GamepadBinding.RightShoulder,
    GamepadBinding Triangle = GamepadBinding.North,
    GamepadBinding Circle = GamepadBinding.East,
    GamepadBinding Cross = GamepadBinding.South,
    GamepadBinding Square = GamepadBinding.West)
{
    public static GamepadMapping Default { get; } = new();

    public GamepadBinding GetBinding(ControllerButton button) => button switch
    {
        ControllerButton.Select => Select,
        ControllerButton.L3 => L3,
        ControllerButton.R3 => R3,
        ControllerButton.Start => Start,
        ControllerButton.Up => Up,
        ControllerButton.Right => Right,
        ControllerButton.Down => Down,
        ControllerButton.Left => Left,
        ControllerButton.L2 => L2,
        ControllerButton.R2 => R2,
        ControllerButton.L1 => L1,
        ControllerButton.R1 => R1,
        ControllerButton.Triangle => Triangle,
        ControllerButton.Circle => Circle,
        ControllerButton.Cross => Cross,
        ControllerButton.Square => Square,
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };

    public GamepadMapping Rebind(
        ControllerButton target,
        GamepadBinding binding)
    {
        GamepadBinding previous = GetBinding(target);
        ControllerButton? conflict = null;
        foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
        {
            if (button != target && GetBinding(button) == binding)
            {
                conflict = button;
                break;
            }
        }
        GamepadMapping mapping = SetBinding(target, binding);
        return conflict is ControllerButton conflictButton
            ? mapping.SetBinding(conflictButton, previous)
            : mapping;
    }

    public GamepadMapping Normalize()
    {
        GamepadMapping normalized = this;
        foreach (ControllerButton button in Enum.GetValues<ControllerButton>())
        {
            GamepadBinding binding = normalized.GetBinding(button);
            if (!Enum.IsDefined(binding))
            {
                normalized = normalized.SetBinding(
                    button,
                    Default.GetBinding(button));
            }
        }

        return normalized;
    }

    public static bool TryGetButton(
        GamepadBinding binding,
        out SDL.GamepadButton button)
    {
        button = binding switch
        {
            GamepadBinding.South => SDL.GamepadButton.South,
            GamepadBinding.East => SDL.GamepadButton.East,
            GamepadBinding.West => SDL.GamepadButton.West,
            GamepadBinding.North => SDL.GamepadButton.North,
            GamepadBinding.Back => SDL.GamepadButton.Back,
            GamepadBinding.Start => SDL.GamepadButton.Start,
            GamepadBinding.LeftStick => SDL.GamepadButton.LeftStick,
            GamepadBinding.RightStick => SDL.GamepadButton.RightStick,
            GamepadBinding.LeftShoulder => SDL.GamepadButton.LeftShoulder,
            GamepadBinding.RightShoulder => SDL.GamepadButton.RightShoulder,
            GamepadBinding.DPadUp => SDL.GamepadButton.DPadUp,
            GamepadBinding.DPadRight => SDL.GamepadButton.DPadRight,
            GamepadBinding.DPadDown => SDL.GamepadButton.DPadDown,
            GamepadBinding.DPadLeft => SDL.GamepadButton.DPadLeft,
            _ => default,
        };
        return binding is
            GamepadBinding.South or GamepadBinding.East or
            GamepadBinding.West or GamepadBinding.North or
            GamepadBinding.Back or GamepadBinding.Start or
            GamepadBinding.LeftStick or GamepadBinding.RightStick or
            GamepadBinding.LeftShoulder or GamepadBinding.RightShoulder or
            GamepadBinding.DPadUp or GamepadBinding.DPadRight or
            GamepadBinding.DPadDown or GamepadBinding.DPadLeft;
    }

    public static bool TryGetTrigger(
        GamepadBinding binding,
        out SDL.GamepadAxis axis)
    {
        axis = binding switch
        {
            GamepadBinding.LeftTrigger => SDL.GamepadAxis.LeftTrigger,
            GamepadBinding.RightTrigger => SDL.GamepadAxis.RightTrigger,
            _ => default,
        };
        return binding is GamepadBinding.LeftTrigger or
            GamepadBinding.RightTrigger;
    }

    public static bool TryFromButton(
        SDL.GamepadButton button,
        out GamepadBinding binding)
    {
        binding = button switch
        {
            SDL.GamepadButton.South => GamepadBinding.South,
            SDL.GamepadButton.East => GamepadBinding.East,
            SDL.GamepadButton.West => GamepadBinding.West,
            SDL.GamepadButton.North => GamepadBinding.North,
            SDL.GamepadButton.Back => GamepadBinding.Back,
            SDL.GamepadButton.Start => GamepadBinding.Start,
            SDL.GamepadButton.LeftStick => GamepadBinding.LeftStick,
            SDL.GamepadButton.RightStick => GamepadBinding.RightStick,
            SDL.GamepadButton.LeftShoulder => GamepadBinding.LeftShoulder,
            SDL.GamepadButton.RightShoulder => GamepadBinding.RightShoulder,
            SDL.GamepadButton.DPadUp => GamepadBinding.DPadUp,
            SDL.GamepadButton.DPadRight => GamepadBinding.DPadRight,
            SDL.GamepadButton.DPadDown => GamepadBinding.DPadDown,
            SDL.GamepadButton.DPadLeft => GamepadBinding.DPadLeft,
            _ => default,
        };
        return button is
            SDL.GamepadButton.South or SDL.GamepadButton.East or
            SDL.GamepadButton.West or SDL.GamepadButton.North or
            SDL.GamepadButton.Back or SDL.GamepadButton.Start or
            SDL.GamepadButton.LeftStick or SDL.GamepadButton.RightStick or
            SDL.GamepadButton.LeftShoulder or
            SDL.GamepadButton.RightShoulder or SDL.GamepadButton.DPadUp or
            SDL.GamepadButton.DPadRight or SDL.GamepadButton.DPadDown or
            SDL.GamepadButton.DPadLeft;
    }

    public static string GetDisplayName(GamepadBinding binding) =>
        binding switch
        {
            GamepadBinding.South => "SOUTH (A / CROSS)",
            GamepadBinding.East => "EAST (B / CIRCLE)",
            GamepadBinding.West => "WEST (X / SQUARE)",
            GamepadBinding.North => "NORTH (Y / TRIANGLE)",
            GamepadBinding.Back => "BACK / SHARE",
            GamepadBinding.Start => "START / OPTIONS",
            GamepadBinding.LeftStick => "LEFT STICK CLICK",
            GamepadBinding.RightStick => "RIGHT STICK CLICK",
            GamepadBinding.LeftShoulder => "LEFT SHOULDER",
            GamepadBinding.RightShoulder => "RIGHT SHOULDER",
            GamepadBinding.LeftTrigger => "LEFT TRIGGER",
            GamepadBinding.RightTrigger => "RIGHT TRIGGER",
            GamepadBinding.DPadUp => "D-PAD UP",
            GamepadBinding.DPadRight => "D-PAD RIGHT",
            GamepadBinding.DPadDown => "D-PAD DOWN",
            GamepadBinding.DPadLeft => "D-PAD LEFT",
            _ => "UNKNOWN",
        };

    private GamepadMapping SetBinding(
        ControllerButton button,
        GamepadBinding binding) => button switch
    {
        ControllerButton.Select => this with { Select = binding },
        ControllerButton.L3 => this with { L3 = binding },
        ControllerButton.R3 => this with { R3 = binding },
        ControllerButton.Start => this with { Start = binding },
        ControllerButton.Up => this with { Up = binding },
        ControllerButton.Right => this with { Right = binding },
        ControllerButton.Down => this with { Down = binding },
        ControllerButton.Left => this with { Left = binding },
        ControllerButton.L2 => this with { L2 = binding },
        ControllerButton.R2 => this with { R2 = binding },
        ControllerButton.L1 => this with { L1 = binding },
        ControllerButton.R1 => this with { R1 = binding },
        ControllerButton.Triangle => this with { Triangle = binding },
        ControllerButton.Circle => this with { Circle = binding },
        ControllerButton.Cross => this with { Cross = binding },
        ControllerButton.Square => this with { Square = binding },
        _ => throw new ArgumentOutOfRangeException(nameof(button)),
    };
}

internal enum GamepadBinding
{
    South,
    East,
    West,
    North,
    Back,
    Start,
    LeftStick,
    RightStick,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    DPadUp,
    DPadRight,
    DPadDown,
    DPadLeft,
}
