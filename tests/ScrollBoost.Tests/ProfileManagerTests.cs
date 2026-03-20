using ScrollBoost.Profiles;

namespace ScrollBoost.Tests;

public class ProfileManagerTests
{
    [Fact]
    public void GetProfile_UnknownApp_ReturnsDefault()
    {
        var config = AppConfig.CreateDefault();
        var manager = new ProfileManager(config);
        var profile = manager.GetProfile("someunknownapp");
        Assert.Equal(config.DefaultProfile.BaseMultiplier, profile.BaseMultiplier);
    }

    [Fact]
    public void GetProfile_KnownApp_ReturnsAppProfile()
    {
        var config = AppConfig.CreateDefault();
        config.AppProfiles["firefox"] = new ScrollProfile
        {
            BaseMultiplier = 2.5,
            CurveType = "linear",
            Acceleration = 0.0,
            MaxMultiplier = 5.0
        };
        var manager = new ProfileManager(config);
        var profile = manager.GetProfile("firefox");
        Assert.Equal(2.5, profile.BaseMultiplier);
    }

    [Fact]
    public void GetProfile_CaseInsensitive()
    {
        var config = AppConfig.CreateDefault();
        config.AppProfiles["firefox"] = new ScrollProfile { BaseMultiplier = 2.5 };
        var manager = new ProfileManager(config);
        Assert.Equal(2.5, manager.GetProfile("Firefox").BaseMultiplier);
        Assert.Equal(2.5, manager.GetProfile("FIREFOX").BaseMultiplier);
    }
}
