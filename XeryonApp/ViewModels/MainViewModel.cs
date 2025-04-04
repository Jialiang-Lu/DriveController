﻿using System.Collections.ObjectModel;
using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using ReactiveUI.SourceGenerators;
using XeryonApp.Models;
using ObservableAsPropertyAttribute = ReactiveUI.Fody.Helpers.ObservableAsPropertyAttribute;

namespace XeryonApp.ViewModels;

public partial class MainViewModel : ViewModelBase, IAsyncDisposable
{
    public SortedObservableCollection<Drive> Drives { get; } = new();

    private readonly SerialPortWatcher _serialPortWatcher = new("XD-C");
    private IDisposable? _portSub;

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
            Debug.WriteLine($"Error: {e.Message}");
        }
    }

    public void Start()
    {
        _portSub = _serialPortWatcher.SerialPortObservable
            .Subscribe(UpdateDrive);
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