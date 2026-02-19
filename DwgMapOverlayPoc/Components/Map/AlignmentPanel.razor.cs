using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Components.Map;

/// <summary>
/// Code-behind for AlignmentPanel.razor.
/// Holds all local UI state and calls QuickAlignService for JS interop + affine math.
/// </summary>
public partial class AlignmentPanel
{
    // ── State ─────────────────────────────────────────────────────────────────

    private DotNetObjectReference<AlignmentPanel>? _selfRef;

    /** Image dimensions (entered by user or auto-detected). */
    private double _imageW;
    private double _imageH;

    /** Pixel coordinates per control point (can be 0,0 for image top-left). */
    private readonly double[] _pixelX = new double[3];
    private readonly double[] _pixelY = new double[3];

    /** World coordinates filled after each map click. */
    private readonly double[] _worldLat = new double[3];
    private readonly double[] _worldLng = new double[3];

    /** Whether the map has been clicked for each point. */
    private readonly bool[] _hasMapCoords = new bool[3];

    /** Which step (0–2) is currently waiting for a map click; -1 = not picking. */
    private int _pickingStep = -1;

    private bool    _hasOverlay;
    private int     _opacityPct  = 75;
    private string? _errorMessage;

    // ── Predicates ───────────────────────────────────────────────────────────

    private bool IsPngReady() =>
        AssetService.PngAsset?.Status == UploadStatus.Ready &&
        !AssetService.PngAsset.IsStub &&
        AssetService.PngAsset.BlobUrl is not null;

    private bool IsPickingStep(int step) => _pickingStep == step;

    private bool GetPointHasMap(int step) => _hasMapCoords[step];

    private bool CanApply() =>
        _imageW > 0 && _imageH > 0 &&
        _hasMapCoords[0] && _hasMapCoords[1] && _hasMapCoords[2];

    // ── Formatting ───────────────────────────────────────────────────────────

    private string FormatLatLng(int step) =>
        $"{_worldLat[step]:F5}, {_worldLng[step]:F5}";

    // ── Map click capture ─────────────────────────────────────────────────────

    private async Task TogglePickAsync(int step)
    {
        if (_pickingStep == step)
        {
            // Cancel current pick
            _pickingStep = -1;
            await JS.InvokeVoidAsync("alignInterop.stopMapClickCapture");
        }
        else
        {
            // Cancel any existing pick first
            if (_pickingStep >= 0)
                await JS.InvokeVoidAsync("alignInterop.stopMapClickCapture");

            _pickingStep = step;
            await JS.InvokeVoidAsync("alignInterop.startMapClickCapture", _selfRef, "OnMapClicked");
        }
        StateHasChanged();
    }

    private void ClearMapCoord(int step)
    {
        _hasMapCoords[step] = false;
        _worldLat[step]     = 0;
        _worldLng[step]     = 0;
    }

    // ── Apply / Remove ────────────────────────────────────────────────────────

    private async Task ApplyAsync()
    {
        _errorMessage = null;

        if (AssetService.PngAsset?.BlobUrl is not string blobUrl) return;

        var pts = new ControlPoint[3];
        for (int i = 0; i < 3; i++)
        {
            pts[i] = new ControlPoint
            {
                ImageX       = _pixelX[i],
                ImageY       = _pixelY[i],
                WorldLat     = _worldLat[i],
                WorldLng     = _worldLng[i],
                HasMapCoords = _hasMapCoords[i]
            };
        }

        bool ok = await AlignService.ApplyAsync(pts, _imageW, _imageH, blobUrl, _opacityPct / 100.0);
        if (ok)
        {
            _hasOverlay = true;
        }
        else
        {
            _errorMessage = AlignService.LastError;
        }
        StateHasChanged();
    }

    private async Task RemoveAsync()
    {
        await AlignService.RemoveAsync();
        _hasOverlay = false;
        StateHasChanged();
    }

    // ── Opacity ───────────────────────────────────────────────────────────────

    private async Task OnOpacityInput(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var v))
        {
            _opacityPct = v;
            if (_hasOverlay)
                await AlignService.SetOpacityAsync(v / 100.0);
        }
    }

    // ── Auto-detect image size ────────────────────────────────────────────────

    /// <summary>
    /// Attempt to read the natural pixel dimensions of the PNG thumbnail so the
    /// user doesn't have to type W and H manually.
    /// </summary>
    private async Task TryAutoDetectImageSizeAsync()
    {
        try
        {
            if (AssetService.PngAsset?.BlobUrl is string blobUrl)
            {
                var dims = await JS.InvokeAsync<double[]?>(
                    "assetInterop.getImageNaturalSize", blobUrl);
                if (dims is { Length: 2 } && dims[0] > 0 && dims[1] > 0)
                {
                    _imageW = dims[0];
                    _imageH = dims[1];
                    StateHasChanged();
                }
            }
        }
        catch { /* non-critical — user can type the values manually */ }
    }
}
