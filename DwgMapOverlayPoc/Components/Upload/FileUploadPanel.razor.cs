using System.Net;
using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;

namespace DwgMapOverlayPoc.Components.Upload;

/// <summary>
/// Code-behind for FileUploadPanel.razor.
/// All C# helpers are placed here to keep the Razor template clean and
/// avoid Razor brace-counting issues with switch expressions and lambdas.
/// </summary>
public partial class FileUploadPanel
{
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
