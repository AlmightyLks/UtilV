using System.Linq;
using UtilV.Core;
using UtilV.Models;
using Xunit;

namespace UtilV.Tests;

public class ClipHistoryTests
{
    private static ClipboardSnapshot Text(string value) => ClipboardSnapshot.ForText(value);

    [Fact]
    public void Add_puts_the_newest_entry_first()
    {
        var history = new ClipHistory();

        history.Add(Text("first"));
        history.Add(Text("second"));

        Assert.Equal(["second", "first"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Adding_existing_content_promotes_it_instead_of_duplicating()
    {
        var history = new ClipHistory();

        history.Add(Text("a"));
        history.Add(Text("b"));
        history.Add(Text("c"));
        history.Add(Text("a"));     // already present, three positions back

        Assert.Equal(["a", "c", "b"], history.Entries.Select(e => e.Text));
        Assert.Equal(3, history.Entries.Count);
    }

    [Fact]
    public void Promotion_keeps_the_pinned_state()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("keep me"));
        history.TogglePin(pinned);
        history.Add(Text("other"));

        history.Add(Text("keep me"));

        var promoted = history.Entries.First();
        Assert.Equal("keep me", promoted.Text);
        Assert.True(promoted.IsPinned);
    }

    [Fact]
    public void Promotion_refreshes_the_capture_time()
    {
        var history = new ClipHistory();

        var entry = history.Add(Text("x"));
        var original = entry.CapturedAt;
        history.Add(Text("y"));

        history.Add(Text("x"));

        Assert.True(entry.CapturedAt >= original);
    }

    [Fact]
    public void Unpinned_entries_are_capped()
    {
        var history = new ClipHistory();

        for (int i = 0; i < history.MaxUnpinned + 10; i++)
            history.Add(Text($"entry {i}"));

        Assert.Equal(history.MaxUnpinned, history.Entries.Count);

        // The oldest go first, so the most recent capture survives.
        Assert.Equal($"entry {history.MaxUnpinned + 9}", history.Entries.First().Text);
    }

    [Fact]
    public void Pinned_entries_are_exempt_from_the_cap()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("pinned"));
        history.TogglePin(pinned);

        for (int i = 0; i < history.MaxUnpinned + 10; i++)
            history.Add(Text($"entry {i}"));

        Assert.Contains(history.Entries, e => e.Text == "pinned");
        Assert.Equal(history.MaxUnpinned + 1, history.Entries.Count);
    }

    [Fact]
    public void Unpinning_can_push_the_list_back_under_the_cap()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("pinned"));
        history.TogglePin(pinned);

        for (int i = 0; i < history.MaxUnpinned; i++)
            history.Add(Text($"entry {i}"));

        Assert.Equal(history.MaxUnpinned + 1, history.Entries.Count);

        history.TogglePin(pinned);

        Assert.Equal(history.MaxUnpinned, history.Entries.Count);
        Assert.DoesNotContain(history.Entries, e => e.Text == "pinned");
    }

    [Fact]
    public void Pinning_lifts_the_entry_to_the_top()
    {
        var history = new ClipHistory();

        history.Add(Text("a"));
        var b = history.Add(Text("b"));
        history.Add(Text("c"));
        Assert.Equal(["c", "b", "a"], history.Entries.Select(e => e.Text));

        history.TogglePin(b);

        Assert.Equal(["b", "c", "a"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void New_captures_land_below_pinned_entries()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("pinned"));
        history.TogglePin(pinned);

        history.Add(Text("newer"));

        Assert.Equal(["pinned", "newer"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Promoting_an_unpinned_entry_keeps_it_below_the_pins()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("pinned"));
        history.TogglePin(pinned);
        history.Add(Text("x"));
        history.Add(Text("y"));

        history.Add(Text("x"));     // promote an existing unpinned entry

        Assert.Equal(["pinned", "x", "y"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Unpinning_drops_the_entry_back_into_the_unpinned_block()
    {
        var history = new ClipHistory();

        var a = history.Add(Text("a"));
        history.Add(Text("b"));
        history.Add(Text("c"));
        history.TogglePin(a);
        Assert.Equal(["a", "c", "b"], history.Entries.Select(e => e.Text));

        history.TogglePin(a);

        // Back among the unpinned in capture order. "a" was the oldest, so it goes last
        // rather than being treated as the newest thing copied.
        Assert.Equal(["c", "b", "a"], history.Entries.Select(e => e.Text));
        Assert.False(a.IsPinned);
    }

    [Fact]
    public void Multiple_pins_stay_grouped_at_the_top()
    {
        var history = new ClipHistory();

        var a = history.Add(Text("a"));
        var b = history.Add(Text("b"));
        history.Add(Text("c"));

        history.TogglePin(a);
        history.TogglePin(b);

        Assert.Equal(["b", "a", "c"], history.Entries.Select(e => e.Text));
        Assert.True(history.Entries.Take(2).All(e => e.IsPinned));
    }

    [Fact]
    public void Lowering_the_cap_trims_immediately()
    {
        var history = new ClipHistory();

        for (int i = 0; i < 10; i++)
            history.Add(Text($"entry {i}"));

        Assert.Equal(10, history.Entries.Count);

        history.MaxUnpinned = 4;

        Assert.Equal(4, history.Entries.Count);
        Assert.Equal("entry 9", history.Entries.First().Text);
    }

    [Fact]
    public void Lowering_the_cap_still_spares_pinned_entries()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("pinned"));
        history.TogglePin(pinned);

        for (int i = 0; i < 10; i++)
            history.Add(Text($"entry {i}"));

        history.MaxUnpinned = 2;

        Assert.Equal(3, history.Entries.Count);
        Assert.Equal("pinned", history.Entries.First().Text);
    }

    [Fact]
    public void Promote_moves_an_entry_back_to_the_top()
    {
        var history = new ClipHistory();

        var a = history.Add(Text("a"));
        history.Add(Text("b"));
        history.Add(Text("c"));

        history.Promote(a);

        Assert.Equal(["a", "c", "b"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Promote_keeps_an_unpinned_entry_below_the_pins()
    {
        var history = new ClipHistory();

        var pinned = history.Add(Text("pinned"));
        history.TogglePin(pinned);
        var x = history.Add(Text("x"));
        history.Add(Text("y"));

        history.Promote(x);

        Assert.Equal(["pinned", "x", "y"], history.Entries.Select(e => e.Text));
    }

    [Fact]
    public void Promote_ignores_an_entry_that_is_not_in_the_list()
    {
        var history = new ClipHistory();

        var entry = history.Add(Text("gone"));
        history.Remove(entry);

        history.Promote(entry);     // must not resurrect it

        Assert.Empty(history.Entries);
    }

    [Fact]
    public void Removed_content_can_be_added_again()
    {
        var history = new ClipHistory();

        var entry = history.Add(Text("gone"));
        history.Remove(entry);
        Assert.Empty(history.Entries);

        history.Add(Text("gone"));

        // The hash index must have been cleared, or this would be swallowed as a duplicate.
        Assert.Single(history.Entries);
    }

    [Fact]
    public void Images_dedup_on_content_not_reference()
    {
        var history = new ClipHistory();
        byte[] png = [1, 2, 3, 4, 5];

        history.Add(ClipboardSnapshot.ForImage(png));
        history.Add(ClipboardSnapshot.ForImage([.. png]));   // equal bytes, different array

        Assert.Single(history.Entries);
    }

    [Fact]
    public void Text_and_image_with_the_same_bytes_are_distinct_entries()
    {
        var history = new ClipHistory();

        history.Add(Text("AB"));
        history.Add(ClipboardSnapshot.ForImage("AB"u8.ToArray()));

        Assert.Equal(2, history.Entries.Count);
    }
}
