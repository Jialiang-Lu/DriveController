﻿using System;
using System.Reactive.Subjects;
using ReactiveUI;

namespace XeryonApp.ViewModels;

public class ViewModelBase : ReactiveObject
{
    public IObservable<Exception> ExplicitExceptions => _explicitExceptions;

    private readonly Subject<Exception> _explicitExceptions = new();

    protected void ThrowException(Exception ex)
    {
        _explicitExceptions.OnNext(ex);
    }
}