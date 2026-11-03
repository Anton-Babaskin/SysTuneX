using System.Collections.ObjectModel;
using System.Collections.Specialized;
using SysTuneX.App.ViewModels;
using Xunit;

namespace SysTuneX.Core.Tests;

/// <summary>
/// The tweak and service lists filter as the user types. They used to clear the list and add back
/// every row that matched, which to WPF is every card on the page destroyed and rebuilt - on each
/// keystroke, on each return to the page, after each batch.
///
/// What matters is not only the result but what it took to get there: a row that stays on screen
/// must never be removed and added back, because that is exactly the rebuild.
/// </summary>
public sealed class FilteredViewTests
{
    private sealed record Row(string Name);

    private static readonly Row[] Master = [.. "abcdefghij".Select(c => new Row(c.ToString()))];

    [Fact]
    public void Narrowing_removes_the_rows_that_stopped_matching_and_touches_nothing_else()
    {
        ObservableCollection<Row> shown = [.. Master];
        List<NotifyCollectionChangedEventArgs> changes = Record(shown);

        FilteredView.ShowOnly(shown, Pick("acegi"));

        Assert.Equal(Pick("acegi"), shown);
        Assert.All(changes, change => Assert.Equal(NotifyCollectionChangedAction.Remove, change.Action));
        Assert.Equal(5, changes.Count);
    }

    [Fact]
    public void Widening_inserts_the_new_rows_where_they_belong()
    {
        ObservableCollection<Row> shown = [.. Pick("cg")];
        List<NotifyCollectionChangedEventArgs> changes = Record(shown);

        FilteredView.ShowOnly(shown, Master);

        Assert.Equal(Master, shown);
        Assert.All(changes, change => Assert.Equal(NotifyCollectionChangedAction.Add, change.Action));
        Assert.Equal(8, changes.Count);
    }

    /// <summary>The case that happens on every page return and after every batch.</summary>
    [Fact]
    public void Nothing_changes_when_nothing_changed()
    {
        ObservableCollection<Row> shown = [.. Pick("bdf")];
        List<NotifyCollectionChangedEventArgs> changes = Record(shown);

        FilteredView.ShowOnly(shown, Pick("bdf"));

        Assert.Empty(changes);
    }

    /// <summary>
    /// The rebuild this replaces, stated as a rule: across any sequence of filters, a row that is
    /// showing before and after one of them is never removed during it.
    /// </summary>
    [Fact]
    public void A_row_that_stays_showing_is_never_removed_and_added_back()
    {
        var random = new Random(20260930);
        ObservableCollection<Row> shown = [];

        for (int round = 0; round < 500; round++)
        {
            List<Row> wanted = [.. Master.Where(_ => random.Next(3) > 0)];
            var before = new HashSet<Row>(shown, ReferenceEqualityComparer.Instance);
            var removed = new List<Row>();

            shown.CollectionChanged += Track;
            FilteredView.ShowOnly(shown, wanted);
            shown.CollectionChanged -= Track;

            Assert.Equal(wanted, shown);
            Assert.DoesNotContain(removed, row => before.Contains(row) && wanted.Contains(row));

            void Track(object? sender, NotifyCollectionChangedEventArgs e)
            {
                switch (e.Action)
                {
                    // Clear() says Reset and names nothing, but it removed every row that was
                    // showing - which is precisely the rebuild this test is here to catch.
                    case NotifyCollectionChangedAction.Reset:
                        removed.AddRange(before);
                        break;

                    case NotifyCollectionChangedAction.Remove or NotifyCollectionChangedAction.Replace:
                        removed.AddRange(e.OldItems!.Cast<Row>());
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Every filter here narrows one master list, so order never changes - but a caller whose order
    /// did change still ends up with exactly what it asked for, by moving rather than rebuilding.
    /// </summary>
    [Fact]
    public void A_changed_order_is_followed_by_moving_rows_not_rebuilding_them()
    {
        ObservableCollection<Row> shown = [.. Pick("abc")];
        List<NotifyCollectionChangedEventArgs> changes = Record(shown);

        FilteredView.ShowOnly(shown, Pick("cab"));

        Assert.Equal(Pick("cab"), shown);
        Assert.All(changes, change => Assert.Equal(NotifyCollectionChangedAction.Move, change.Action));
    }

    [Fact]
    public void Rows_are_matched_by_identity_not_by_equality()
    {
        // Records compare by value; two rows with the same name are still two rows on screen.
        var twin = new Row("a");
        ObservableCollection<Row> shown = [Master[0]];

        FilteredView.ShowOnly(shown, [twin]);

        Assert.Same(twin, Assert.Single(shown));
    }

    private static List<Row> Pick(string names) => [.. names.Select(c => Master[c - 'a'])];

    private static List<NotifyCollectionChangedEventArgs> Record(ObservableCollection<Row> shown)
    {
        var changes = new List<NotifyCollectionChangedEventArgs>();
        shown.CollectionChanged += (_, e) => changes.Add(e);
        return changes;
    }
}
