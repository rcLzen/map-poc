using DwgMapOverlayPoc.Models;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Manages 3-point georeferencing: given 3 pixel↔world pairs, solves an affine
/// transform that maps arbitrary pixel coords to world LatLng, then derives the
/// image's topLeft/topRight/bottomLeft corners for L.imageOverlay.rotated.
/// </summary>
public sealed class QuickAlignService(IJSRuntime js)
{
    private readonly IJSRuntime _js = js;

    public bool    HasOverlay { get; private set; }
    public string? LastError  { get; private set; }

    public event Action? OnChanged;

    // ── Affine solver ────────────────────────────────────────────────────────

    /// <summary>
    /// Compute world LatLng corners and push the rotated overlay to the map.
    /// Returns true on success; on failure sets <see cref="LastError"/> and returns false.
    /// </summary>
    public async Task<bool> ApplyAsync(
        ControlPoint[] pts, double imageW, double imageH,
        string blobUrl, double opacity)
    {
        LastError = null;

        double px0 = pts[0].ImageX, py0 = pts[0].ImageY;
        double px1 = pts[1].ImageX, py1 = pts[1].ImageY;
        double px2 = pts[2].ImageX, py2 = pts[2].ImageY;

        // Determinant of the 3×3 coefficient matrix A = [[px,py,1] per row]
        double detA = px0 * (py1 - py2)
                    - py0 * (px1 - px2)
                    + (px1 * py2 - px2 * py1);

        if (Math.Abs(detA) < 1e-9)
        {
            LastError = "Control points are collinear — choose 3 non-collinear pixel locations.";
            OnChanged?.Invoke();
            return false;
        }

        // Solve affine:  lat = aL*px + bL*py + cL  (Cramer's rule)
        double lat0 = pts[0].WorldLat, lat1 = pts[1].WorldLat, lat2 = pts[2].WorldLat;
        double aL = (lat0 * (py1 - py2) - py0 * (lat1 - lat2) + (lat1 * py2 - lat2 * py1)) / detA;
        double bL = (px0 * (lat1 - lat2) - lat0 * (px1 - px2) + (px1 * lat2 - px2 * lat1)) / detA;
        double cL = (px0 * (py1 * lat2 - py2 * lat1) - py0 * (px1 * lat2 - px2 * lat1) + lat0 * (px1 * py2 - px2 * py1)) / detA;

        // Solve affine:  lng = aG*px + bG*py + cG
        double lng0 = pts[0].WorldLng, lng1 = pts[1].WorldLng, lng2 = pts[2].WorldLng;
        double aG = (lng0 * (py1 - py2) - py0 * (lng1 - lng2) + (lng1 * py2 - lng2 * py1)) / detA;
        double bG = (px0 * (lng1 - lng2) - lng0 * (px1 - px2) + (px1 * lng2 - px2 * lng1)) / detA;
        double cG = (px0 * (py1 * lng2 - py2 * lng1) - py0 * (px1 * lng2 - px2 * lng1) + lng0 * (px1 * py2 - px2 * py1)) / detA;

        // Map corners: image (0,0) = topLeft, (W,0) = topRight, (0,H) = bottomLeft
        double tlLat = cL,              tlLng = cG;
        double trLat = aL * imageW + cL, trLng = aG * imageW + cG;
        double blLat = bL * imageH + cL, blLng = bG * imageH + cG;

        try
        {
            await _js.InvokeVoidAsync(
                "alignInterop.setRotatedOverlay",
                blobUrl,
                new[] { tlLat, tlLng },
                new[] { trLat, trLng },
                new[] { blLat, blLng },
                opacity);

            HasOverlay = true;
            OnChanged?.Invoke();
            return true;
        }
        catch (JSException ex)
        {
            LastError = $"JS error: {ex.Message}";
            OnChanged?.Invoke();
            return false;
        }
    }

    public async Task RemoveAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("alignInterop.clearRotatedOverlay");
        }
        catch (JSException) { /* map may not be ready */ }

        HasOverlay = false;
        OnChanged?.Invoke();
    }

    public async Task SetOpacityAsync(double opacity)
    {
        try
        {
            await _js.InvokeVoidAsync("alignInterop.setRotatedOverlayOpacity", opacity);
        }
        catch (JSException) { }
    }
}
