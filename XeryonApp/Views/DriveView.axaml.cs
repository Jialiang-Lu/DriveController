using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using XeryonApp.ViewModels;

namespace XeryonApp.Views;

public partial class DriveView : UserControl
{
    public DriveView()
    {
        InitializeComponent();
    }

    private void OnButtonPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is Drive drive && sender is Button button)
        {
            switch (button.Name)
            {
                case "ScanUp":
                    drive.StartScan(-1);
                    break;
                case "ScanDown":
                    drive.StartScan(1);
                    break;
            }
        }
    }

    private void OnButtonPointerReleased(object sender, PointerReleasedEventArgs e)
    {
        if (DataContext is Drive drive && sender is Button { Name: "ScanUp" or "ScanDown" } button )
        {
            drive.StopScan();
        }
    }
}