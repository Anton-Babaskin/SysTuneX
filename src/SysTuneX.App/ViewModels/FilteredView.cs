using System.Collections.ObjectModel;

namespace SysTuneX.App.ViewModels;

/// <summary>
/// Brings a filtered list in line with what should be showing, by removing and inserting only the
/// rows that changed.
///
/// The filters used to clear the list and add every matching row back. To the list on screen each
/// of those is a row destroyed and a new one built - a card, a switch, a badge and three text
/// blocks - and it happened on every keystroke in the search box, on every return to the page and
/// after every batch. Typing "game" rebuilt the whole page four times, the switches lost their hover
/// and focus, and the scroll position went with them.
///
/// No WPF in here, deliberately: the tests compile this file on their own and run it anywhere.
/// </summary>
public static class FilteredView
{
    /// <summary>
    /// Leaves <paramref name="shown"/> holding exactly <paramref name="wanted"/>, in that order. A row
    /// that was showing and still is stays the same instance at every step: it is never removed and
    /// added back.
    /// </summary>
    public static void ShowOnly<T>(ObservableCollection<T> shown, IReadOnlyList<T> wanted)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(shown);
        ArgumentNullException.ThrowIfNull(wanted);

        var keep = new HashSet<T>(wanted, ReferenceEqualityComparer.Instance);

        for (int i = shown.Count - 1; i >= 0; i--)
        {
            if (!keep.Contains(shown[i]))
            {
                shown.RemoveAt(i);
            }
        }

        // What is left is in its original order, and every filter here narrows one master list, so
        // this is almost always inserts only. A move covers a caller whose order did change.
        for (int i = 0; i < wanted.Count; i++)
        {
            T item = wanted[i];

            if (i < shown.Count && ReferenceEquals(shown[i], item))
            {
                continue;
            }

            int at = IndexOf(shown, item, i + 1);

            if (at >= 0)
            {
                shown.Move(at, i);
            }
            else
            {
                shown.Insert(i, item);
            }
        }
    }

    private static int IndexOf<T>(ObservableCollection<T> list, T item, int from)
        where T : class
    {
        for (int i = from; i < list.Count; i++)
        {
            if (ReferenceEquals(list[i], item))
            {
                return i;
            }
        }

        return -1;
    }
}
