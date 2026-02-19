using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.AspNetCore.Components;

namespace DwgMapOverlayPoc.Components.Map;

/// <summary>Code-behind for TmsTilePanel.razor.</summary>
public partial class TmsTilePanel
{
    private int _opacityPct = 75;

    // ── Predicates ────────────────────────────────────────────────────────────

    private bool IsZipReady() =>
        AssetService.ZipAsset?.Status == UploadStatus.Ready &&
        !AssetService.ZipAsset.IsStub &&
        AssetService.ZipAsset.BlobUrl is not null;

    private bool IsIdle()    => TmsService.Status == TmsStatus.Idle;
    private bool IsLoading() => TmsService.Status == TmsStatus.Loading;
    private bool IsError()   => TmsService.Status == TmsStatus.Error;

    // ── Formatting ────────────────────────────────────────────────────────────

    private string FormatZooms()
    {
        if (TmsService.ZoomLevels.Length == 0) return string.Empty;
        if (TmsService.ZoomLevels.Length == 1) return TmsService.ZoomLevels[0].ToString();
        int min = TmsService.ZoomLevels[0];
        int max = TmsService.ZoomLevels[^1];
        return min == max ? min.ToString() : $"{min}–{max}";
    }

    // ── Actions ───────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        if (AssetService.ZipAsset?.BlobUrl is string blobUrl)
            await TmsService.LoadAsync(blobUrl);
    }

    private async Task ShowAsync() => await TmsService.ActivateAsync();

    private async Task HideAsync() => await TmsService.DeactivateAsync();

    private async Task OnOpacityInput(ChangeEventArgs e)
    {
        if (int.TryParse(e.Value?.ToString(), out var v))
        {
            _opacityPct = v;
            await TmsService.SetOpacityAsync(v / 100.0);
        }
    }
}
