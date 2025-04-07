using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace XeryonApp.Models;

[Flags]
public enum Status
{
    AmplifiersEnabled = 1 << 0,
    EndStop = 1 << 1,
    ThermalProtection1 = 1 << 2,
    ThermalProtection2 = 1 << 3,
    ForceZero = 1 << 4,
    MotorOn = 1 << 5,
    ClosedLoop = 1 << 6,
    EncoderAtIndex = 1 << 7,
    EncoderValid = 1 << 8,
    SearchingIndex = 1 << 9,
    PositionReached = 1 << 10,
    ErrorCompensation = 1 << 11,
    EncoderError = 1 << 12,
    Scanning = 1 << 13,
    LeftEndStop = 1 << 14,
    RightEndStop = 1 << 15,
    ErrorLimit = 1 << 16,
    SearchingOptimalFrequency = 1 << 17,
    SafetyTimeoutTriggered = 1 << 18,
    EtherCATAcknowledge = 1 << 19,
    EmergencyStop = 1 << 20,
    PositionFail = 1 << 21,
}

public class Axis
{
    public char Letter { get; }

    public Distance TargetPosition
    {
        get => new Distance(_data["DPOS"] * _stage.EncoderResolution, Distance.Type.NM);
        set
        {
            var dpos = (int)(value / _stage.EncoderResolution);
            SendCommand("DPOS", dpos);
            _wasValidDPOS = true;

            if (ForceWaiting)
            {
                var distance = Math.Abs(dpos - _data["EPOS"]);
                var sendTime = DateTime.UtcNow;

                while (!(IsWithinTolerance(dpos) && HasStatus(Status.PositionReached)))
                {
                    if (TimeoutReached(sendTime))
                    {
                        Debug.WriteLine($"Position not reached, timeout reached. (4) {value}");
                        break;
                    }

                    Thread.Sleep(100);
                }
            }
        }
    }

    public Distance CurrentPosition => new Distance(_data["EPOS"] * _stage.EncoderResolution, Distance.Type.NM);

    public bool ForceWaiting { get; set; } = false;

    //public double EncoderResolution => _stage.EncoderResolution;

    public delegate void DataReceived((string tag, int value) data);
    public event DataReceived? OnDataReceived;

    private readonly Xeryon _xeryon;
    private readonly Stage _stage;
    private readonly Dictionary<string, int> _settings = new(101);
    private readonly Dictionary<string, int> _data = new(29);
    private int _pollingInterval = 200;
    private int _updateNb;
    private bool _wasValidDPOS;

    public Axis(Xeryon xeryon, char letter, Stage stage)
    {
        _xeryon = xeryon;
        Letter = letter;
        _stage = stage;
        _data["DPOS"] = 0;
        _data["EPOS"] = 0;
        _data["STAT"] = 0;
        //_isLogging = false;
    }

    public void SendCommand(string tag)
    {
        SendCommandInternal(tag);
    }

    public void SendCommand(string tag, int value)
    {
        SendCommandInternal(tag, value);
        SetSetting(tag, value);
    }

    public void SetSetting(string tag, string value, bool fromSettingsFile = false) =>
        SetSetting(tag, double.Parse(value), fromSettingsFile);

    public void SetSetting(string tag, double value, bool fromSettingsFile = false)
    {
        if (fromSettingsFile)
        {
            value = ApplySettingsMultipliers(tag, value);
            if (tag == "MASS")
            {
                tag = "CFRQ";
            }
        }

        var intValue = (int)value;
        _settings[tag] = intValue;

        if (!fromSettingsFile)
        {
            SendCommandInternal(tag, intValue);
        }
    }

    public void SendSettings()
    {
        foreach (var setting in _settings)
        {
            SendCommandInternal(setting.Key, setting.Value);
        }

        //SendCommandInternal(_stage.EncoderResolutionCommand);
        SendCommandInternal("SAVE");
    }

    public int GetSetting(string tag) => _settings[tag];

    public bool TryGetSetting(string tag, out int value) => _settings.TryGetValue(tag, out value);

    public int GetData(string tag) => _data[tag];

    public bool TryGetData(string tag, out int value) => _data.TryGetValue(tag, out value);

    public Status Status => (Status)_data["STAT"];

    public bool HasStatus(Status status) => Status.HasFlag(status);

    public void FindIndex(int direction = 0)
    {
        Debug.WriteLine($"Searching for index {Status}");
        SendCommand("INDX", direction);
        _wasValidDPOS = false;

        while (!HasStatus(Status.EncoderValid))
        {
            WaitForUpdate();
            if (!HasStatus(Status.SearchingIndex))
            {
                Debug.WriteLine($"Index is not found, but stopped searching for index.");
                break;
            }

            Thread.Sleep(200);
        }

        if (HasStatus(Status.EncoderValid))
        {
            Debug.WriteLine($"Index of axis {Letter} found.");
        }
    }

    public void SetLimits(Distance lowLimit, Distance highLimit)
    {
        var low = (int)(lowLimit / _stage.EncoderResolution);
        var high = (int)(highLimit / _stage.EncoderResolution);
        SendCommand("LLIM", low);
        SendCommand("HLIM", high);
    }

    public void Step(Distance d)
    {
        var newDpos = _wasValidDPOS ? TargetPosition + d : CurrentPosition + d;
        TargetPosition = newDpos;
        WaitForUpdate();
        Debug.WriteLine($"Stepped {d}");
    }

    public void SetSpeed(Distance speed)
    {
        var s = (int)speed[Distance.Type.UM];
        SendCommand("SSPD", s);
        SendCommand("SAVE");
    }

    public void StartScan(int direction, int execTime = 0)
    {
        SendCommand("SCAN", direction);
        _wasValidDPOS = false;

        if (execTime > 0)
        {
            Thread.Sleep(execTime * 1000);
            SendCommand("SCAN", 0);
        }
    }

    public void StopScan()
    {
        SendCommand("SCAN", 0);
        _wasValidDPOS = false;
    }

    public void StartLogging()
    {
        //_isLogging = true;
        SetSetting("POLI", 1);
        WaitForUpdate();
    }

    public void EndLogging()
    {
        //_isLogging = false;
        SetSetting("POLI", _pollingInterval);
    }

    public void ResumeMovement()
    {
        SendCommand("CONT");
    }

    public void StopMovement()
    {
        SendCommand("STOP");
        _wasValidDPOS = false;
    }

    public void Stop()
    {
        SendCommand("STOP");
        SendCommand("ZERO");
        SendCommand("RSET");
        _wasValidDPOS = false;
    }

    public void Reset()
    {
        SendCommand("RSET");
        _wasValidDPOS = false;
    }

    public void ReceiveData(string tag, int value)
    {
        if (tag == "TIME")
        {
            ReceiveData("PCTIME", (int)(DateTime.UtcNow - DateTime.UnixEpoch).TotalMilliseconds);
        }

        _data[tag] = value;
        OnDataReceived?.Invoke((tag, value));

        if (tag == "EPOS")
        {
            _updateNb++; // Increment update count when "EPOS" is received
        }
    }

    public void Calibrate()
    {
        SendCommand("FFRQ", 0);
        _wasValidDPOS = false;

        WaitForUpdate();
        var lfrq = GetSetting("LFRQ");
        var hfrq = GetSetting("HFRQ");

        Debug.WriteLine("Start calibrating...");
        while (HasStatus(Status.SearchingOptimalFrequency))
        {
            var frq = GetData("FREQ");
            var freqRatio = ((frq - lfrq) * 100) / (hfrq - lfrq); // Calculate % finished

            if (freqRatio % 10 == 0)
            {
                Debug.WriteLine($"Calibrating: {freqRatio}%");
                Thread.Sleep(1000);
            }

            Thread.Sleep(200);
        }

        Debug.WriteLine("Calibrating finished.");
    }

    private void SendCommandInternal(string tag, int value)
    {
        _xeryon.SendCommand(this, $"{tag}={value}");
    }

    private void SendCommandInternal(string cmd)
    {
        _xeryon.SendCommand(this, cmd);
    }

    private bool IsWithinTolerance(int dpos)
    {
        dpos = Math.Abs(dpos);
        var pto2 = GetSetting("PTO2");
        var epos = Math.Abs(GetData("EPOS"));
        if (pto2 == 0) pto2 = 10;

        return (dpos - pto2) <= epos && epos <= (dpos + pto2);
    }

    private bool TimeoutReached(DateTime startTime)
    {
        var elapsedMilliseconds = (DateTime.UtcNow - startTime).TotalMilliseconds;

        var timeout = 1000;

        if (_settings.TryGetValue("TOUT", out var tout) && _settings.TryGetValue("TOU2", out var tou2))
        {
            timeout = (tout + tou2);
        }

        return elapsedMilliseconds > timeout;
    }

    private void WaitForUpdate()
    {
        var startNb = _updateNb;
        while (_updateNb - startNb < 3)
        {
            Thread.Sleep(10);
        }
    }

    private double ApplySettingsMultipliers(string tag, double value)
    {
        switch (tag)
        {
            case "MAMP":
            case "MIMP":
            case "OFSA":
            case "OFSB":
            case "AMPL":
            case "MAM2":
                // Volts in settings file to int in command
                value *= Stage.AmplitudeMultiplier;
                break;
            case "PHAC":
            case "PHAS":
                // Degrees in settings file to int in command
                value *= Stage.PhaseMultiplier;
                break;
            case "SSPD":
            case "MSPD":
            case "ISPD":
                // mm/s or deg/s in settings file to um/s or mdeg/s in command
                value *= _stage.SpeedMultiplier;
                break;
            case "ENCO":
            case "LLIM":
            case "RLIM":
            case "HLIM":
            case "ZON1":
            case "ZON2":
                // mm in settings file to encoder units in command
                value = (value * 1e6 / _stage.EncoderResolution);
                break;
            case "POLI":
                _pollingInterval = (int)value;
                break;
            case "MASS":
                value = value switch
                {
                    <= 50 => 100000,
                    <= 100 => 60000,
                    <= 250 => 30000,
                    <= 500 => 10000,
                    <= 1000 => 5000,
                    _ => 3000
                };
                break;
        }

        return value;
    }
}