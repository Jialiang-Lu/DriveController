using ReactiveUI.Fody.Helpers;
using ReactiveUI;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using ReactiveUI.SourceGenerators;
using XeryonApp.Models;
using ReactiveAttribute = ReactiveUI.Fody.Helpers.ReactiveAttribute;
using ObservableAsPropertyAttribute = ReactiveUI.Fody.Helpers.ObservableAsPropertyAttribute;

namespace XeryonApp.ViewModels;

public partial class Drive : ReactiveObject, IComparable<Drive>, IAsyncDisposable
{
    /// <summary>
    /// Physical address of the drive.
    /// </summary>
    public int Address => Port.Address;

    /// <summary>
    /// Serial port information.
    /// </summary>
    public SerialPortInfo Port { get; init; }

    /// <summary>
    /// Status of the drive.
    /// </summary>
    [ObservableAsProperty]
    public Status Status { get; }

    [ObservableAsProperty]
    public string StatusString { get; } = "";

    /// <summary>
    /// Speed in um/s.
    /// </summary>
    [Reactive]
    public decimal? Speed { get; set; }

    public decimal[] FixedSpeeds { get; } = { 10, 20, 30, 40, 80, 120, 160, 200, 300, 400, 500, 1000 };

    /// <summary>
    /// Step size in mm.
    /// </summary>
    [Reactive]
    public decimal? Step { get; set; }

    public decimal[] FixedSteps { get; } =
        { 0.1m, 0.2m, 0.3m, 0.4m, 0.5m, 0.6m, 0.7m, 0.8m, 0.9m, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

    /// <summary>
    /// Current position in mm.
    /// </summary>
    [ObservableAsProperty]
    public decimal CurrentPosition { get; }

    /// <summary>
    /// Absolute current position in mm.
    /// </summary>
    [ObservableAsProperty]
    public decimal AbsolutePosition { get; }

    /// <summary>
    /// Target position in mm.
    /// </summary>
    [Reactive]
    public decimal? TargetPosition { get; set; }

    /// <summary>
    /// Absolute target position in mm.
    /// </summary>
    [ObservableAsProperty]
    public decimal AbsoluteTarget { get; }

    /// <summary>
    /// Zero position in mm.
    /// </summary>
    [Reactive]
    public decimal ZeroPosition { get; set; }

    [ObservableAsProperty]
    public bool ZeroSet { get; }

    /// <summary>
    /// Whether the position is relative to the zero position.
    /// </summary>
    [Reactive]
    public bool RelativeMode { get; set; }

    /// <summary>
    /// Whether the motor is on.
    /// </summary>
    [ObservableAsProperty]
    public bool IsMoving { get; }

    /// <summary>
    /// Whether the target position is reached.
    /// </summary>
    [ObservableAsProperty]
    public bool TargetReached { get; }

    /// <summary>
    /// Minimum range in mm.
    /// </summary>
    public decimal MinRange { get; set; } = 0;

    /// <summary>
    /// Maximum range in mm.
    /// </summary>
    public decimal MaxRange { get; set; } = 50;

    private Xeryon? _xeryon;
    private Axis? _axis;

    public Drive(SerialPortInfo port)
    {
        Speed = 500;
        Step = 1;
        TargetPosition = 0;
        ZeroPosition = 0;
        RelativeMode = false;

        Port = port;
        _xeryon = new Xeryon(Port.Name, 115200);
        var model = _xeryon.GetModel();
        if (model == null)
            throw new Exception("Model not defined in settings file");
        _axis = _xeryon.AddAxis('X', model);
        _xeryon.Start();
        _axis.SendCommand("INDA", 0);
        _axis.SendCommand("SAVE");
        if (_xeryon.TryGetSetting("LLIM", out var minRange))
            MinRange = (decimal)minRange;
        if (_xeryon.TryGetSetting("HLIM", out var maxRange))
            MaxRange = (decimal)maxRange;

        var dataStream = Observable.FromEvent<Axis.DataReceived, (string tag, int value)>(
            h => _axis.OnDataReceived += h,
            h => _axis.OnDataReceived -= h);
        dataStream.Where(data => data.tag == "STAT")
            .Select(data => (Status)data.value)
            .ToPropertyEx(this, x => x.Status);
        this.WhenAnyValue(x => x.Status)
            .Select(status => status.ToString())
            .ToPropertyEx(this, x => x.StatusString);
        dataStream.Where(data => data.tag == "EPOS")
            .Select(data => (decimal)_axis.CurrentPosition[Distance.Type.MM])
            .ToPropertyEx(this, x => x.AbsolutePosition);
        dataStream.Where(data => data.tag == "DPOS")
            .Select(data => (decimal)_axis.TargetPosition[Distance.Type.MM])
            .ToPropertyEx(this, x => x.AbsoluteTarget);
        this.WhenAnyValue(x => x.Status)
            .Select(status => status.HasFlag(Status.PositionReached))
            .DistinctUntilChanged()
            .ToPropertyEx(this, x => x.TargetReached);
        this.WhenAnyValue(x => x.Status)
            .Select(status => status.HasFlag(Status.MotorOn))
            .DistinctUntilChanged()
            .ToPropertyEx(this, x => x.IsMoving);
        this.WhenAnyValue(x => x.AbsolutePosition, x => x.RelativeMode, x => x.ZeroPosition,
                (pos, rel, zero) => pos - (rel ? zero : 0m))
            .ToPropertyEx(this, x => x.CurrentPosition);
        this.WhenAnyValue(x => x.AbsoluteTarget, x => x.RelativeMode, x => x.ZeroPosition,
                (pos, rel, zero) => pos - (rel ? zero : 0m))
            .Subscribe(pos => TargetPosition = pos);
        this.WhenAnyValue(x => x.TargetPosition)
            .Subscribe(pos =>
            {
                if (pos == null) return;
                SetTarget(pos.Value, RelativeMode);
            });
        this.WhenAnyValue(x => x.Speed)
            .Subscribe(speed =>
            {
                if (speed == null) return;
                _axis.SetSpeed(new Distance((double)speed.Value, Distance.Type.UM));
            });
        this.WhenAnyValue(x => x.ZeroPosition)
            .Select(pos => pos != 0)
            .ToPropertyEx(this, x => x.ZeroSet);
    }

    [ReactiveCommand]
    public void Start()
    {
        if (TargetReached) return;
        _axis?.ResumeMovement();
    }

    [ReactiveCommand]
    public void Stop()
    {
        _axis?.StopMovement();
    }

    [ReactiveCommand]
    public void StartStop()
    {
        if (IsMoving)
            Stop();
        else
            Start();
    }

    [ReactiveCommand]
    public void StepDown(decimal step)
    {
        if (step == 0 || IsMoving) return;
        _axis?.Step(new Distance((double)step, Distance.Type.MM));
    }

    [ReactiveCommand]
    public void StepUp(decimal step)
    {
        StepDown(-step);
    }

    [ReactiveCommand]
    public void SetTarget(decimal pos, bool relative = false)
    {
        if (_axis == null) return;
        if (relative)
            pos += ZeroPosition;
        _axis.TargetPosition = new Distance((double)pos, Distance.Type.MM);
    }

    [ReactiveCommand]
    public void ResetTarget()
    {
        SetTarget(AbsolutePosition);
    }

    [ReactiveCommand]
    public void SetZero()
    {
        if (ZeroPosition > 0)
        {
            ZeroPosition = 0;
            if (RelativeMode)
                RelativeMode = false;
        }
        else
        {
            ZeroPosition = AbsolutePosition;
            if (!RelativeMode)
                RelativeMode = true;
        }
    }

    [ReactiveCommand]
    public void GoToZero()
    {
        SetTarget(ZeroPosition);
    }

    [ReactiveCommand]
    public void Test()
    {
        Task.Run(TestAsync);
    }

    [ReactiveCommand]
    public void Reset()
    {
        Task.Run(ResetAsync);
    }

    private async ValueTask TestAsync()
    {
        _axis?.SendCommand("Test", 1);
        await Task.Delay(1000);
        _axis?.SendCommand("Test", 0);
    }

    private async ValueTask ResetAsync()
    {
        if (_axis == null || _xeryon == null) return;
        var speed = Speed;
        Speed = 10000;
        _axis.StartScan(-1);
        var pos = AbsolutePosition;
        var maxIter = 50;
        do
        {
            pos = AbsolutePosition;
            await Task.Delay(100);
            maxIter--;
        } while (Math.Abs(pos - AbsolutePosition) > 0.01m && maxIter > 0);

        _xeryon.Reset();
        Speed = speed;
        Stop();
        ResetTarget();
    }

    public async ValueTask DisposeAsync()
    {
        Speed = 10000;
        await ResetAsync();
        _xeryon?.Stop();
        _xeryon = null;
        _axis = null;
    }

    public int CompareTo(Drive? other)
    {
        return other == null ? 1 : Port.CompareTo(other.Port);
    }
}
