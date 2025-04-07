using System;
using System.Reactive;
using System.Reactive.Linq;
using System.Reactive.Subjects;
using ReactiveUI;

namespace XeryonApp.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public Interaction<Exception, Unit> ExceptionThrown { get; } =
        new Interaction<Exception, Unit>(RxApp.MainThreadScheduler);

    protected async void ThrowException(Exception ex)
    {
        await ExceptionThrown.Handle(ex);
    }
}