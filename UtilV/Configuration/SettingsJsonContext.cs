using System.Text.Json.Serialization;

namespace UtilV.Configuration;

/// <summary>
/// Source-generated serialization for the settings file. Required rather than preferred:
/// PublishAot turns off System.Text.Json's reflection fallback, so the reflection-based
/// overloads throw at runtime.
/// </summary>
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(UtilVSettings))]
internal partial class SettingsJsonContext : JsonSerializerContext;
