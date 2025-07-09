using ReactiveUI.Fody.Helpers;
using ReactiveUI;
using System.Reactive.Linq;
using System.Threading.Tasks;
using System;
using System.Diagnostics;
using System.Reactive.Subjects;
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
    /// The serial number of the drive.
    /// </summary>
    public int Id { get; init; }

    /// <summary>
    /// Serial port information.
    /// </summary>
    public SerialPortInfo Port { get; init; }

    /// <summary>
    /// Whether the drive is connected.
    /// </summary>
    [ObservableAsProperty]
    public bool Enabled { get; set; }

    /// <summary>
    /// Whether the drive is currently remote controlled.
    /// </summary>
    [Reactive] public bool RemoteControlled { get; set; }

    /// <summary>
    /// Whether the drive is allowed to be remote controlled.
    /// </summary>
    [Reactive] public bool RemoteAllowed { get; set; }

    /// <summary>
    /// Status of the drive.
    /// </summary>
    [ObservableAsProperty]
    public Status Status { get; }

    [ObservableAsProperty] 
    public bool SafeToMove { get; set; }

    [ObservableAsProperty]
    public string StatusString { get; } = "";

    /// <summary>
    /// Speed in um/s.
    /// </summary>
    [Reactive]
    public decimal? Speed { get; set; }

    [ObservableAsProperty]
    public double ActualSpeed { get; }

    [ObservableAsProperty] 
    public string TimeFormatted { get; } = "";

    [Reactive]
    public double? SpeedFactor { get; private set; }

    public decimal[] FixedSpeeds { get; } = { 10, 20, 30, 40, 60, 80, 120, 160, 200, 
        300, 400, 500, 600, 1000 };

    /// <summary>
    /// Step size in mm.
    /// </summary>
    [Reactive]
    public decimal? Step { get; set; }

    public decimal[] FixedSteps { get; } =
        { 0.1m, 0.2m, 0.3m, 0.4m, 0.5m, 0.6m, 0.7m, 0.8m, 0.9m, 
            1, 2, 3, 4, 5, 6, 7, 8, 9, 10 };

    /// <summary>
    /// Offset in mm. Encoder is reset after power on, and this is to compensate for that.
    /// </summary>
    public decimal Offset { get; private set; } = 0.0m;

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
    /// Whether the drive is currently scanning.
    /// </summary>
    [ObservableAsProperty]
    public bool IsScanning { get; }

    [Reactive] 
    public bool IsMovingTop { get; set; }

    [Reactive]
    public int ScanDirection { get; set; }

    /// <summary>
    /// Whether the target position is reached.
    /// </summary>
    [ObservableAsProperty]
    public bool TargetReached { get; } = true;


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
    private readonly ReplaySubject<bool> _connectionSubject = new(1);

    public Drive()
    {
        Speed = 500;
        Step = 1;
        TargetPosition = 0;
        ZeroPosition = 0;
        RelativeMode = false;
        RemoteAllowed = true;
    }

    public Drive(SerialPortInfo port) : this()
    {
        Port = port;
        _xeryon = new Xeryon(port.Name, 115200);
        _axis = _xeryon.AddAxis('X');
        try
        {
            _xeryon.Start();
        }
        catch (Exception e)
        {
            _xeryon.DisposeAsync().AsTask().Wait();
            throw new InvalidOperationException($"Failed to connect to the drive on port {port.Name}.", e);
        }
        _axis.SendCommand("SRNO=?");
        var maxWait = 10;
        int id;
        while (!_axis.TryGetData("SRNO", out id) && maxWait >= 0)
        {
            Task.Delay(50).Wait();
            maxWait--;
        }
        if (maxWait < 0)
        {
            _xeryon.DisposeAsync().AsTask().Wait();
            throw new TimeoutException("Timeout waiting for the drive to respond.");
        }
        Id = id;
        Debug.WriteLine($"Connected to drive ID: {Id} on port {Port}");
        _connectionSubject.OnNext(true);

        _axis.SendCommand("INDA", 0);
        _axis.SendCommand("SAVE");
        if (_xeryon.TryGetSetting("LLIM", out var minRange))
            MinRange = (decimal)minRange;
        if (_xeryon.TryGetSetting("HLIM", out var maxRange))
            MaxRange = (decimal)maxRange;
        var dataStream = Observable.FromEvent<Axis.DataReceived, (string tag, int value)>(
            h => _axis.OnDataReceived += h,
            h => _axis.OnDataReceived -= h);
        Observable.FromEvent<Action<bool>, bool>(h => _xeryon.ConnectionUpdated += h,
                h => _xeryon.ConnectionUpdated -= h).Merge(_connectionSubject)
            .Delay(enabled => Observable.Timer(TimeSpan.FromSeconds(enabled ? 1 : 0)))
            .ToPropertyEx(this, x => x.Enabled);
        this.WhenAnyValue(x => x.Enabled)
            .Subscribe(enabled =>
            {
                if (!enabled)
                    Offset = AbsolutePosition;
                else
                {
                    _axis.SendCommand("LOAD");
                    _axis.SendCommand("EPOS=?");
                    dataStream.Where(data => data.tag == "EPOS")
                        .Select(_ => (decimal)_axis.CurrentPosition[Distance.Type.MM])
                        .Take(1)
                        .Subscribe(pos =>
                        {
                            Offset -= pos;
                            SetLimits();
                        });
                }
            });
        dataStream.Where(data => data.tag == "STAT")
            .Select(data => (Status)data.value)
            .ToPropertyEx(this, x => x.Status);
        this.WhenAnyValue(x => x.Status)
            .Select(status => status.ToString())
            .ToPropertyEx(this, x => x.StatusString);
        var epos = dataStream.Where(data => data.tag == "EPOS")
            .Select(_ => (decimal)_axis.CurrentPosition[Distance.Type.MM]);
        epos.Select(pos => pos + Offset)
            .ToPropertyEx(this, x => x.AbsolutePosition);
        dataStream.Where(data => data.tag == "DPOS")
            .Select(_ => (decimal)_axis.TargetPosition[Distance.Type.MM] + Offset)
            .ToPropertyEx(this, x => x.AbsoluteTarget);
        epos.Select(pos => ((double)pos, DateTimeOffset.Now))
            .Scan<(double pos, DateTimeOffset time), (double speed, double pos, DateTimeOffset time)>(
                (0d, 0d, DateTimeOffset.Now), (t1, t2) =>
                {
                    var (_, prevPos, prevTime) = t1;
                    var (pos, time) = t2;
                    var elapsed = (time - prevTime).TotalSeconds;
                    var speed = (pos - prevPos) * 1000 / elapsed;
                    return (speed, pos, time);
                })
            .Select(t => t.speed)
            .ToPropertyEx(this, x => x.ActualSpeed);
        this.WhenAnyValue(x => x.CurrentPosition)
            .Select(p => p < 1)
            .ToPropertyEx(this, x => x.SafeToMove);
        this.WhenAnyValue(x => x.Status)
            .Select(status => status.HasFlag(Status.Scanning))
            .DistinctUntilChanged()
            .ToPropertyEx(this, x => x.IsScanning);
        this.WhenAnyValue(x => x.IsScanning)
            .Delay(isScanning => Observable.Timer(TimeSpan.FromMilliseconds(isScanning ? 0 : 200)))
            .CombineLatest(
                this.WhenAnyValue(x => x.Status),
                (isScanning, status) => isScanning || status.HasFlag(Status.PositionReached))
            .DistinctUntilChanged()
            .ToPropertyEx(this, x => x.TargetReached);
        this.WhenAnyValue(x => x.Status)
            .Select(status => status.HasFlag(Status.MotorOn))
            .DistinctUntilChanged()
            .ToPropertyEx(this, x => x.IsMoving);
        this.WhenAnyValue(x => x.AbsolutePosition, x => x.RelativeMode, x => x.ZeroPosition,
                (pos, rel, zero) => pos - (rel ? zero : 0m))
            .Where(_ => Enabled)
            .ToPropertyEx(this, x => x.CurrentPosition);
        this.WhenAnyValue(x => x.AbsoluteTarget, x => x.RelativeMode, x => x.ZeroPosition,
                (pos, rel, zero) => pos - (rel ? zero : 0m))
            .Where(_ => Enabled)
            .Subscribe(pos => TargetPosition = pos);
        this.WhenAnyValue(x => x.TargetPosition)
            .Subscribe(pos =>
            {
                if (pos == null) return;
                SetTarget(pos.Value, RelativeMode);
            });
        this.WhenAnyValue(x => x.Speed, x => x.SpeedFactor, x => x.IsScanning)
            .Subscribe(speeds =>
            {
                var (speed, factor, isScanning) = speeds;
                if (speed == null) return;
                if (factor == null || !isScanning)
                    factor = 1;
                else
                    factor = Math.Clamp(factor.Value, 0, 1);
                SetSpeed(speed.Value * (decimal)factor);
            });
        this.WhenAnyValue(x => x.ZeroPosition)
            .Select(pos => pos != 0)
            .ToPropertyEx(this, x => x.ZeroSet);
        this.WhenAnyValue(x => x.TargetReached, x => x.IsScanning, x => x.CurrentPosition, x => x.TargetPosition,
                x => x.ActualSpeed)
            .Select(t =>
            {
                var (targetReached, isScanning, currentPosition, targetPosition, actualSpeed) = t;
                if (targetReached || isScanning || targetPosition == null)
                    return "";
                var time = (double)(targetPosition.Value - currentPosition) / actualSpeed * 1000;
                switch (time)
                {
                    case double.NaN:
                        return "";
                    case > 3600:
                        return ">1h";
                    default:
                        var minutes = (int)(time / 60);
                        var seconds = time % 60;
                        return $"{minutes:D2}:{seconds:00.0}";
                }

            }).ToPropertyEx(this, x => x.TimeFormatted);
    }

    public async Task<bool> TryReconnect(string? port = null, int maxAttempts = 3)
    {
        if (Enabled) return false;
        while (maxAttempts > 0 && _xeryon != null)
        {
            try
            {
                _xeryon!.Connect(port);
                return true;
            }
            catch (Exception e)
            {
                Debug.WriteLine($"Failed to reconnect to the drive on port {port}: {e.Message}, retrying...");
                maxAttempts--;
                await Task.Delay(1000);
            }
        }

        return false;
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
    public void StartScan(double factor)
    {
        if (_axis == null) return;
        SpeedFactor = Math.Abs(factor);
        var direction = Math.Sign(factor);
        if (direction != ScanDirection || !IsScanning)
            _axis.StartScan(direction);
        ScanDirection = direction;
    }

    public async void StopScan()
    {
        if (_axis == null || !IsScanning || IsMovingTop) return;
        SpeedFactor = 1;
        _axis.StopScan();
        await Task.Delay(100);
        ResetTarget();
    }

    [ReactiveCommand]
    public void StepDown(decimal step)
    {
        if (step == 0) return;
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
        pos -= Offset;
        SetSpeed(0);
        _axis.TargetPosition = new Distance((double)pos, Distance.Type.MM);
        SetSpeed(Speed ?? 0);
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
    
    private void SetSpeed(decimal speed)
    {
        Debug.WriteLine($"Setting speed to {speed} um/s");
        _axis?.SetSpeed(new Distance((double)(speed), Distance.Type.UM));
    }

    private void SetLimits(decimal? min = null, decimal? max = null)
    {
        min ??= MinRange - Offset;
        max ??= MaxRange - Offset;
        _axis?.SetLimits(
            new Distance((double)min, Distance.Type.MM),
            new Distance((double)max, Distance.Type.MM));
    }

    [ReactiveCommand]
    public void Test()
    {
        Task.Run(TestAsync);
    }

    [ReactiveCommand]
    public void Reset()
    {
        Task.Run(() => ResetAsync(10000));
    }

    [ReactiveCommand]
    public void MoveTop()
    {
        Task.Run(() => MoveTopAsync());
    }

    private async ValueTask TestAsync()
    {
        _axis?.SendCommand("Test", 1);
        await Task.Delay(1000);
        _axis?.SendCommand("Test", 0);
    }

    private async ValueTask MoveTopAsync(decimal? speed = null)
    {
        RemoteAllowed = false;
        SpeedFactor = null;
        if (_axis == null || _xeryon == null) return;
        IsMovingTop = true;
        SetLimits(-MaxRange);
        Speed = speed ?? Speed;
        _axis.StartScan(-1);
        await Task.Delay(500);
        do
        {
            Debug.WriteLine($"Actual speed: {ActualSpeed}, speed {Speed}");
        } while (Math.Abs(ActualSpeed) > (double)Speed! / 5);

        Stop();
        SetLimits();
        IsMovingTop = false;
        SpeedFactor = 1;
        RemoteAllowed = true;
    }

    private async ValueTask ResetAsync(decimal? speed = null)
    {
        var oldSpeed = Speed;
        await MoveTopAsync(speed);
        RemoteAllowed = false;
        Speed = oldSpeed;
        Stop();
        _xeryon?.Reset();
        ResetTarget();
        RemoteAllowed = true;
    }

    public async ValueTask DisposeAsync(bool reset)
    {
        if (Enabled && reset)
        {
            Speed = 10000;
            await MoveTopAsync();
        }
        if (_xeryon != null)
        {
            await _xeryon.DisposeAsync();
        }
        _xeryon = null;
        _axis = null;
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(false);
    }

    public int CompareTo(Drive? other)
    {
        return other == null ? 1 : Port.CompareTo(other.Port);
    }
}
