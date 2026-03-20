#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ScrollBoost.Profiles;

public class AppConfig
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    [JsonPropertyName("configVersion")]
    public int ConfigVersion { get; set; } = 1;

    [JsonPropertyName("defaultProfile")]
    public ScrollProfile DefaultProfile { get; set; } = new();

    [JsonPropertyName("appProfiles")]
    public Dictionary<string, ScrollProfile> AppProfiles { get; set; } = new();

    [JsonPropertyName("gestureTimeoutMs")]
    public int GestureTimeoutMs { get; set; } = 250;

    [JsonPropertyName("smoothingAlpha")]
    public double SmoothingAlpha { get; set; } = 0.3;

    [JsonPropertyName("velocityWindowSize")]
    public int VelocityWindowSize { get; set; } = 4;

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; } = true;

    [JsonPropertyName("startupMode")]
    public string StartupMode { get; set; } = "none";

    /// <summary>
    /// Backward-compatibility shim for code still referencing the old bool property.
    /// Maps true → "registry", false → "none". Will be removed when UI is updated.
    /// </summary>
    [JsonIgnore]
    public bool StartWithWindows
    {
        get => StartupMode == "registry" || StartupMode == "scheduler";
        set => StartupMode = value ? "registry" : "none";
    }

    [JsonExtensionData]
    public Dictionary<string, JsonElement>? ExtensionData { get; set; }

    public static AppConfig CreateDefault() => new();

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static AppConfig FromJson(string json)
    {
        try
        {
            var config = JsonSerializer.Deserialize<AppConfig>(json, JsonOptions) ?? CreateDefault();

            // Migrate old startWithWindows: true → startupMode: "registry"
            if (config.ExtensionData != null &&
                config.ExtensionData.TryGetValue("startWithWindows", out var legacyValue) &&
                legacyValue.ValueKind == JsonValueKind.True)
            {
                config.StartupMode = "registry";
            }

            return config;
        }
        catch (JsonException)
        {
            return CreateDefault();
        }
    }

    public static AppConfig Load(string path)
    {
        if (!File.Exists(path)) return CreateDefault();
        string json = File.ReadAllText(path);
        return FromJson(json);
    }

    public void Save(string path)
    {
        File.WriteAllText(path, ToJson());
    }
}
