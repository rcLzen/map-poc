using DwgMapOverlayPoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Components.Map;

/// <summary>Code-behind for MapExportPanel.razor.</summary>
public partial class MapExportPanel : IDisposable
{
    private bool    _busy          = false;
    private string? _statusMessage = null;
    private string  _statusClass   = "text-success";

    protected override Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
            EquipService.OnChanged += HandleChanged;
        return Task.CompletedTask;
    }

    private void HandleChanged() => InvokeAsync(StateHasChanged);

    // ── Screenshot ────────────────────────────────────────────────────────────

    private async Task ScreenshotAsync()
    {
        if (_busy) return;

        _busy          = true;
        _statusMessage = null;
        StateHasChanged();

        try
        {
            var dataUrl  = await JS.InvokeAsync<string>(
                "mapExportInterop.captureMapPng", ".leaflet-container");

            var filename = $"map-{DateTime.UtcNow:yyyyMMdd-HHmmss}.png";
            await JS.InvokeVoidAsync("mapExportInterop.downloadPng", dataUrl, filename);

            _statusMessage = "Screenshot saved.";
            _statusClass   = "text-success";
        }
        catch (Exception ex)
        {
            _statusMessage = $"Screenshot failed: {ex.Message}";
            _statusClass   = "text-danger";
        }
        finally
        {
            _busy = false;
            StateHasChanged();
        }
    }

    // ── GeoJSON export ────────────────────────────────────────────────────────

    private async Task ExportGeoJsonAsync()
    {
        try
        {
            await EquipService.DownloadGeoJsonAsync();
        }
        catch (Exception ex)
        {
            _statusMessage = $"Export failed: {ex.Message}";
            _statusClass   = "text-danger";
            StateHasChanged();
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        EquipService.OnChanged -= HandleChanged;
    }
}
