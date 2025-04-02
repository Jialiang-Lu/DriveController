using System;

namespace XeryonApp.Models;

public class Stage
{
    public const double AmplitudeMultiplier = 1456;
    public const double PhaseMultiplier = 182;

    public static LinearStage XLS_312 { get; } = new LinearStage("XLS1=312", 312.5, 1000);
    public static LinearStage XLS_1250 { get; } = new LinearStage("XLS1=1250", 1250, 1000);
    public static LinearStage XLS_78 { get; } = new LinearStage("XLS1=78", 78.125, 1000);
    public static LinearStage XLS_5 { get; } = new LinearStage("XLS1=5", 5, 1000);
    public static LinearStage XLS_1 { get; } = new LinearStage("XLS1=1", 1, 1000);
    public static LinearStage XLS_312_3N { get; } = new LinearStage("XLS3=312", 312.5, 1000);
    public static LinearStage XLS_1250_3N { get; } = new LinearStage("XLS3=1250", 1250, 1000);
    public static LinearStage XLS_78_3N { get; } = new LinearStage("XLS3=78", 78.125, 1000);
    public static LinearStage XLS_5_3N { get; } = new LinearStage("XLS3=5", 5, 1000);
    public static LinearStage XLS_1_3N { get; } = new LinearStage("XLS3=1", 1, 1000);
    public static LinearStage XLA_312 { get; } = new LinearStage("XLA1=312", 312.5, 1000);
    public static LinearStage XLA_1250 { get; } = new LinearStage("XLA1=1250", 1250, 1000);
    public static LinearStage XLA_78 { get; } = new LinearStage("XLA1=78", 78.125, 1000);

    public static RotationStage XRTA { get; } = new RotationStage("XRTA=109", 2 * Math.PI * 1000000 / 57600, 100, 57600);
    public static RotationStage XRTU_40_3 { get; } = new RotationStage("XRT1=2", 2 * Math.PI * 1000000 / 86400, 100, 86400);
    public static RotationStage XRTU_40_19 { get; } = new RotationStage("XRT1=18", 2 * Math.PI * 1000000 / 86400, 100, 86400);
    public static RotationStage XRTU_40_49 { get; } = new RotationStage("XRT1=47", 2 * Math.PI * 1000000 / 86400, 100, 86400);
    public static RotationStage XRTU_40_73 { get; } = new RotationStage("XRT1=73", 2 * Math.PI * 1000000 / 86400, 100, 86400);
    public static RotationStage XRTU_30_3 { get; } = new RotationStage("XRT1=3", 2 * Math.PI * 1000000 / 1843200, 100, 1843200);
    public static RotationStage XRTU_30_19 { get; } = new RotationStage("XRT1=19", 2 * Math.PI * 1000000 / 360000, 100, 360000);
    public static RotationStage XRTU_30_49 { get; } = new RotationStage("XRT1=49", 2 * Math.PI * 1000000 / 144000, 100, 144000);
    public static RotationStage XRTU_30_109 { get; } = new RotationStage("XRT1=109", 2 * Math.PI * 1000000 / 57600, 100, 57600);

    public string EncoderResolutionCommand { get; init; }
    public double EncoderResolution { get; init; }
    public double SpeedMultiplier { get; init; }

    public static Stage GetStage(string encoderResolutionCommand) => encoderResolutionCommand switch
    {
        "XLS1=312" => XLS_312,
        "XLS1=1250" => XLS_1250,
        "XLS1=78" => XLS_78,
        "XLS1=5" => XLS_5,
        "XLS1=1" => XLS_1,
        "XLS3=312" => XLS_312_3N,
        "XLS3=1250" => XLS_1250_3N,
        "XLS3=78" => XLS_78_3N,
        "XLS3=5" => XLS_5_3N,
        "XLS3=1" => XLS_1_3N,
        "XLA1=312" => XLA_312,
        "XLA1=1250" => XLA_1250,
        "XLA1=78" => XLA_78,
        "XRTA=109" => XRTA,
        "XRT1=2" => XRTU_40_3,
        "XRT1=18" => XRTU_40_19,
        "XRT1=47" => XRTU_40_49,
        "XRT1=73" => XRTU_40_73,
        "XRT1=3" => XRTU_30_3,
        "XRT1=19" => XRTU_30_19,
        "XRT1=49" => XRTU_30_49,
        "XRT1=109" => XRTU_30_109,
        _ => throw new ArgumentException("Unknown stage command."),
    };

    public Stage(string encoderResolutionCommand, double encoderResolution, double speedMultiplier)
    {
        EncoderResolutionCommand = encoderResolutionCommand;
        EncoderResolution = encoderResolution;
        SpeedMultiplier = speedMultiplier;
    }

    public virtual bool IsLinear => false;
}

public class LinearStage : Stage
{
    public LinearStage(string encoderResolutionCommand, double encoderResolution, double speedMultiplier)
        : base(encoderResolutionCommand, encoderResolution, speedMultiplier)
    {
    }

    public override bool IsLinear => true;
}

public class RotationStage : Stage
{
    public double EncCountsPerRev { get; init; }

    public RotationStage(string encoderResolutionCommand, double encoderResolution, double speedMultiplier,
        double encCountsPerRev)
        : base(encoderResolutionCommand, encoderResolution, speedMultiplier)
    {
        EncCountsPerRev = encCountsPerRev;
    }

    public override bool IsLinear => false;
}