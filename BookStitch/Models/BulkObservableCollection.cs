using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace BookStitch.Models;

/// <summary>Collection-only replacement; callers own threading and all workflow work.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public void ReplaceAll(IEnumerable<T> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        CheckReentrancy();
        var replacement = values.ToArray(); // Self-replacement and enumeration failure are safe.
        Items.Clear();
        foreach (var value in replacement) Items.Add(value);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}
