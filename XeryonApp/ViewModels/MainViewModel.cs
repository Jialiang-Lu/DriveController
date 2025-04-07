using System.Collections.ObjectModel;
using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using ReactiveUI.Fody.Helpers;
using ReactiveUI.SourceGenerators;
using XeryonApp.Models;
using ObservableAsPropertyAttribute = ReactiveUI.Fody.Helpers.ObservableAsPropertyAttribute;

namespace XeryonApp.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    public ObservableCollection<SerialPortInfo> SerialPorts { get; } = new();

    public SortedObservableCollection<Drive> Drives { get; } = new();

    private readonly SerialPortWatcher _serialPortWatcher = new("XD-C");
    private IDisposable? _portSub;
    private bool _initialized;

    [ReactiveCommand]
    public async void UpdateDrive(SerialPortInfo port)
    {
        Debug.WriteLine($"Port {port} is added");
        try
        {
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
        _initialized = true;
    }

    public async ValueTask DisposeAsync()
    {
        _portSub?.Dispose();
        foreach (var drive in Drives)
        {
            await drive.DisposeAsync();
        }
        _serialPortWatcher.Dispose();
    }
}