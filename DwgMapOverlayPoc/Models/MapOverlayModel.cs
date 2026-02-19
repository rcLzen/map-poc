namespace DwgMapOverlayPoc.Models;

/// <summary>
/// Represents a single DWG/image overlay positioned on the Leaflet map.
/// </summary>
public class MapOverlayModel
{
    /// <summary>Unique identifier for this overlay layer.</summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>Display name shown in the sidebar layer list.</summary>
    public string Name { get; set; } = "Untitled Overlay";

    /// <summary>URL or base-64 data-URI of the image to display.</summary>
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>South-West corner (latitude, longitude) of the image bounds.</summary>
    public LatLng SouthWest { get; set; } = new(0, 0);

    /// <summary>North-East corner (latitude, longitude) of the image bounds.</summary>
    public LatLng NorthEast { get; set; } = new(0, 0);

    /// <summary>Opacity of the overlay layer (0.0 – 1.0).</summary>
    public double Opacity { get; set; } = 0.75;

    /// <summary>Whether this layer is currently visible on the map.</summary>
    public bool IsVisible { get; set; } = true;
}

/// <summary>
/// Represents a geographic coordinate pair.
/// </summary>
/// <param name="Lat">Latitude in decimal degrees.</param>
/// <param name="Lng">Longitude in decimal degrees.</param>
public record LatLng(double Lat, double Lng);
