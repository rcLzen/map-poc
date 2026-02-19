using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;

namespace DwgMapOverlayPoc.Tests;

public class BaseMapServiceTests
{
    [Fact]
    public void UsesPlaceholderTokenWhenConfigMissing()
    {
        var service = new BaseMapService(new TestConfiguration());

        Assert.False(service.HasValidMapboxToken);
        Assert.Equal("osm", service.DefaultGroupKey);
        Assert.Equal(MapTheme.Light, service.DefaultTheme);

        var defaultSource = service.GetDefault();
        Assert.Equal("osm", defaultSource.ProviderKey);
        Assert.Equal(MapTheme.Light, defaultSource.Theme);
    }

    [Fact]
    public void UsesConfiguredMapboxTokenInUrls()
    {
        var token = "pk.test-token";
        var service = new BaseMapService(new TestConfiguration(new Dictionary<string, string?>
        {
            [BaseMapService.MapboxTokenConfigKey] = token
        }));

        Assert.True(service.HasValidMapboxToken);

        var mapbox = service.GetGroup("mapbox");
        Assert.NotNull(mapbox);
        Assert.Contains(token, mapbox!.LightSource.UrlTemplate);
        Assert.Contains(token, mapbox.DarkSource.UrlTemplate);
    }

internal sealed class TestConfiguration : IConfiguration
{
    private readonly Dictionary<string, string?> _values;

    public TestConfiguration() : this([]) { }

    public TestConfiguration(Dictionary<string, string?> values) => _values = values;

    public string? this[string key]
    {
        get => _values.TryGetValue(key, out var value) ? value : null;
        set => _values[key] = value;
    }

    public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();

    public IChangeToken GetReloadToken() => new TestChangeToken();

    public IConfigurationSection GetSection(string key) => new TestConfigurationSection(key, this);

    private sealed class TestConfigurationSection(string key, TestConfiguration root) : IConfigurationSection
    {
        public string Key => key;
        public string Path => key;
        public string? Value
        {
            get => root[key];
            set => root[key] = value;
        }

        public string? this[string name]
        {
            get => root[$"{key}:{name}"];
            set => root[$"{key}:{name}"] = value;
        }

        public IEnumerable<IConfigurationSection> GetChildren() => Array.Empty<IConfigurationSection>();

        public IChangeToken GetReloadToken() => new TestChangeToken();

        public IConfigurationSection GetSection(string name) => new TestConfigurationSection($"{key}:{name}", root);
    }

    private sealed class TestChangeToken : IChangeToken
    {
        public bool HasChanged => false;
        public bool ActiveChangeCallbacks => false;
        public IDisposable RegisterChangeCallback(Action<object?> callback, object? state) => new TestDisposable();
    }

    private sealed class TestDisposable : IDisposable
    {
        public void Dispose() { }
    }
}
}

public class OverlayServiceTests
{
    [Fact]
    public void AddAndRemoveOverlayUpdatesCollection()
    {
        var service = new OverlayService();
        var overlay = new MapOverlayModel { Name = "Test" };
        var notifications = 0;

        service.OnChange += () => notifications++;

        service.Add(overlay);

        Assert.Single(service.Overlays);
        Assert.Equal(1, notifications);

        var removed = service.Remove(overlay.Id);

        Assert.True(removed);
        Assert.Empty(service.Overlays);
        Assert.Equal(2, notifications);
    }

    [Fact]
    public void ToggleVisibilityAndSetOpacityClampValues()
    {
        var service = new OverlayService();
        var overlay = new MapOverlayModel { Name = "Test", Opacity = 0.5 };
        service.Add(overlay);

        service.ToggleVisibility(overlay.Id);
        Assert.False(overlay.IsVisible);

        service.SetOpacity(overlay.Id, 2.5);
        Assert.Equal(1.0, overlay.Opacity);

        service.SetOpacity(overlay.Id, -1.0);
        Assert.Equal(0.0, overlay.Opacity);
    }
}

public class TileServiceTests
{
    [Fact]
    public void ReturnsBuiltInSources()
    {
        var service = new TileService();

        var sources = service.GetBuiltInSources();

        Assert.Equal(3, sources.Count);
        Assert.Contains(sources, source => source.Label == "OpenStreetMap");
        Assert.Contains(sources, source => source.Label == "ESRI World Imagery");
    }
}
