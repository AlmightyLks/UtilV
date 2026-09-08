using System;
using System.IO;
using UtilV.Configuration;
using Xunit;

namespace UtilV.Tests;

public class SettingsStoreTests : IDisposable
{
    private readonly string _configRoot;
    private readonly string? _previousXdg;

    public SettingsStoreTests()
    {
        _configRoot = Path.Combine(Path.GetTempPath(), "utilv-tests-" + Guid.NewGuid().ToString("N"));
        _previousXdg = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _configRoot);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable("XDG_CONFIG_HOME", _previousXdg);

        if (Directory.Exists(_configRoot))
            Directory.Delete(_configRoot, recursive: true);
    }

    [Fact]
    public void Path_follows_the_XDG_config_directory()
    {
        Assert.Equal(Path.Combine(_configRoot, "utilv", "settings.json"), SettingsStore.Path);
    }

    [Fact]
    public void Defaults_are_returned_when_no_file_exists()
    {
        var settings = SettingsStore.Load();

        Assert.Equal(UtilVSettings.DefaultMaxHistoryEntries, settings.MaxHistoryEntries);
        Assert.Equal(UtilVSettings.DefaultMaxPayloadMegabytes, settings.MaxPayloadMegabytes);
    }

    [Fact]
    public void Saved_values_round_trip()
    {
        SettingsStore.Save(new UtilVSettings { MaxHistoryEntries = 40, MaxPayloadMegabytes = 8 });

        var loaded = SettingsStore.Load();

        Assert.Equal(40, loaded.MaxHistoryEntries);
        Assert.Equal(8, loaded.MaxPayloadMegabytes);
    }

    [Fact]
    public void Save_creates_the_config_directory()
    {
        SettingsStore.Save(new UtilVSettings());

        Assert.True(File.Exists(SettingsStore.Path));
    }

    [Fact]
    public void Out_of_range_values_are_clamped_on_save_and_load()
    {
        SettingsStore.Save(new UtilVSettings { MaxHistoryEntries = 100_000, MaxPayloadMegabytes = 0 });

        var loaded = SettingsStore.Load();

        Assert.Equal(UtilVSettings.MaxHistoryEntriesLimit, loaded.MaxHistoryEntries);
        Assert.Equal(UtilVSettings.MinPayloadMegabytes, loaded.MaxPayloadMegabytes);
    }

    [Fact]
    public void A_corrupt_file_falls_back_to_defaults_rather_than_throwing()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(SettingsStore.Path)!);
        File.WriteAllText(SettingsStore.Path, "{ this is not json");

        var loaded = SettingsStore.Load();

        Assert.Equal(UtilVSettings.DefaultMaxHistoryEntries, loaded.MaxHistoryEntries);
    }

    [Fact]
    public void Megabytes_convert_to_bytes()
    {
        var settings = new UtilVSettings { MaxPayloadMegabytes = 4 };

        Assert.Equal(4 * 1024 * 1024, settings.MaxPayloadBytes);
    }
}
