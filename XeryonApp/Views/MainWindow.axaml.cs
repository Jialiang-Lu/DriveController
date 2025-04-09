using System;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using MsBox.Avalonia;
using XeryonApp.Controls;
using XeryonApp.ViewModels;

namespace XeryonApp.Views;

public partial class MainWindow : Window
{
    private bool _isClosingConfirmed, _closeRequested, _resetBeforeClose;

    public MainWindow()
    {
        InitializeComponent();
        //LostFocusEvent.AddClassHandler<NumericUpDown>(NumericUpDownHandler);
        //KeyDownEvent.AddClassHandler<NumericUpDown>(NumericUpDownHandler);
        DataContext = new MainViewModel();
        Activated += OnActivated;
        Closing += OnClosing;
    }

    private void OnActivated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ExceptionThrown.RegisterHandler(async interaction =>
            {
                var ex = interaction.Input;
                Debug.WriteLine($"Exception: {ex.Message}");
                await MessageBoxManager.GetMessageBoxStandard("Error", $"{ex.Message}\n{ex.StackTrace}").ShowWindowDialogAsync(this);
                interaction.SetOutput(Unit.Default);
            });
            vm.ChangeSpeed.RegisterHandler(interaction =>
            {
                var (index, up) = interaction.Input;
                var speedControl = GetNamedChildByIndex<CustomNumericUpDown>(Drives, "Speed", index);
                speedControl?.Spin(up ? SpinDirection.Increase : SpinDirection.Decrease);
                interaction.SetOutput(Unit.Default);
            });
            vm.ChangeStep.RegisterHandler(interaction =>
            {
                var (index, up) = interaction.Input;
                var stepControl = GetNamedChildByIndex<CustomNumericUpDown>(Drives, "Step", index);
                stepControl?.Spin(up ? SpinDirection.Increase : SpinDirection.Decrease);
                interaction.SetOutput(Unit.Default);
            });
            vm.Start();
        }
    }

    private static T? GetNamedChildByIndex<T>(ItemsControl items, string name, int index) where T : Control
    {
        var panel = items.ItemsPanelRoot;
        if (panel == null || index < 0 || index >= panel.Children.Count)
            return null;
        var child = panel.Children[index];
        var namedChild = child.GetLogicalDescendants().OfType<T>().FirstOrDefault(c => c.Name == name);
        return namedChild;
    }

    private async void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is MainViewModel vm && !_isClosingConfirmed)
        {
            e.Cancel = true;
            if (_closeRequested)
                return;
            _closeRequested = true;
            await vm.DisposeAsync(_resetBeforeClose);
            _isClosingConfirmed = true;
            Close();
        }
    }

    private static void NumericUpDownHandler(object? sender, RoutedEventArgs e)
    {
        if (sender is not NumericUpDown numericUpDown) return;
        if (e is KeyEventArgs keyEventArgs && keyEventArgs.Key != Key.Enter) return;
        BindingOperations.GetBindingExpressionBase(numericUpDown, NumericUpDown.ValueProperty)?.UpdateSource();
    }

    private async void OnResetExistClick(object? sender, RoutedEventArgs e)
    {
        _resetBeforeClose = true;
        Close();
    }

    private void OnExistClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}