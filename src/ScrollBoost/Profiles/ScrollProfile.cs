using System.Text.Json.Serialization;

namespace ScrollBoost.Profiles;

public class ScrollProfile
{
    [JsonPropertyName("baseMultiplier")]
    public double BaseMultiplier { get; set; } = 1.5;

    [JsonPropertyName("curveType")]
    public string CurveType { get; set; } = "sigmoid";

    [JsonPropertyName("acceleration")]
    public double Acceleration { get; set; } = 0.4;

    [JsonPropertyName("maxMultiplier")]
    public double MaxMultiplier { get; set; } = 12.0;
}
