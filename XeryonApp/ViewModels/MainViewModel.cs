using System.Collections.ObjectModel;
using System;
using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using ReactiveUI.SourceGenerators;
using XeryonApp.Models;
using ObservableAsPropertyAttribute = ReactiveUI.Fody.Helpers.ObservableAsPropertyAttribute;

namespace XeryonApp.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    public ObservableCollection<Drive> Drives { get; } = new();

    private readonly SerialPortWatcher _serialPortWatcher = new("XD-C");
    private IDisposable? _addDriveSub;

    [ReactiveCommand]
    public void AddDrive(SerialPortInfo port)
    {
        try
        {
            Drives.Add(new Drive(port));
        }
        catch (Exception e)
        {
            ThrowException(e);
        }
    }

    public void Start()
    {
        _addDriveSub = _serialPortWatcher.SerialPortObservable
            .Subscribe(AddDrive);
    }

    public async ValueTask DisposeAsync()
    {
        _addDriveSub?.Dispose();
        foreach (var device in Drives)
        {
            await device.DisposeAsync();
        }
        _serialPortWatcher.Dispose();
    }
}