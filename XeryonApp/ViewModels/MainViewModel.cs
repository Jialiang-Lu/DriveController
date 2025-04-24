using System.Collections.ObjectModel;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading.Tasks;
using ReactiveUI;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.SourceGenerators;
using XeryonApp.Input;
using XeryonApp.Models;
using ObservableAsPropertyAttribute = ReactiveUI.Fody.Helpers.ObservableAsPropertyAttribute;
using ReactiveAttribute = ReactiveUI.Fody.Helpers.ReactiveAttribute;

namespace XeryonApp.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    [Reactive]
    public bool RemoteEnabled { get; set; }

    [ObservableAsProperty]
    public bool RemoteConnected { get; }

    [Reactive]
    public int RemoteIndex { get; set; }

    [Reactive]
    public Drive? SelectedDrive { get; set; }

    public Interaction<(int index, bool up), Unit> ChangeSpeed { get; } =
        new Interaction<(int index, bool up), Unit>(RxApp.MainThreadScheduler);

    public Interaction<(int index, bool up), Unit> ChangeStep { get; } =
        new Interaction<(int index, bool up), Unit>(RxApp.MainThreadScheduler);

    public ObservableCollection<SerialPortInfo> SerialPorts { get; } = new();

    public SortedObservableCollection<Drive> Drives { get; } = new();

    private readonly SerialPortWatcher _serialPortWatcher = new("XD-C");
    private IDisposable? _portSub, _remoteSub;
    private bool _initialized, _leftTrigger, _rightTrigger;
    private Gamepad? _gamepad;

    [ReactiveCommand]
    public void ToggleRemote()
    {
        if (_gamepad == null)
            return;
        RemoteEnabled = !RemoteEnabled;
    }

    [ReactiveCommand]
    public async void UpdateDrive((SerialPortInfo port, bool active) portInfo)
    {
        try
        {
            var (port, active) = portInfo;
            if (!active)
                return;
            var drive = Drives.FirstOrDefault(d => d.Port.Address == port.Address);
            switch (drive)
            {
                case { Enabled: false }:
                {
                    var result = await drive.TryReconnect(port.Name);
                    if (!result)
                        throw new Exception($"Failed to reconnect to drive on port {port.Name}.");
                    return;
                }
                case { Enabled: true }:
                    return;
                default:
                    Drives.Add(new Drive(port));
                    break;
            }
        }
        catch (Exception e)
        {
            ThrowException(e);
        }
    }

    public void Start()
    {
        if (_initialized)
            return;
        _portSub = _serialPortWatcher.SerialPortObservable
            .Subscribe(UpdateDrive);
        _serialPortWatcher.SerialPortsObservable
            .Subscribe(ports =>
            {
                SerialPorts.Clear();
                foreach (var port in ports)
                {
                    SerialPorts.Add(port);
                }
            });
        Drives.CollectionChanged += (sender, args) =>
        {
            UpdateRemoteSelection(RemoteIndex);
        };
        _gamepad = new Gamepad
        {
            ThumbstickDeadzone = new Deadzone(0.2, 0.9), 
            TriggerDeadzone = new Deadzone(0.6, 0.6)
        };
        _gamepad.IsConnectedStream.ToPropertyEx(this, x => x.RemoteConnected);
        var tickStream = Observable.Interval(TimeSpan.FromMilliseconds(100), RxApp.MainThreadScheduler);
        this.WhenAnyValue(x => x.RemoteEnabled)
            .Subscribe(enabled =>
            {
                UpdateRemoteSelection(enabled ? RemoteIndex : -1);
            });
        var buttonStream = _gamepad.ButtonStream
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(b => b.IsPressed);
        var remoteSub = buttonStream
            .Where(b => b.ButtonName == ButtonName.Start)
            .Subscribe(_ =>
            {
                ToggleRemote();
            });
        var buttonSub = buttonStream
            .Where(_ => RemoteEnabled)
            .Select(b => b.ButtonName)
            .Subscribe(OnRemoteButton);
        var leftThumbstickStream = tickStream.Zip(_gamepad.ThumbstickStream
            .Where(t => t.Side == Side.Left).MostRecent(Thumbstick.LeftZero), (_, x) => x);
        var rightThumbstickStream = tickStream.Zip(_gamepad.ThumbstickStream
            .Where(t => t.Side == Side.Right).MostRecent(Thumbstick.RightZero), (_, x) => x);
        var thumbstickSub = leftThumbstickStream.Merge(rightThumbstickStream)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(_ => RemoteEnabled)
            .Subscribe(OnRemoteThumbstick);
        var leftTriggerStream = tickStream.Zip(_gamepad.TriggerStream
            .Where(t => t.Side == Side.Left).MostRecent(Trigger.LeftZero), (_, x) => x);
        var rightTriggerStream = tickStream.Zip(_gamepad.TriggerStream
            .Where(t => t.Side == Side.Right).MostRecent(Trigger.RightZero), (_, x) => x);
        var triggerSub = leftTriggerStream.Merge(rightTriggerStream)
            .ObserveOn(RxApp.MainThreadScheduler)
            .Where(_ => RemoteEnabled)
            .Subscribe(OnRemoteTrigger);
        _remoteSub = Disposable.Create(() =>
        {
            remoteSub.Dispose();
            buttonSub.Dispose();
            thumbstickSub.Dispose();
            triggerSub.Dispose();
        });
        _initialized = true;
    }

    private void OnRemoteButton(ButtonName button)
    {
        if (!_rightTrigger)
            OnRemoteButton(button, RemoteIndex);
        else
            for (var i = 0; i < Drives.Count; i++)
                OnRemoteButton(button, i);
    }

    private void OnRemoteButton(ButtonName button, int index)
    {
        if (index < 0 || index >= Drives.Count)
            return;
        var drive = Drives[index];
        switch (button)
        {
            case ButtonName.Back:
                if (drive.SafeToMove)
                    drive.Reset();
                break;
            case ButtonName.A:
                drive.Start();
                break;
            case ButtonName.B:
                drive.Stop();
                break;
            case ButtonName.X when !_leftTrigger:
                drive.SetZero();
                break;
            case ButtonName.X when _leftTrigger:
                drive.GoToZero();
                break;
            case ButtonName.Y:
                drive.RelativeMode = !drive.RelativeMode;
                break;
            case ButtonName.LeftThumb:
                drive.MoveTop();
                break;
            case ButtonName.RightThumb:
                drive.ResetTarget();
                break;
            case ButtonName.DPadUp when !_leftTrigger:
                if (index > 0)
                    UpdateRemoteSelection(index - 1);
                break;
            case ButtonName.DPadDown when !_leftTrigger:
                if (index < Drives.Count - 1)
                    UpdateRemoteSelection(index + 1);
                break;
            case ButtonName.DPadLeft when !_leftTrigger:
                ChangeSpeed.Handle((index, false)).Wait();
                break;
            case ButtonName.DPadRight when !_leftTrigger:
                ChangeSpeed.Handle((index, true)).Wait();
                break;
            case ButtonName.DPadUp when _leftTrigger:
                drive.StepUp(drive.Step ?? 0);
                break;
            case ButtonName.DPadDown when _leftTrigger:
                drive.StepDown(drive.Step ?? 0);
                break;
            case ButtonName.DPadLeft when _leftTrigger:
                ChangeStep.Handle((index, false)).Wait();
                break;
            case ButtonName.DPadRight when _leftTrigger:
                ChangeStep.Handle((index, true)).Wait();
                break;
        }
    }

    private void OnRemoteThumbstick(Thumbstick thumbstick)
    {
        for (var i = 0; i < Drives.Count; i++)
        {
            if (i == RemoteIndex || _rightTrigger)
                OnRemoteThumbstick(thumbstick, i, i == RemoteIndex);
            else
            {
                OnRemoteThumbstick(Thumbstick.LeftZero, i);
                OnRemoteThumbstick(Thumbstick.RightZero, i);
            }
        }
    }

    private void OnRemoteThumbstick(Thumbstick thumbstick, int index, bool vibrate = false)
    {
        if (index < 0 || index >= Drives.Count)
            return;
        var drive = Drives[index];
        switch (thumbstick.Side)
        {
            case Side.Left:
                var pos = thumbstick.Y;
                if (pos == 0)
                {
                    if (drive.RemoteAllowed)
                        drive.StopScan();
                    if (vibrate)
                        _gamepad!.SetVibration();
                }
                else
                {
                    if (drive.RemoteAllowed)
                        drive.StartScan(-pos);
                    if (vibrate)
                        _gamepad!.SetVibration(Math.Abs(pos) * 0.5);
                }
                break;
            case Side.Right:
                drive.TargetPosition -= (decimal)thumbstick.Y / 10;
                break;
        }
    }

    private void OnRemoteTrigger(Trigger trigger)
    {
        switch (trigger.Side)
        {
            case Side.Left:
                _leftTrigger = trigger.Value > 0;
                break;
            case Side.Right:
                _rightTrigger = trigger.Value > 0;
                break;
        }
    }

    private void UpdateRemoteSelection(int index)
    {
        if (RemoteIndex >= 0 && RemoteIndex < Drives.Count)
        {
            Drives[RemoteIndex].StopScan();
            Drives[RemoteIndex].RemoteControlled = false;
            SelectedDrive = null;
        }
        if (index < 0 || index >= Drives.Count)
            return;
        RemoteIndex = index;
        Drives[index].RemoteControlled = true;
        SelectedDrive = Drives[index];
    }

    public async ValueTask DisposeAsync(bool reset)
    {
        _portSub?.Dispose();
        _remoteSub?.Dispose();
        foreach (var drive in Drives)
        {
            await drive.DisposeAsync(reset);
        }
        _serialPortWatcher.Dispose();
    }

    public async ValueTask DisposeAsync()
    {
        await DisposeAsync(false);
    }
}