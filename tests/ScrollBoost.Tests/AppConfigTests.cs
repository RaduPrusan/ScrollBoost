using ScrollBoost.Profiles;

namespace ScrollBoost.Tests;

public class AppConfigTests
{
    [Fact]
    public void DefaultConfig_HasSensibleDefaults()
    {
        var config = AppConfig.CreateDefault();
        Assert.Equal(1.0, config.DefaultProfile.BaseMultiplier);
        Assert.Equal("sigmoid", config.DefaultProfile.CurveType);
        Assert.Equal(0.8, config.DefaultProfile.Acceleration);
        Assert.Equal(30.0, config.DefaultProfile.MaxMultiplier);
        Assert.True(config.Enabled);
        Assert.Equal("none", config.StartupMode);
    }

    [Fact]
    public void RoundTrip_SerializeDeserialize()
    {
        var config = AppConfig.CreateDefault();
        config.AppProfiles["firefox"] = new ScrollProfile
        {
            BaseMultiplier = 2.0,
            CurveType = "power",
            Acceleration = 0.6,
            MaxMultiplier = 15.0
        };

        string json = config.ToJson();
        var loaded = AppConfig.FromJson(json);

        Assert.Equal(config.DefaultProfile.BaseMultiplier, loaded.DefaultProfile.BaseMultiplier);
        Assert.True(loaded.AppProfiles.ContainsKey("firefox"));
        Assert.Equal(2.0, loaded.AppProfiles["firefox"].BaseMultiplier);
    }

    [Fact]
    public void FromJson_MissingFields_GetDefaults()
    {
        string json = """{"configVersion": 1, "defaultProfile": {"baseMultiplier": 3.0}}""";
        var config = AppConfig.FromJson(json);
        Assert.Equal(3.0, config.DefaultProfile.BaseMultiplier);
        Assert.Equal("sigmoid", config.DefaultProfile.CurveType);
        Assert.True(config.Enabled);
    }

    [Fact]
    public void FromJson_InvalidJson_ReturnsDefault()
    {
        var config = AppConfig.FromJson("not valid json {{{");
        Assert.Equal(1.0, config.DefaultProfile.BaseMultiplier);
    }

    [Fact]
    public void FromJson_LegacyStartWithWindowsTrue_MapsToRegistry()
    {
        string json = """{"configVersion": 1, "startWithWindows": true}""";
        var config = AppConfig.FromJson(json);
        Assert.Equal("registry", config.StartupMode);
    }

    [Fact]
    public void FromJson_LegacyStartWithWindowsFalse_RemainsNone()
    {
        string json = """{"configVersion": 1, "startWithWindows": false}""";
        var config = AppConfig.FromJson(json);
        Assert.Equal("none", config.StartupMode);
    }
}
