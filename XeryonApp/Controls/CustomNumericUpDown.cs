using Avalonia;
using Avalonia.Controls;
using System;

namespace XeryonApp.Controls;

public class CustomNumericUpDown : NumericUpDown
{
    protected override Type StyleKeyOverride => typeof(NumericUpDown);

    // Define the sorted list of custom values
    public decimal[] CustomValues
    {
        get => GetValue(CustomValuesProperty);
        set => SetValue(CustomValuesProperty, value);
    }

    public static readonly StyledProperty<decimal[]> CustomValuesProperty =
        AvaloniaProperty.Register<CustomNumericUpDown, decimal[]>(nameof(CustomValues), Array.Empty<decimal>());

    // Increment used when the current value is below the smallest value in the list
    public decimal LowIncrement
    {
        get => GetValue(LowIncrementProperty);
        set => SetValue(LowIncrementProperty, value);
    }

    public static readonly StyledProperty<decimal> LowIncrementProperty =
        AvaloniaProperty.Register<CustomNumericUpDown, decimal>(nameof(LowIncrement), 1.0m);

    // Increment used when the current value is above the largest value in the list
    public decimal HighIncrement
    {
        get => GetValue(HighIncrementProperty);
        set => SetValue(HighIncrementProperty, value);
    }

    public static readonly StyledProperty<decimal> HighIncrementProperty =
        AvaloniaProperty.Register<CustomNumericUpDown, decimal>(nameof(HighIncrement), 1.0m);

    protected override void OnSpin(SpinEventArgs e)
    {
        if (CustomValues.Length == 0)
        {
            base.OnSpin(e);
            return;
        }

        var currentValue = Value ?? Minimum;

        var newValue = e.Direction switch
        {
            SpinDirection.Increase when currentValue < CustomValues[0] - LowIncrement => currentValue + LowIncrement,
            SpinDirection.Increase when currentValue < CustomValues[^1] => GetNextValue(currentValue, true),
            SpinDirection.Increase => currentValue + HighIncrement,
            SpinDirection.Decrease when currentValue > CustomValues[^1] + HighIncrement => currentValue - HighIncrement,
            SpinDirection.Decrease when currentValue > CustomValues[0] => GetNextValue(currentValue, false),
            SpinDirection.Decrease => currentValue - LowIncrement,
            _ => currentValue
        };

        newValue = Math.Max(Minimum , Math.Min(Maximum, newValue));

        Value = newValue;
    }

    private decimal GetNextValue(decimal currentValue, bool increase)
    {
        var index = Array.BinarySearch(CustomValues, currentValue);
        if (index >= 0)
        {
            // Exact match found, move to the next value if possible
            return CustomValues[Math.Max(0, Math.Min(CustomValues.Length - 1, index + (increase ? 1 : -1)))];
        }
        else
        {
            // No exact match found, BinarySearch returns the bitwise complement of the next larger element's index
            index = ~index;
            if (!increase && index > 0 || index >= CustomValues.Length)
                --index;
            return CustomValues[index];
        }
    }
}
