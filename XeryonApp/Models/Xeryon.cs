using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace XeryonApp.Models;

public partial class Xeryon : IAsyncDisposable
{
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

    public bool Connected
    {
        get => _connected;
        private set
        {
            if (_connected != value)
            {
                _connected = value;
                ConnectionUpdated?.Invoke(value);
            }
        }
    }

    public event Action<bool>? ConnectionUpdated;

    private string _port;
    private readonly int _baudRate;
    private readonly List<Axis> _axisList = new();
    private SerialPort? _serialPort;
    private CancellationTokenSource? _cancellationTokenSource;
    private Task? _dataProcessingTask;
    private string _settingsFile = "settings_default.txt";
    private readonly List<(char? letter, string tag, double value)> _settings =
        new List<(char? letter, string tag, double value)>(64);
    private bool _connected;

    [GeneratedRegex(@"^(?:(?<letter>[A-Z]):)?(?<tag>\w+)\s*=\s*(?<value>[-+]?[0-9]*\.?[0-9]+)", RegexOptions.Compiled)]
    private static partial Regex SettingsPattern();

    public Xeryon(string port, int baudRate, string settingsFile = "")
    {
        _port = port;
        _baudRate = baudRate;
        SettingsFile = settingsFile;
        if (!Path.Exists(_settingsFile))
            throw new FileNotFoundException($"Settings file not found: {_settingsFile}");
    }

    public Axis AddAxis(char letter) => AddAxis(letter,
        GetModel() ?? throw new KeyNotFoundException("Model not found in the settings file."));

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

        Connect();
        Reset();
    }

    public void Connect(string? port = null)
    {
        if (port != null)
            _port = port;
        Disconnect().Wait();

        _serialPort = new SerialPort(_port, _baudRate)
        {
            DataBits = 8,
            StopBits = StopBits.One,
            Parity = Parity.None,
            Handshake = Handshake.None,
            ReadTimeout = 1000,
            WriteTimeout = 1000
        };

        _serialPort.Close();
        _serialPort.Open();
        Debug.WriteLine($"Serial port {_port} opened successfully.");
        _cancellationTokenSource = new CancellationTokenSource();
        _dataProcessingTask = Task.Run(ProcessDataAsync);
        Connected = true;
    }

    private async Task Disconnect()
    {
        Connected = false;
        if (_cancellationTokenSource != null)
        {
            await _cancellationTokenSource.CancelAsync();
        }
        if (_dataProcessingTask != null) await _dataProcessingTask;
        _serialPort?.Close();
    }

    public async ValueTask DisposeAsync()
    {
        if (!Connected)
            return;

        foreach (var axis in _axisList)
        {
            axis.Stop();
        }

        await Disconnect();
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
            throw new IOException($"Failed to send command: {ex.Message}");
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

    public void LoadSettings()
    {
        _settings.Clear();
        using var file = new StreamReader(SettingsFile);
        while (file.ReadLine() is { } line)
        {
            var match = SettingsPattern().Match(line);
            if (match.Success)
            {
                var letter = match.Groups["letter"].Success ? (char?)match.Groups["letter"].Value[0] : null;
                var tag = match.Groups["tag"].Value;
                var value = double.Parse(match.Groups["value"].Value);
                _settings.Add((letter, tag, value));
            }
            else
            {
                Debug.WriteLine($"Invalid settings line: {line}");
            }
        }
    }

    public void ReadSettings()
    {
        if (_settings.Count == 0)
            LoadSettings();
        if (_axisList.Count == 0)
            return;
        foreach (var (letter, tag, value) in _settings)
        {
            var axis = letter == null ? _axisList[0] : GetAxis(letter.Value);
            axis?.SetSetting(tag, value, true);
        }
    }

    public bool TryGetSetting(string tag, out double value)
    {
        var index = _settings.FindIndex(s => s.tag == tag);
        if (index < 0)
        {
            value = double.NaN;
            return false;
        }
        value = _settings[index].value;
        return true;
    }

    public Stage? GetModel()
    {
        if (_settings.Count == 0)
            LoadSettings();
        var index = _settings.FindIndex(s => s.tag.StartsWith("XLA"));
        return index < 0 ? null : Stage.GetStage($"{_settings[index].tag}={_settings[index].value}");
    }

    private async Task ProcessDataAsync()
    {
        Debug.WriteLine("Data processing thread started...");
        var token = _cancellationTokenSource?.Token ?? CancellationToken.None;

        while (!token.IsCancellationRequested && Connected)
        {
            var line = "";
            try
            {
                await Task.Delay(5, token);
                line = _serialPort!.ReadLine().Trim();
                if (string.IsNullOrEmpty(line)) continue;
                var match = SettingsPattern().Match(line);
                if (!match.Success) continue;
                var letter = match.Groups["letter"].Success ? (char?)match.Groups["letter"].Value[0] : null;
                var tag = match.Groups["tag"].Value;
                if (tag.Length != 4) continue;
                var value = int.Parse(match.Groups["value"].Value);
                var axis = letter == null ? _axisList[0] : GetAxis(letter.Value);
                axis!.ReceiveData(tag, value);
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"Serial port error: {ex.Message}");
                break;
            }
            catch (OperationCanceledException)
            {
                Debug.WriteLine("Data processing thread was cancelled.");
                break;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in processing line \"{line}\": {ex.Message}\n{ex.StackTrace}");
            }
        }

        Debug.WriteLine("Data processing thread stopped...");
    }
}
