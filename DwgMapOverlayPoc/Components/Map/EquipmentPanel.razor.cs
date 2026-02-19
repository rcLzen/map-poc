using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.AspNetCore.Components;

namespace DwgMapOverlayPoc.Components.Map;

/// <summary>Code-behind for EquipmentPanel.razor.</summary>
public partial class EquipmentPanel
{
    // ── GeoJSON auto-load tracking ────────────────────────────────────────────

    private string? _loadedGeoJsonUrl;

    /// <summary>
    /// Called on init and on every assets-changed event.
    /// Loads GeoJSON into the snap service if a new asset became ready.
    /// </summary>
    private void CheckAndLoadGeoJson()
    {
        var geo = AssetService.GeoJsonAsset;
        if (geo?.Status == UploadStatus.Ready
            && !geo.IsStub
            && geo.BlobUrl is not null
            && geo.BlobUrl != _loadedGeoJsonUrl)
        {
            _loadedGeoJsonUrl = geo.BlobUrl;
            _ = SnapService.LoadGeoJsonAsync(geo.BlobUrl);
        }
    }

    // ── Formatting ────────────────────────────────────────────────────────────

    private static string FormatMarkerCoords(EquipmentMarker m) =>
        $"{m.Lat:F5}, {m.Lng:F5}";

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task TogglePlacementAsync()
    {
        await EquipService.SetPlacementModeAsync(!EquipService.PlacementMode);
    }

    private async Task RemoveMarkerAsync(string id)
    {
        await EquipService.RemoveMarkerAsync(id);
    }

    private async Task ExportAsync()
    {
        await EquipService.DownloadGeoJsonAsync();
    }

    private async Task ClearAllAsync()
    {
        await EquipService.ClearAllAsync();
    }
}
