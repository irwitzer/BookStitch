using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;

namespace BookStitch.Models;

/// <summary>Collection-only replacement; callers own threading and all workflow work.</summary>
public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    /// <summary>Reorders the same item instances without discarding WPF row containers.</summary>
    public bool Reorder(IReadOnlyList<T> order)
    {
        ArgumentNullException.ThrowIfNull(order);
        var inventory = new HashSet<T>(this);
        if (order.Count != Count || order.Distinct().Count() != Count ||
            order.Any(item => !inventory.Contains(item))) throw new ArgumentException("Expected a permutation of existing items.", nameof(order));
        int CountMoves(bool backwards)
        {
            var simulation = this.ToList();
            var moves = 0;
            for (var step = 0; step < order.Count; step++)
            {
                var i = backwards ? order.Count - 1 - step : step;
                var from = simulation.IndexOf(order[i]);
                if (from == i) continue;
                var item = simulation[from];simulation.RemoveAt(from);simulation.Insert(i,item);moves++;
            }
            return moves;
        }
        // Moving the first item to the end must be one Move, not N-1 moves of
        // all its neighbours. Select the cheaper direction before notifying WPF.
        var reverse = CountMoves(true) < CountMoves(false);
        var changed = false;
        for (var step = 0; step < order.Count; step++)
        {
            var i = reverse ? order.Count - 1 - step : step;
            if (EqualityComparer<T>.Default.Equals(this[i], order[i])) continue;
            Move(IndexOf(order[i]), i);
            changed = true;
        }
        return changed;
    }

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
