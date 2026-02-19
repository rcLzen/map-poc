namespace DwgMapOverlayPoc.Models;

/// <summary>Light or dark visual theme for a base map.</summary>
public enum MapTheme { Light, Dark }

/// <summary>
/// Describes a single tile layer source (base map or custom tile set).
/// </summary>
public class TileSourceModel
{
    /// <summary>Human-readable label shown in the UI.</summary>
    public string Label { get; set; } = string.Empty;

    /// <summary>
    /// Provider group key (e.g. "osm", "carto", "mapbox").
    /// Used to link a model back to its <see cref="BasemapGroup"/>.
    /// </summary>
    public string ProviderKey { get; set; } = string.Empty;

    /// <summary>Whether this is the light or dark variant of a provider.</summary>
    public MapTheme Theme { get; set; } = MapTheme.Light;

    /// <summary>
    /// Leaflet-compatible tile URL template.
    /// Supports {s} (subdomain), {z}/{x}/{y} (tile coords).
    /// For Mapbox, the token is embedded directly in the URL at build time.
    /// </summary>
    public string UrlTemplate { get; set; } = string.Empty;

    /// <summary>Attribution HTML rendered in the map corner.</summary>
    public string Attribution { get; set; } = string.Empty;

    /// <summary>Maximum native zoom level for this source.</summary>
    public int MaxZoom { get; set; } = 19;

    /// <summary>
    /// Tile pixel size passed to Leaflet's tileSize option.
    /// Most CDN tiles are 256 px. Mapbox vector-backed raster tiles are 512 px.
    /// </summary>
    public int TileSize { get; set; } = 256;

    /// <summary>
    /// Leaflet zoomOffset to compensate when TileSize != 256.
    /// Set to -1 when TileSize is 512 so zoom levels still align correctly.
    /// </summary>
    public int ZoomOffset { get; set; } = 0;

    /// <summary>Whether the tiles are stored locally (offline tile pack).</summary>
    public bool IsLocalTilePack { get; set; } = false;
}
