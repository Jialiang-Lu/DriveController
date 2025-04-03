using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace XeryonApp.ViewModels;

public class SortedObservableCollection<T> : ObservableCollection<T>
{
    private readonly IComparer<T> _comparer;

    public SortedObservableCollection(IComparer<T>? comparer = null)
    {
        _comparer = comparer ?? Comparer<T>.Default;
    }

    public SortedObservableCollection(IEnumerable<T> list,  IComparer<T>? comparer = null) : this(comparer)
    {
        foreach (var item in list)
        {
            Add(item);
        }
    }

    protected override void InsertItem(int index, T item)
    {
        index = ((List<T>)Items).BinarySearch(item, _comparer);
        if (index < 0)
        {
            index = ~index;
        }
        base.InsertItem(index, item);
    }

    protected override void MoveItem(int oldIndex, int newIndex)
    {
    }

    protected override void SetItem(int index, T item)
    {
        InsertItem(index, item);
    }
}