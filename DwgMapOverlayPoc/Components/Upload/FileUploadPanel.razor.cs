using System.Net;
using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.AspNetCore.Components.Forms;

namespace DwgMapOverlayPoc.Components.Upload;

/// <summary>
/// Code-behind for FileUploadPanel.razor.
/// All C# helpers are placed here to keep the Razor template clean and
/// avoid Razor brace-counting issues with switch expressions and lambdas.
/// </summary>
public partial class FileUploadPanel
{
    // ── InputFile key management ──────────────────────────────────────────────
    //
    // Each Guid is used as the @key for its card's <InputFile>.  While the Guid
    // is stable, Blazor's diffing algorithm reuses the same underlying <input>
    // DOM element across every re-render, so the browser-side _blazorFilesById
    // entry (and the IBrowserFile stream) is never invalidated mid-upload.
    //
    // After an upload finishes (success or error) or an asset is removed, the
    // Guid is rotated so the browser sees a brand-new <input> element — clearing
    // any previously selected file and resetting the card to idle.

    private Guid _pngKey     = Guid.NewGuid();
    private Guid _geoJsonKey = Guid.NewGuid();
    private Guid _zipKey     = Guid.NewGuid();

    // ── Upload handlers ───────────────────────────────────────────────────────
    //
    // Handlers are proper async Tasks (not fire-and-forget lambdas) so the
    // IBrowserFile stays valid for the entire duration of the service call.
    // The Guid is rotated AFTER the awaited call returns — i.e., after the
    // stream has been fully read and closed — so the key is always stable while
    // data is flowing.

    private async Task HandlePngChangeAsync(InputFileChangeEventArgs e)
    {
        await AssetService.UploadPngAsync(e.File);
        _pngKey = Guid.NewGuid();
    }

    private async Task HandleGeoJsonChangeAsync(InputFileChangeEventArgs e)
    {
        await AssetService.UploadGeoJsonAsync(e.File);
        _geoJsonKey = Guid.NewGuid();
    }

    private async Task HandleZipChangeAsync(InputFileChangeEventArgs e)
    {
        await AssetService.UploadZipAsync(e.File);
        _zipKey = Guid.NewGuid();
    }

    // ── Remove handlers ───────────────────────────────────────────────────────
    //
    // Rotating the key on remove ensures the <input> is replaced with a fresh
    // element, so a subsequent upload doesn't carry stale browser file state.

    private async Task RemovePngAsync()
    {
        await AssetService.RemovePngAsync();
        _pngKey = Guid.NewGuid();
    }

    private async Task RemoveGeoJsonAsync()
    {
        await AssetService.RemoveGeoJsonAsync();
        _geoJsonKey = Guid.NewGuid();
    }

    private async Task RemoveZipAsync()
    {
        await AssetService.RemoveZipAsync();
        _zipKey = Guid.NewGuid();
    }

    // ── CSS / label derivation ────────────────────────────────────────────────

    private static string CardCss(DwgAsset? a) => a?.Status switch
    {
        UploadStatus.Reading or UploadStatus.Processing => "upload-card--uploading",
        UploadStatus.Ready when a.IsStub                => "upload-card--stub",
        UploadStatus.Ready                              => "upload-card--ready",
        UploadStatus.Error                              => "upload-card--error",
        _                                               => string.Empty
    };

    private static string StatusLabel(DwgAsset a) =>
        a.Status == UploadStatus.Reading ? "Reading" : "Processing";

    private static string BadgeCss(DwgAsset? a) => a?.Status switch
    {
        UploadStatus.Ready when !a.IsStub => "upload-badge--ok",
        UploadStatus.Ready                => "upload-badge--stub",
        UploadStatus.Error                => "upload-badge--err",
        _                                 => string.Empty
    };

    private static string BadgeText(DwgAsset? a) => a?.Status switch
    {
        UploadStatus.Ready when !a.IsStub => "✓",
        UploadStatus.Ready                => "⚠",
        UploadStatus.Error                => "!",
        _                                 => string.Empty
    };

    private static string BadgeTitle(DwgAsset? a) => a?.Status switch
    {
        UploadStatus.Ready when !a.IsStub => "Ready",
        UploadStatus.Ready                => "Re-upload needed",
        UploadStatus.Error                => WebUtility.HtmlEncode(a.ErrorMessage ?? string.Empty),
        _                                 => string.Empty
    };

    private static bool ShowBadge(DwgAsset? a) =>
        a?.Status is UploadStatus.Ready or UploadStatus.Error;

    // ── ZIP tree helpers ──────────────────────────────────────────────────────

    private IEnumerable<ZipEntry> GetZipShown() =>
        AssetService.ZipAsset?.ZipContents?.Take(50)
        ?? Enumerable.Empty<ZipEntry>();

    private static string ZipEntryCss(ZipEntry e) =>
        e.IsDirectory
            ? "upload-zip-entry upload-zip-entry--dir"
            : "upload-zip-entry";

    private static string TrimPath(string path)
    {
        var trimmed = path.TrimEnd('/');
        var parts   = trimmed.Split('/');
        return parts.Length <= 2
            ? trimmed
            : ".../" + string.Join("/", parts[^2..]);
    }

    // ── Static formatting ─────────────────────────────────────────────────────

    private static string FormatMaxMB(long bytes) =>
        $"{bytes / (1024L * 1024):F0} MB";

    private static string FormatBytes(long bytes)
    {
        if (bytes < 1_024)         return $"{bytes} B";
        if (bytes < 1_048_576)     return $"{bytes / 1_024.0:F0} KB";
        return $"{bytes / 1_048_576.0:F1} MB";
    }
}
