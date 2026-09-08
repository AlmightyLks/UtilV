using System.Text.RegularExpressions;
using UtilV.Configuration;
using UtilV.Core;
using UtilV.Models;
using UtilV.ViewModels;
using Xunit;

namespace UtilV.Tests;

public class VersionTests
{
    private static ClipboardViewModel CreateViewModel() =>
        new(new ClipHistory(), new FakeWriter(), new UtilVSettings());

    [Fact]
    public void Version_is_reported_for_the_settings_pane()
    {
        var version = CreateViewModel().Version;

        Assert.False(string.IsNullOrWhiteSpace(version));
        Assert.Matches(new Regex(@"^\d+\.\d+\.\d+"), version);
    }

    [Fact]
    public void Version_carries_no_build_metadata()
    {
        Assert.DoesNotContain("+", CreateViewModel().Version);
    }

    private sealed class FakeWriter : IClipboardWriter
    {
        public void Set(ClipEntry entry) { }
    }
}
