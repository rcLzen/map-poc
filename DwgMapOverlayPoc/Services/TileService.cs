using DwgMapOverlayPoc.Models;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Provides pre-configured tile sources and helpers for managing tile packs.
/// </summary>
public class TileService
{
    /// <summary>Returns the built-in base-map tile sources available to the user.</summary>
    public IReadOnlyList<TileSourceModel> GetBuiltInSources() =>
    [
        new TileSourceModel
        {
            Label = "OpenStreetMap",
            UrlTemplate = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
            Attribution = "&copy; <a href='https://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors",
            MaxZoom = 19
        },
        new TileSourceModel
        {
            Label = "OpenStreetMap (HOT)",
            UrlTemplate = "https://{s}.tile.openstreetmap.fr/hot/{z}/{x}/{y}.png",
            Attribution = "&copy; <a href='https://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors, Tiles courtesy of <a href='https://hot.openstreetmap.org/' target='_blank'>HOT</a>",
            MaxZoom = 19
        },
        new TileSourceModel
        {
            Label = "ESRI World Imagery",
            UrlTemplate = "https://server.arcgisonline.com/ArcGIS/rest/services/World_Imagery/MapServer/tile/{z}/{y}/{x}",
            Attribution = "Tiles &copy; Esri &mdash; Source: Esri, i-cubed, USDA, USGS, AEX, GeoEye, Getmapping, Aerogrid, IGN, IGP, UPR-EGP, and the GIS User Community",
            MaxZoom = 18
        }
    ];
}
