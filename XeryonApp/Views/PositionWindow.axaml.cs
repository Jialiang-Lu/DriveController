using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace XeryonApp.Views;

public partial class PositionWindow : Window
{
    public PositionWindow()
    {
        InitializeComponent();
        LostFocusEvent.AddClassHandler<NumericUpDown>(MainWindow.NumericUpDownHandler);
        KeyDownEvent.AddClassHandler<NumericUpDown>(MainWindow.NumericUpDownHandler);
    }
}