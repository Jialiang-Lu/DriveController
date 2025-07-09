using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using System;
using System.Linq;
using System.Windows.Input;

namespace XeryonApp.Controls;

public class HoldButton : Button
{
    protected override Type StyleKeyOverride => typeof(Button);

    /// <summary>
    /// Defines the <see cref="Hold"/> event.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> HoldEvent =
        RoutedEvent.Register<Button, RoutedEventArgs>(nameof(Hold), RoutingStrategies.Bubble);

    /// <summary>
    /// Defines the <see cref="HoldCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> HoldCommandProperty =
        AvaloniaProperty.Register<Button, ICommand?>(nameof(HoldCommand), enableDataValidation: true);

    /// <summary>
    /// Defines the <see cref="HoldCommandParameter"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> HoldCommandParameterProperty =
        AvaloniaProperty.Register<Button, object?>(nameof(HoldCommandParameter));

    /// <summary>
    /// Raised when the user holds the button.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? Hold
    {
        add => AddHandler(HoldEvent, value);
        remove => RemoveHandler(HoldEvent, value);
    }

    /// <summary>
    /// Gets or sets an <see cref="ICommand"/> to be invoked when the button is held.
    /// </summary>
    public ICommand? HoldCommand
    {
        get => GetValue(HoldCommandProperty);
        set => SetValue(HoldCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a parameter to be passed to the <see cref="HoldCommand"/>.
    /// </summary>
    public object? HoldCommandParameter
    {
        get => GetValue(HoldCommandParameterProperty);
        set => SetValue(HoldCommandParameterProperty, value);
    }

    /// <summary>
    /// Defines the <see cref="Release"/> event.
    /// </summary>
    public static readonly RoutedEvent<RoutedEventArgs> ReleaseEvent =
        RoutedEvent.Register<Button, RoutedEventArgs>(nameof(Release), RoutingStrategies.Bubble);

    /// <summary>
    /// Defines the <see cref="ReleaseCommand"/> property.
    /// </summary>
    public static readonly StyledProperty<ICommand?> ReleaseCommandProperty =
        AvaloniaProperty.Register<Button, ICommand?>(nameof(ReleaseCommand), enableDataValidation: true);

    /// <summary>
    /// Defines the <see cref="ReleaseCommandParameter"/> property.
    /// </summary>
    public static readonly StyledProperty<object?> ReleaseCommandParameterProperty =
        AvaloniaProperty.Register<Button, object?>(nameof(ReleaseCommandParameter));

    /// <summary>
    /// Raised when the user releases the button.
    /// </summary>
    public event EventHandler<RoutedEventArgs>? Release
    {
        add => AddHandler(ReleaseEvent, value);
        remove => RemoveHandler(ReleaseEvent, value);
    }

    /// <summary>
    /// Gets or sets an <see cref="ICommand"/> to be invoked when the button is released.
    /// </summary>
    public ICommand? ReleaseCommand
    {
        get => GetValue(ReleaseCommandProperty);
        set => SetValue(ReleaseCommandProperty, value);
    }

    /// <summary>
    /// Gets or sets a parameter to be passed to the <see cref="ReleaseCommand"/>.
    /// </summary>
    public object? ReleaseCommandParameter
    {
        get => GetValue(ReleaseCommandParameterProperty);
        set => SetValue(ReleaseCommandParameterProperty, value);
    }

    /// <summary>
    /// Invokes the <see cref="Hold"/> event.
    /// </summary>
    protected virtual void OnHold()
    {
        if (IsEffectivelyEnabled)
        {
            var e = new RoutedEventArgs(HoldEvent);
            RaiseEvent(e);

            var (command, parameter) = (HoldCommand, HoldCommandParameter);
            if (!e.Handled && command is not null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
                e.Handled = true;
            }
        }
    }

    /// <summary>
    /// Invokes the <see cref="Release"/> event.
    /// </summary>
    protected virtual void OnRelease()
    {
        if (IsEffectivelyEnabled)
        {
            var e = new RoutedEventArgs(ReleaseEvent);
            RaiseEvent(e);
            var (command, parameter) = (ReleaseCommand, ReleaseCommandParameter);
            if (!e.Handled && command is not null && command.CanExecute(parameter))
            {
                command.Execute(parameter);
                e.Handled = true;
            }
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            OnHold();
        }
        base.OnPointerPressed(e);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (IsPressed && e.InitialPressMouseButton == MouseButton.Left)
        {
            e.Handled = true;

            if (this.GetVisualsAt(e.GetPosition(this)).Any(c => this == c || this.IsVisualAncestorOf(c)))
            {
                OnRelease();
            }
        }
        base.OnPointerReleased(e);
    }

    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        OnRelease();
        e.Handled = true;
    }
}