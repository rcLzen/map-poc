using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Services;

public enum TmsStatus { Idle, Loading, Ready, Error }

/// <summary>
/// Loads tiles from a gdal2tiles-style ZIP into the browser's memory and
/// exposes them as a custom Leaflet GridLayer via JS interop.
/// </summary>
public sealed class TmsTileService(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    public TmsStatus Status        { get; private set; } = TmsStatus.Idle;
    public int       TileCount     { get; private set; }
    public int[]     ZoomLevels    { get; private set; } = [];
    public bool      IsLayerVisible { get; private set; }
    public string?   LastError     { get; private set; }

    public event Action? OnChanged;

    public async Task LoadAsync(string zipBlobUrl)
    {
        Status    = TmsStatus.Loading;
        TileCount = 0;
        ZoomLevels = [];
        LastError  = null;
        OnChanged?.Invoke();

        try
        {
            var result = await _js.InvokeAsync<TmsLoadResult>(
                "tmsInterop.loadTilesFromZip", zipBlobUrl);

            TileCount  = result.TileCount;
            ZoomLevels = result.Zooms ?? [];
            Status     = TmsStatus.Ready;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Status    = TmsStatus.Error;
        }

        OnChanged?.Invoke();
    }

    public async Task ActivateAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("tmsInterop.addTmsLayer");
            IsLayerVisible = true;
        }
        catch (JSException ex) { LastError = ex.Message; }

        OnChanged?.Invoke();
    }

    public async Task DeactivateAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("tmsInterop.removeTmsLayer");
            IsLayerVisible = false;
        }
        catch (JSException ex) { LastError = ex.Message; }

        OnChanged?.Invoke();
    }

    public async Task SetOpacityAsync(double opacity)
    {
        try
        {
            await _js.InvokeVoidAsync("tmsInterop.setTmsLayerOpacity", opacity);
        }
        catch (JSException) { }
    }

    // ── JSON shape returned by tmsInterop.loadTilesFromZip ───────────────────

    private sealed record TmsLoadResult(
        [property: JsonPropertyName("tileCount")] int    TileCount,
        [property: JsonPropertyName("zooms")]     int[]? Zooms);
}
