using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Provides GeoJSON vertex/edge snapping via Turf.js.
/// <see cref="LoadGeoJsonAsync"/> must be called before snapping works.
/// </summary>
public sealed class SnappingService(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    public bool SnapEnabled  { get; set; } = false;
    public int  SnapRadiusPx { get; set; } = 10;

    public event Action? OnChanged;

    /// <summary>Parse and cache GeoJSON in the browser for subsequent snap queries.</summary>
    public async Task LoadGeoJsonAsync(string blobUrl)
    {
        try
        {
            await _js.InvokeVoidAsync("snapInterop.loadGeoJson", blobUrl);
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[SnappingService] loadGeoJson failed: {ex.Message}");
        }
        OnChanged?.Invoke();
    }

    /// <summary>
    /// If snapping is enabled, queries the nearest GeoJSON vertex/edge within
    /// <see cref="SnapRadiusPx"/> pixels at the given zoom level.
    /// Returns the original coordinates unchanged when snapping is disabled
    /// or no nearby geometry is found.
    /// </summary>
    public async Task<(double Lat, double Lng, bool Snapped)> SnapAsync(
        double lat, double lng, int zoomLevel)
    {
        if (!SnapEnabled) return (lat, lng, false);

        // Convert pixel radius → approximate degree tolerance
        // 1 degree ≈ 256 * 2^zoom pixels along the equator
        double pixelsPerDeg = 256.0 * Math.Pow(2, zoomLevel) / 360.0;
        double toleranceDeg = SnapRadiusPx / pixelsPerDeg;

        try
        {
            var result = await _js.InvokeAsync<SnapResult?>(
                "snapInterop.snapNearest", lat, lng, toleranceDeg);

            if (result is not null)
                return (result.Lat, result.Lng, true);
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[SnappingService] snapNearest failed: {ex.Message}");
        }

        return (lat, lng, false);
    }

    private sealed record SnapResult(
        [property: JsonPropertyName("lat")] double Lat,
        [property: JsonPropertyName("lng")] double Lng);
}
