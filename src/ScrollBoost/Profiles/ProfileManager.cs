using System;
using System.Collections.Generic;

namespace ScrollBoost.Profiles;

public class ProfileManager
{
    private AppConfig _config;
    private readonly Dictionary<string, ScrollProfile> _normalizedProfiles = new(StringComparer.OrdinalIgnoreCase);

    public AppConfig Config => _config;

    public ProfileManager(AppConfig config)
    {
        _config = config;
        RebuildIndex();
    }

    public void UpdateConfig(AppConfig config)
    {
        _config = config;
        RebuildIndex();
    }

    public ScrollProfile GetProfile(string processName)
    {
        if (string.IsNullOrEmpty(processName))
            return _config.DefaultProfile;

        return _normalizedProfiles.TryGetValue(processName, out var profile)
            ? profile
            : _config.DefaultProfile;
    }

    private void RebuildIndex()
    {
        _normalizedProfiles.Clear();
        foreach (var (key, value) in _config.AppProfiles)
        {
            _normalizedProfiles[key] = value;
        }
    }
}
