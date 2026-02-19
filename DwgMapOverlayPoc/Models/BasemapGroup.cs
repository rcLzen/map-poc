namespace DwgMapOverlayPoc.Models;

/// <summary>
/// Groups the light and dark tile-source variants for a single map provider
/// (e.g. OpenStreetMap, CartoDB, Mapbox). Rendered as one radio-button entry
/// in the sidebar; the active theme selects which variant is applied.
/// </summary>
public class BasemapGroup
{
    /// <summary>
    /// Stable identifier used as the radio button value.
    /// Examples: "osm", "carto", "mapbox".
    /// </summary>
    public string Key { get; init; } = string.Empty;

    /// <summary>Label shown next to the radio button in the sidebar.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>
    /// Optional short description shown beneath the radio label.
    /// Displayed only when the sidebar is expanded.
    /// </summary>
    public string Description { get; init; } = string.Empty;

    /// <summary>Whether this group requires an external API token to load.</summary>
    public bool RequiresToken { get; init; } = false;

    /// <summary>Tile source used when the active theme is Light.</summary>
    public TileSourceModel LightSource { get; init; } = new();

    /// <summary>
    /// Tile source used when the active theme is Dark.
    /// Providers without a native dark style (e.g. OSM) may share the Dark
    /// source with another provider (e.g. CartoDB Dark Matter).
    /// </summary>
    public TileSourceModel DarkSource { get; init; } = new();

    /// <summary>
    /// Returns the correct source for the requested theme.
    /// </summary>
    public TileSourceModel GetSource(MapTheme theme) =>
        theme == MapTheme.Dark ? DarkSource : LightSource;
}
