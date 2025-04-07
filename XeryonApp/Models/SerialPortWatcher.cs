using System.Collections.Generic;
using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Reactive.Subjects;
using Microsoft.Win32;

namespace XeryonApp.Models;

public readonly struct SerialPortInfo : IComparable<SerialPortInfo>, IEquatable<SerialPortInfo>
{
    public string Name { get; init; }
    public int Address { get; init; } = -1;

    private static readonly Dictionary<string, int> _addressCache = new(7);

    public static SerialPortInfo FromName(string name)
    {
        return new SerialPortInfo(name);
    }

    public SerialPortInfo(string name)
    {
        Name = name;
        using var searcher =
            new ManagementObjectSearcher("SELECT PNPDeviceID FROM Win32_PnPEntity WHERE Name LIKE '%(" + name + ")%'");
        var pnpDeviceId = searcher.Get().Cast<ManagementObject>().FirstOrDefault()?["PNPDeviceID"]?.ToString();
        if (pnpDeviceId == null)
        {
            if (_addressCache.TryGetValue(name, out var address))
            {
                Address = address;
            }
            return;
        }
        var registryPath = @"SYSTEM\CurrentControlSet\Enum\" + pnpDeviceId;
        using var key = Registry.LocalMachine.OpenSubKey(registryPath);
        if (key?.GetValue("Address") == null)
        {
            if (_addressCache.TryGetValue(name, out var address))
            {
                Address = address;
            }
            return;
        }
        Address = (int)key.GetValue("Address")!;
        _addressCache[name] = Address;
    }

    public int CompareTo(SerialPortInfo other)
    {
        var result = Address.CompareTo(other.Address);
        return result != 0 ? result : string.Compare(Name, other.Name, StringComparison.Ordinal);
    }

    public bool Equals(SerialPortInfo other)
    {
        return Address == other.Address && Name == other.Name;
    }

    public override string ToString()
    {
        return $"{Address}: {Name}";
    }
}

public class SerialPortWatcher : IDisposable
{
    private readonly ManagementEventWatcher _deviceInsertedWatcher, _deviceRemovedWatcher;
    private readonly ReplaySubject<SerialPortInfo> _serialPortSubject = new();
    private readonly List<SerialPortInfo> _serialPorts = new();
    private readonly string _descriptionFilter;
    private bool _disposed;

    public IReadOnlyList<SerialPortInfo> SerialPorts => _serialPorts.AsReadOnly();

    public IObservable<SerialPortInfo> SerialPortObservable => _serialPortSubject;

    public SerialPortWatcher(string descriptionFilter)
    {
        _descriptionFilter = descriptionFilter;
        GetPorts();

        // Watch for device insertion
        _deviceInsertedWatcher = new ManagementEventWatcher(new WqlEventQuery(
            "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_SerialPort'"));
        _deviceInsertedWatcher.EventArrived += OnDeviceChanged;

        // Watch for device removal
        _deviceRemovedWatcher = new ManagementEventWatcher(new WqlEventQuery(
            "SELECT * FROM __InstanceDeletionEvent WITHIN 1 WHERE TargetInstance ISA 'Win32_SerialPort'"));
        _deviceRemovedWatcher.EventArrived += OnDeviceChanged;

        _deviceInsertedWatcher.Start();
        _deviceRemovedWatcher.Start();
    }

    // Dispose resources properly
    public void Dispose()
    {
        if (_disposed) return;

        _deviceInsertedWatcher.Stop();
        _deviceRemovedWatcher.Stop();
        _deviceInsertedWatcher.Dispose();
        _deviceRemovedWatcher.Dispose();

        _disposed = true;
    }

    private void GetPorts()
    {
        using var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_SerialPort");
        foreach (var obj in searcher.Get())
        {
            UpdatePort(obj, true);
        }
    }

    private void OnDeviceChanged(object sender, EventArrivedEventArgs e)
    {
        UpdatePort((ManagementBaseObject)e.NewEvent["TargetInstance"],
            e.NewEvent.ClassPath.ClassName == "__InstanceCreationEvent");
    }

    private void UpdatePort(ManagementBaseObject obj, bool add)
    {
        if ((string)obj["Description"] != _descriptionFilter)
            return;
        var name = (string)obj["DeviceID"];
        var port = new SerialPortInfo(name);
        if (add)
        {
            _serialPorts.Add(port);
            _serialPorts.Sort();
            _serialPortSubject.OnNext(port);
        }
        else
        {
            _serialPorts.Remove(port);
        }
    }
}
