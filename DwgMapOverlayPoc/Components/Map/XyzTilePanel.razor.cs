using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.AspNetCore.Components;

namespace DwgMapOverlayPoc.Components.Map;

/// <summary>Code-behind for XyzTilePanel.razor.</summary>
public partial class XyzTilePanel
{
    private int _opacityPct = 75;

    // ── Predicates ────────────────────────────────────────────────────────────

    private bool IsZipReady() =>
        AssetService.ZipAsset?.Status == UploadStatus.Ready &&
        !AssetService.ZipAsset.IsStub &&
        AssetService.ZipAsset.BlobUrl is not null;

    private bool IsIdle()    => XyzService.Status == XyzStatus.Idle;
    private bool IsLoading() => XyzService.Status == XyzStatus.Loading;
    private bool IsError()   => XyzService.Status == XyzStatus.Error;

    // ── Formatting ────────────────────────────────────────────────────────────

    private string FormatZooms()
    {
        if (XyzService.ZoomLevels.Length == 0) return string.Empty;
        if (XyzService.ZoomLevels.Length == 1) return XyzService.ZoomLevels[0].ToString();
        int min = XyzService.ZoomLevels[0];
        int max = XyzService.ZoomLevels[^1];
        return min == max ? min.ToString() : $"{min}–{max}";
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (AssetService.ZipAsset?.BlobUrl is string blobUrl)
            await XyzService.LoadAsync(blobUrl);
    }

    private async Task ShowAsync() => await XyzService.ActivateAsync();

    private async Task HideAsync() => await XyzService.DeactivateAsync();

    private async Task OnOpacityInput(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var v))
        {
            _opacityPct = v;
            await XyzService.SetOpacityAsync(v / 100.0);
        }
    }
}
