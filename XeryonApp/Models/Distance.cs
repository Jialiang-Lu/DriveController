using System;

namespace XeryonApp.Models;

public readonly struct Distance
{
    public enum Type
    {
        /// <summary>
        /// Millimeter
        /// </summary>
        MM,
        /// <summary>
        /// Micrometer
        /// </summary>
        UM,
        /// <summary>
        /// Nanometer
        /// </summary>
        NM,
        /// <summary>
        /// Inch
        /// </summary>
        INCH,
        /// <summary>
        /// Milli-inch
        /// </summary>
        MINCH
    }

    private readonly double _nm;

    public Distance(double value, Type type)
    {
        _nm = type switch
        {
            Type.MM => value * 1_000_000.0,
            Type.UM => value * 1_000.0,
            Type.NM => value,
            Type.INCH => value * 25.4 * 1_000_000.0,
            Type.MINCH => value * 25.4 * 1_000.0,
            _ => throw new ArgumentOutOfRangeException(nameof(type), "Unexpected unit type.")
        };
    }

    private Distance(double nm)
    {
        _nm = nm;
    }

    public double this[Type type] => type switch
    {
        Type.MM => _nm / 1_000_000.0,
        Type.UM => _nm / 1_000.0,
        Type.NM => _nm,
        Type.INCH => _nm / (25.4 * 1_000_000.0),
        Type.MINCH => _nm / (25.4 * 1_000.0),
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Unexpected unit type.")
    };

    public override string ToString() => $"{this[Type.UM]} um";

    public static Distance operator +(Distance d1, Distance d2) => new Distance(d1._nm + d2._nm);
    public static Distance operator -(Distance d1, Distance d2) => new Distance(d1._nm - d2._nm);
    public static Distance operator -(Distance d) => new Distance(-d._nm);
    public static implicit operator double(Distance d) => d._nm;
}
