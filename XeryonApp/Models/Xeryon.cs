using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XeryonApp.Models;

public class Xeryon
{
    private readonly string _port;
    private readonly int _baudRate;
    private readonly List<Axis> _axisList = new();
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _dataProcessingTask;
    private string _settingsFile = "settings_default.txt";
    private readonly Dictionary<string, double> _settings = new(101);

    public string SettingsFile
    {
        get => _settingsFile;
        set
        {
            if (Path.Exists(value))
            {
                _settingsFile = value;
            }
        }
    }

    public Xeryon(string port, int baudRate, string settingsFile = "")
    {
        _port = port;
        _baudRate = baudRate;
        SettingsFile = settingsFile;
    }

    public Axis AddAxis(char letter, Stage stage)
    {
        var axis = new Axis(this, letter, stage);
        _axisList.Add(axis);
        return axis;
    }

    public Axis? GetAxis(char letter)
    {
        return _axisList.Find(axis => axis.Letter == letter);
    }

    public bool IsSingleAxisSystem => _axisList.Count <= 1;

    public void Start()
    {
        if (_axisList.Count == 0)
        {
            Debug.WriteLine("Cannot start the system without stages initialized in the software.");
            return;
        }

        _serialPort = new SerialPort(_port, _baudRate)
        {
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None,
            Handshake = Handshake.None,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        try
        {
            _serialPort.Open();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to open serial port: {ex.Message}");
            return;
        }

        Debug.WriteLine($"Serial port {_port} opened successfully.");
        _cancellationTokenSource = new CancellationTokenSource();
        _dataProcessingTask = Task.Run(() => ProcessDataAsync(_cancellationTokenSource.Token));

        Reset();
    }

    public void Stop()
    {
        foreach (var axis in _axisList)
        {
            axis.Stop();
        }

        _cancellationTokenSource?.Cancel();
        _serialPort?.Close();
        _serialPort?.Dispose();
        _dataProcessingTask?.Wait();
    }

    public void SendCommand(Axis? axis, string command)
    {
        var fullCommand = IsSingleAxisSystem || axis == null
            ? $"{command}\n"
            : $"{axis.Letter}:{command}\n";

        Debug.WriteLine($"Sending command: {fullCommand}");
        try
        {
            _serialPort?.WriteLine(fullCommand);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to send command: {ex.Message}");
        }
    }

    public void Reset()
    {
        foreach (var axis in _axisList)
        {
            axis.Reset();
        }

        Thread.Sleep(200);
        ReadSettings();
        foreach (var axis in _axisList)
        {
            axis.SendSettings();
        }
    }

    public void StopMovements()
    {
        foreach (var axis in _axisList)
        {
            axis.StopMovement();
        }
    }

    public void ReadSettings()
    {
        try
        {
            using var file = new StreamReader(SettingsFile);
            while (file.ReadLine() is { } line)
            {
                // Remove unnecessary spaces, carriage returns, and line breaks
                line = line.Replace(" ", "").Replace("\r", "").Replace("\n", "");

                // Ignore lines without '=' or those starting with comments
                if (!line.Contains('=') || line.StartsWith("%"))
                    continue;

                // Remove the comment
                if (line.Contains('%'))
                    line = line.Split('%')[0];

                var axis = _axisList.FirstOrDefault(); // Default to the first axis
                if (line.Contains(':'))
                {
                    // Determine the axis based on the letter prefix
                    var axisLetter = line[0];
                    axis = GetAxis(axisLetter);
                    if (axis == null)
                        continue; // Ignore unknown axes
                    line = line[2..]; // Remove the "X:" prefix
                }

                var parts = line.Split('=');
                if (parts.Length == 2)
                {
                    var tag = parts[0];
                    var value = parts[1];

                    _settings[tag] = double.Parse(value);
                    axis?.SetSetting(tag, value, true);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Error reading settings file: {ex.Message}");
            throw;
        }
    }

    public double GetSetting(string tag) => _settings[tag];

    public bool TryGetSetting(string tag, out double value) => _settings.TryGetValue(tag, out value);

    public Stage? GetModel()
    {
        ReadSettings();
        var key = _settings.Keys.FirstOrDefault(key => key.StartsWith("XLA"));
        return key == null ? null : Stage.GetStage($"{key}={_settings[key]}");
    }

    private async Task ProcessDataAsync(CancellationToken token)
    {
        Debug.WriteLine("Data processing thread started...");

        while (!token.IsCancellationRequested && _serialPort!= null)
        {
            var line = "";
            try
            {
                line = _serialPort.ReadLine().Trim();
                ProcessLine(line);

                await Task.Delay(10, token);
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Data processing thread was cancelled.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in processing line \"{line}\": {ex.Message}\n{ex.StackTrace}");
            }
        }

        Debug.WriteLine("Data processing thread stopped...");
    }

    private void ProcessLine(string? line)
    {
        if (string.IsNullOrEmpty(line)) return;

        string tag, value;
        Axis axis;

        if (IsSingleAxisSystem && line.Contains('='))
        {
            tag = line.Split('=')[0];
            value = line.Split('=')[1];
            axis = _axisList[0];
        }
        else if (line.Contains(":"))
        {
            var parts = line.Split(':');
            var axis0 = GetAxis(parts[0][0]);
            if (axis0 == null) return;
            axis = axis0;
            var tagValue = parts[1].Split('=');
            tag = tagValue[0];
            value = tagValue[1];
        }
        else
        {
            Debug.WriteLine($"Unknown line format: {line}");
            return;
        }

        axis.ReceiveData(tag, int.Parse(value));
    }
}
