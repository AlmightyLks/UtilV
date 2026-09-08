using System;
using System.IO;
using System.Text.Json;

namespace UtilV.Configuration;

/// <summary>Reads and writes <see cref="UtilVSettings"/> under the XDG config directory.</summary>
internal static class SettingsStore
{
    /// <summary>
    /// $XDG_CONFIG_HOME/utilv/settings.json, falling back to ~/.config/utilv/settings.json
    /// as the XDG base directory specification prescribes.
    /// </summary>
    public static string Path
    {
        get
        {
            var config = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");

            if (string.IsNullOrEmpty(config))
            {
                config = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".config");
            }

            return System.IO.Path.Combine(config, "utilv", "settings.json");
        }
    }

    public static UtilVSettings Load()
    {
        try
        {
            if (!File.Exists(Path))
                return new UtilVSettings();

            var loaded = JsonSerializer.Deserialize(
                File.ReadAllText(Path), SettingsJsonContext.Default.UtilVSettings);
            return (loaded ?? new UtilVSettings()).Clamped();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // A corrupt or unreadable file must not stop the daemon starting.
            Diagnostics.Log($"settings: falling back to defaults ({ex.GetType().Name})");
            return new UtilVSettings();
        }
    }

    public static void Save(UtilVSettings settings)
    {
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);

            if (!string.IsNullOrEmpty(directory))
                Directory.CreateDirectory(directory);

            // Write-then-replace: a crash mid-write would otherwise leave a truncated file
            // that the next start has to discard.
            var temporary = Path + ".tmp";
            File.WriteAllText(temporary,
                JsonSerializer.Serialize(settings.Clamped(), SettingsJsonContext.Default.UtilVSettings));
            File.Move(temporary, Path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Diagnostics.Log($"settings: could not save ({ex.GetType().Name})");
        }
    }
}
