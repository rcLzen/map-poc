using DwgMapOverlayPoc.Models;
using Microsoft.Extensions.Configuration;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Provides the catalogue of available base-map providers and their tile
/// sources, including dark/light variants and optional Mapbox integration.
///
/// Providers catalogue
/// ───────────────────
///  "osm"    OpenStreetMap Standard (light) / CartoDB Dark Matter (dark)
///           Free, no token required. OSM has no native dark style, so the
///           dark variant falls back to CartoDB Dark Matter tiles.
///
///  "carto"  CartoDB Positron (light) / CartoDB Dark Matter (dark)
///           Free, no token required. Clean minimalist styles – ideal for
///           overlaying DWG drawings because the background is low-contrast.
///
///  "mapbox" Mapbox Streets v12 (light) / Mapbox Dark v11 (dark)
///           Requires a valid Mapbox access token configured in
///           wwwroot/appsettings.json under "Mapbox:Token".
///           Uses 256 px raster tiles (no tileSize/zoomOffset adjustment).
/// </summary>
public class BaseMapService
{
    // -----------------------------------------------------------------------
    // Configuration key
    // -----------------------------------------------------------------------

    /// <summary>
    /// The IConfiguration key for the Mapbox access token.
    /// Set in wwwroot/appsettings.json → { "Mapbox": { "Token": "pk...." } }
    /// </summary>
    public const string MapboxTokenConfigKey = "Mapbox:Token";

    /// <summary>Placeholder token displayed in the UI when no real token is set.</summary>
    public const string MapboxTokenPlaceholder = "pk.REPLACE_WITH_YOUR_MAPBOX_TOKEN";

    // -----------------------------------------------------------------------
    // Fields
    // -----------------------------------------------------------------------

    private readonly string _mapboxToken;
    private readonly IReadOnlyList<BasemapGroup> _groups;

    // -----------------------------------------------------------------------
    // Constructor
    // -----------------------------------------------------------------------

    public BaseMapService(IConfiguration config)
    {
        _mapboxToken = config[MapboxTokenConfigKey] ?? MapboxTokenPlaceholder;
        _groups = BuildGroups();
    }

    // -----------------------------------------------------------------------
    // Public API
    // -----------------------------------------------------------------------

    /// <summary>Key of the provider shown selected on first load.</summary>
    public string DefaultGroupKey => "osm";

    /// <summary>Theme shown active on first load.</summary>
    public MapTheme DefaultTheme => MapTheme.Light;

    /// <summary>Whether the configured Mapbox token is a real (non-placeholder) value.</summary>
    public bool HasValidMapboxToken =>
        !string.IsNullOrWhiteSpace(_mapboxToken) &&
        _mapboxToken != MapboxTokenPlaceholder;

    /// <summary>Returns all provider groups in display order.</summary>
    public IReadOnlyList<BasemapGroup> GetGroups() => _groups;

    /// <summary>Looks up a group by its key (case-insensitive).</summary>
    public BasemapGroup? GetGroup(string key) =>
        _groups.FirstOrDefault(g => string.Equals(g.Key, key, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Returns the correct <see cref="TileSourceModel"/> for the given
    /// provider key and theme. Falls back to the OSM light source if the
    /// group is not found.
    /// </summary>
    public TileSourceModel GetTileSource(string groupKey, MapTheme theme)
    {
        var group = GetGroup(groupKey) ?? _groups[0];
        return group.GetSource(theme);
    }

    /// <summary>Returns the default tile source (OSM light, Houston-centred).</summary>
    public TileSourceModel GetDefault() => GetTileSource(DefaultGroupKey, DefaultTheme);

    // -----------------------------------------------------------------------
    // Private builder
    // -----------------------------------------------------------------------

    private IReadOnlyList<BasemapGroup> BuildGroups()
    {
        // ── Shared dark-mode source for providers without native dark tiles ──
        var cartoDark = new TileSourceModel
        {
            Label       = "CartoDB Dark Matter",
            ProviderKey = "carto",
            Theme       = MapTheme.Dark,
            UrlTemplate = "https://{s}.basemaps.cartocdn.com/dark_all/{z}/{x}/{y}{r}.png",
            Attribution = "&copy; <a href='https://www.openstreetmap.org/copyright' target='_blank'>OpenStreetMap</a> contributors &copy; <a href='https://carto.com/attributions' target='_blank'>CARTO</a>",
            MaxZoom     = 19,
            TileSize    = 256,
            ZoomOffset  = 0
        };

        return
        [
            // ── OpenStreetMap ────────────────────────────────────────────────
            // UI screenshot description:
            //   Radio: [●] OpenStreetMap
            //   Light → familiar OSM colour scheme, street names, POIs
            //   Dark  → CartoDB Dark Matter (dark grey/black bg, white labels)
            new BasemapGroup
            {
                Key         = "osm",
                DisplayName = "OpenStreetMap",
                Description = "Free community map · no token required",
                RequiresToken = false,
                LightSource = new TileSourceModel
                {
                    Label       = "OpenStreetMap Standard",
                    ProviderKey = "osm",
                    Theme       = MapTheme.Light,
                    UrlTemplate = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
                    Attribution = "&copy; <a href='https://www.openstreetmap.org/copyright' target='_blank'>OpenStreetMap</a> contributors",
                    MaxZoom     = 19,
                    TileSize    = 256,
                    ZoomOffset  = 0
                },
                // OSM has no native dark tiles; use CartoDB Dark Matter as proxy
                DarkSource = cartoDark
            },

            // ── CartoDB ──────────────────────────────────────────────────────
            // UI screenshot description:
            //   Radio: [○] CartoDB
            //   Light → CartoDB Positron: very light grey, minimal labels,
            //           excellent backdrop for DWG overlays
            //   Dark  → CartoDB Dark Matter: deep charcoal bg, white streets,
            //           great for night-mode workflows
            new BasemapGroup
            {
                Key         = "carto",
                DisplayName = "CartoDB",
                Description = "Minimalist tiles · no token required",
                RequiresToken = false,
                LightSource = new TileSourceModel
                {
                    Label       = "CartoDB Positron",
                    ProviderKey = "carto",
                    Theme       = MapTheme.Light,
                    UrlTemplate = "https://{s}.basemaps.cartocdn.com/light_all/{z}/{x}/{y}{r}.png",
                    Attribution = "&copy; <a href='https://www.openstreetmap.org/copyright' target='_blank'>OpenStreetMap</a> contributors &copy; <a href='https://carto.com/attributions' target='_blank'>CARTO</a>",
                    MaxZoom     = 19,
                    TileSize    = 256,
                    ZoomOffset  = 0
                },
                DarkSource = cartoDark
            },

            // ── Mapbox ───────────────────────────────────────────────────────
            // UI screenshot description:
            //   Radio: [○] Mapbox Streets
            //   Badge: "Token required" shown when token is placeholder
            //   Light → Mapbox Streets v12: polished streets, rich POI icons,
            //           Mapbox branding, familiar Google-Maps aesthetic
            //   Dark  → Mapbox Dark v11: dark navy bg, amber road labels,
            //           premium look for night/industrial use
            // To enable: replace "Mapbox:Token" in wwwroot/appsettings.json
            new BasemapGroup
            {
                Key           = "mapbox",
                DisplayName   = "Mapbox Streets",
                Description   = "Requires Mapbox access token",
                RequiresToken = true,
                LightSource = new TileSourceModel
                {
                    Label       = "Mapbox Streets",
                    ProviderKey = "mapbox",
                    Theme       = MapTheme.Light,
                    // 256 px raster endpoint – no tileSize/zoomOffset workaround needed
                    UrlTemplate = $"https://api.mapbox.com/styles/v1/mapbox/streets-v12/tiles/256/{{z}}/{{x}}/{{y}}?access_token={_mapboxToken}",
                    Attribution = "Map data &copy; <a href='https://www.openstreetmap.org/copyright' target='_blank'>OpenStreetMap</a> contributors, &copy; <a href='https://www.mapbox.com/' target='_blank'>Mapbox</a>",
                    MaxZoom     = 22,
                    TileSize    = 256,
                    ZoomOffset  = 0
                },
                DarkSource = new TileSourceModel
                {
                    Label       = "Mapbox Dark",
                    ProviderKey = "mapbox",
                    Theme       = MapTheme.Dark,
                    UrlTemplate = $"https://api.mapbox.com/styles/v1/mapbox/dark-v11/tiles/256/{{z}}/{{x}}/{{y}}?access_token={_mapboxToken}",
                    Attribution = "Map data &copy; <a href='https://www.openstreetmap.org/copyright' target='_blank'>OpenStreetMap</a> contributors, &copy; <a href='https://www.mapbox.com/' target='_blank'>Mapbox</a>",
                    MaxZoom     = 22,
                    TileSize    = 256,
                    ZoomOffset  = 0
                }
            }
        ];
    }
}
