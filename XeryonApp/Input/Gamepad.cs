using System;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using System.Threading;
using SharpDX;
using SharpDX.XInput;

namespace XeryonApp.Input;

public enum Side
{
    Left = 0,
    Right = 1
}

public enum ButtonName
{
    None,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    Start,
    Back,
    LeftThumb,
    RightThumb,
    LeftShoulder,
    RightShoulder,
    A,
    B,
    X,
    Y
}

public struct Thumbstick
{
    public static readonly Thumbstick LeftZero = new(Side.Left, 0, 0),
        RightZero = new(Side.Right, 0, 0);

    public Side Side { get; }
    public double X { get; } // in range [-1, 1]
    public double Y { get; } // in range [-1, 1]

    public Thumbstick(Side side, double x, double y)
    {
        Side = side;
        X = x;
        Y = y;
    }
}

public struct Trigger
{
    public static readonly Trigger LeftZero = new(Side.Left, 0),
        RightZero = new(Side.Right, 0);

    public Side Side { get; }
    public double Value { get; } // in range [0, 1]

    public Trigger(Side side, double value)
    {
        Side = side;
        Value = value;
    }
}

public struct Button
{
    public ButtonName ButtonName { get; }
    public bool IsPressed { get; } // true = down, false = up

    public Button(ButtonName buttonName, bool isPressed)
    {
        ButtonName = buttonName;
        IsPressed = isPressed;
    }
}

public struct Deadzone
{
    public double Min { get; set; }
    public double Max { get; set; }

    public Deadzone(double min, double max)
    {
        Min = min;
        Max = max;
    }

    public double Process(double value)
    {
        var a = Math.Abs(value);
        var sign = Math.Sign(value);
        if (a < Min)
            return 0;
        if (a > Max)
            return sign;
        return sign * (a - Min) / (Max - Min);
    }
}

public class Gamepad : IAsyncDisposable
{
    public IObservable<bool> IsConnectedStream => _isConnectedSubject.AsObservable();
    public IObservable<Button> ButtonStream => _buttonSubject.AsObservable();
    public IObservable<Thumbstick> ThumbstickStream => _thumbstickSubject.AsObservable();
    public IObservable<Trigger> TriggerStream => _triggerSubject.AsObservable();
    public Deadzone ThumbstickDeadzone { get; set; } = new Deadzone(0.1, 0.9);
    public Deadzone TriggerDeadzone { get; set; } = new Deadzone(0.1, 0.9);

    private readonly Controller _controller;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _pollingTask;
    private readonly BehaviorSubject<bool> _isConnectedSubject;
    private readonly Subject<Button> _buttonSubject = new();
    private readonly Subject<Thumbstick> _thumbstickSubject = new();
    private readonly Subject<Trigger> _triggerSubject = new();
    private bool _isConnected;
    private State _previousState;
    private Thumbstick _previousLeftThumbstick, _previousRightThumbstick;
    private Trigger _previousLeftTrigger, _previousRightTrigger;

    public Gamepad()
    {
        _controller = new Controller(UserIndex.One);
        _isConnectedSubject = new BehaviorSubject<bool>(false);
        _pollingTask = Task.Run(PollLoop);
    }

    private void PollLoop()
    {
        while (!_cts.Token.IsCancellationRequested)
        {
            Thread.Sleep(10); // polling interval

            var isConnected = _controller.IsConnected;
            if (_isConnected != isConnected)
            {
                _isConnected = isConnected;
                _isConnectedSubject.OnNext(isConnected);
            }

            if (!isConnected)
                continue;

            var currentState = _controller.GetState();
            if (isConnected && currentState.PacketNumber != _previousState.PacketNumber)
            {
                var gamepad = currentState.Gamepad;
                var prevGamepad = _previousState.Gamepad;

                foreach (GamepadButtonFlags flag in Enum.GetValues(typeof(GamepadButtonFlags)))
                {
                    var wasPressed = prevGamepad.Buttons.HasFlag(flag);
                    var isPressed = gamepad.Buttons.HasFlag(flag);

                    if (wasPressed != isPressed)
                    {
                        var button = new Button((ButtonName)(flag <= GamepadButtonFlags.RightShoulder
                            ? (int)Math.Log((int)flag, 2) + 1
                            : (int)Math.Log((int)flag, 2) - 1), isPressed);
                        _buttonSubject.OnNext(button);
                    }
                }

                ProcessThumbstick(Side.Left, gamepad.LeftThumbX, gamepad.LeftThumbY, ref _previousLeftThumbstick);
                ProcessThumbstick(Side.Right, gamepad.RightThumbX, gamepad.RightThumbY, ref _previousRightThumbstick);
                ProcessTrigger(Side.Left, gamepad.LeftTrigger, ref _previousLeftTrigger);
                ProcessTrigger(Side.Right, gamepad.RightTrigger, ref _previousRightTrigger);

                _previousState = currentState;
            }
        }
    }

    private void ProcessThumbstick(Side side, short x, short y, ref Thumbstick previousThumbstick)
    {
        var thumbstick = new Thumbstick(side, NormalizeThumbstick(x), NormalizeThumbstick(y));
        if (thumbstick.X != previousThumbstick.X || thumbstick.Y != previousThumbstick.Y)
        {
            _thumbstickSubject.OnNext(thumbstick);
            previousThumbstick = thumbstick;
        }
    }

    private void ProcessTrigger(Side side, byte value, ref Trigger previousTrigger)
    {
        var trigger = new Trigger(side, NormalizeTrigger(value));
        if (trigger.Value != previousTrigger.Value)
        {
            _triggerSubject.OnNext(trigger);
            previousTrigger = trigger;
        }
    }

    private double NormalizeThumbstick(short value)
    {
        return ThumbstickDeadzone.Process((value + 0.5f) / 32767.5f);
    }

    private double NormalizeTrigger(byte value)
    {
        return TriggerDeadzone.Process(value / 255.0);
    }

    public bool SetVibration(double left, double right)
    {
        return _controller.SetVibration(new Vibration
        {
            LeftMotorSpeed = (ushort)(left * ushort.MaxValue),
            RightMotorSpeed = (ushort)(right * ushort.MaxValue)
        }) == Result.Ok;
    }

    public bool SetVibration(double speed = 0) => SetVibration(speed, speed);

    public ValueTask DisposeAsync()
    {
        _cts.Cancel();
        _isConnectedSubject.Dispose();
        _buttonSubject.Dispose();
        _thumbstickSubject.Dispose();
        _triggerSubject.Dispose();
        _cts.Dispose();
        return new ValueTask(_pollingTask);
    }
}