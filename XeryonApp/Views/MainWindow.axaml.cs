using System;
using System.Diagnostics;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MsBox.Avalonia;
using XeryonApp.ViewModels;

namespace XeryonApp.Views;

public partial class MainWindow : Window
{
    private bool _isClosingConfirmed, _closeRequested;

    public MainWindow()
    {
        InitializeComponent();
        LostFocusEvent.AddClassHandler<NumericUpDown>(NumericUpDownHandler);
        KeyDownEvent.AddClassHandler<NumericUpDown>(NumericUpDownHandler);
        var vm = new MainViewModel();
        DataContext = vm;
        Activated += MainWindow_Activated;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Activated(object? sender, EventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            vm.ExceptionThrown.RegisterHandler(async interaction =>
            {
                var ex = interaction.Input;
                Debug.WriteLine($"Exception: {ex.Message}");
                await MessageBoxManager.GetMessageBoxStandard("Error", ex.Message).ShowWindowDialogAsync(this);
                interaction.SetOutput(Unit.Default);
            });
            vm.Start();
        }
    }

    private async void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (DataContext is IAsyncDisposable asyncDisposable && !_isClosingConfirmed)
        {
            e.Cancel = true;
            if (_closeRequested)
                return;
            _closeRequested = true;
            await asyncDisposable.DisposeAsync();
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
}