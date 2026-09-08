using UtilV.Models;
using Xunit;

namespace UtilV.Tests;

public class ClipEntryTests
{
    [Fact]
    public void Preview_keeps_line_breaks()
    {
        var entry = ClipEntry.FromText("first\nsecond");

        Assert.Equal("first\nsecond", entry.Preview);
    }

    [Fact]
    public void Preview_normalises_windows_line_endings()
    {
        var entry = ClipEntry.FromText("first\r\nsecond");

        Assert.Equal("first\nsecond", entry.Preview);
    }

    [Fact]
    public void Preview_marks_the_lines_that_did_not_fit()
    {
        var entry = ClipEntry.FromText("one\ntwo\nthree\nfour");

        Assert.Equal("one\ntwo\nthree ...", entry.Preview);
    }

    [Fact]
    public void Preview_trims_the_surrounding_blank_lines()
    {
        var entry = ClipEntry.FromText("\n\n  body  \n\n");

        Assert.Equal("body", entry.Preview);
    }

    [Fact]
    public void Preview_truncates_a_very_long_line()
    {
        var entry = ClipEntry.FromText(new string('x', 500));

        Assert.Equal(new string('x', 120) + "...", entry.Preview);
    }

    [Fact]
    public void Preview_does_not_split_a_surrogate_pair_when_truncating()
    {
        // The rocket sits exactly on the 120-character cut, and is two UTF-16 units wide.
        var entry = ClipEntry.FromText(new string('x', 119) + "\U0001F680" + new string('x', 50));

        var preview = entry.Preview;

        Assert.Equal(new string('x', 119) + "...", preview);
        Assert.DoesNotContain(preview, char.IsSurrogate);
    }

    [Fact]
    public void Preview_describes_whitespace_only_content()
    {
        var entry = ClipEntry.FromText("   \n  ");

        Assert.Equal("6 whitespace characters", entry.Preview);
    }

    [Fact]
    public void Preview_describes_an_image_by_size()
    {
        var entry = ClipEntry.FromImage(new byte[2048]);

        Assert.Equal("Image (2 KB)", entry.Preview);
    }
}
