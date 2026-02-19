using System.Text.Json.Serialization;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Services;

public enum XyzStatus { Idle, Loading, Ready, Error }

/// <summary>
/// Loads tiles from a gdal2tiles --xyz ZIP into the browser's memory and
/// exposes them as a custom Leaflet GridLayer via JS interop.
///
/// Expected ZIP layout: {z}/{x}/{y}.png — standard XYZ / slippy-map tiles.
/// Generate with: gdal2tiles.py --xyz --zoom=&lt;min&gt;-&lt;max&gt; input.tif tiles/
/// </summary>
public sealed class XyzTileService(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    public XyzStatus Status         { get; private set; } = XyzStatus.Idle;
    public int        TileCount     { get; private set; }
    public int[]      ZoomLevels    { get; private set; } = [];
    public bool       IsLayerVisible { get; private set; }
    public string?    LastError     { get; private set; }

    public event Action? OnChanged;

    public async Task LoadAsync(string zipBlobUrl)
    {
        Status     = XyzStatus.Loading;
        TileCount  = 0;
        ZoomLevels = [];
        LastError  = null;
        OnChanged?.Invoke();

        try
        {
            var result = await _js.InvokeAsync<XyzLoadResult>(
                "xyzInterop.loadTilesFromZip", zipBlobUrl);

            TileCount  = result.TileCount;
            ZoomLevels = result.Zooms ?? [];
            Status     = XyzStatus.Ready;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Status    = XyzStatus.Error;
        }

        OnChanged?.Invoke();
    }

    public async Task ActivateAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("xyzInterop.addXyzLayer");
            IsLayerVisible = true;
        }
        catch (JSException ex) { LastError = ex.Message; }

        OnChanged?.Invoke();
    }

    public async Task DeactivateAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("xyzInterop.removeXyzLayer");
            IsLayerVisible = false;
        }
        catch (JSException ex) { LastError = ex.Message; }

        OnChanged?.Invoke();
    }

    public async Task SetOpacityAsync(double opacity)
    {
        try
        {
            await _js.InvokeVoidAsync("xyzInterop.setXyzLayerOpacity", opacity);
        }
        catch (JSException) { }
    }

    // ── JSON shape returned by xyzInterop.loadTilesFromZip ───────────────────

    private sealed record XyzLoadResult(
        [property: JsonPropertyName("tileCount")] int    TileCount,
        [property: JsonPropertyName("zooms")]     int[]? Zooms);
}
