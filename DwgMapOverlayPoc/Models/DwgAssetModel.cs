using System.Text.Json.Serialization;

namespace DwgMapOverlayPoc.Models;

/// <summary>The three asset types the user can upload.</summary>
public enum AssetType { Png, GeoJson, Zip }

/// <summary>Lifecycle state of a single upload slot.</summary>
public enum UploadStatus
{
    /// <summary>Nothing uploaded (or stub restored from localStorage — check IsStub).</summary>
    None,
    /// <summary>Bytes are being read from the browser file into memory.</summary>
    Reading,
    /// <summary>Bytes are in memory; JS processing (Blob URL / thumbnail / ZIP parse) is running.</summary>
    Processing,
    /// <summary>Asset is fully available; BlobUrl is set.</summary>
    Ready,
    /// <summary>Validation or I/O error; see ErrorMessage.</summary>
    Error
}

/// <summary>
/// A single entry found inside an XYZ tile ZIP, returned by JSZip preview.
/// Property names use JsonPropertyName so Blazor JS interop camelCase → PascalCase
/// deserialisation works without relying on case-insensitive fallback.
/// </summary>
public record ZipEntry(
    [property: JsonPropertyName("name")]        string Name,
    [property: JsonPropertyName("size")]        long   Size,
    [property: JsonPropertyName("isDirectory")] bool   IsDirectory);

/// <summary>
/// Runtime state for one uploaded asset slot (PNG, GeoJSON, or ZIP).
/// Instances are owned by <see cref="DwgMapOverlayPoc.Services.DwgAssetService"/>.
/// </summary>
public sealed class DwgAsset
{
    // ── Identity ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Session-unique identifier used as the Blob URL registry key.
    /// Uses <c>init</c> so IDB-restored assets can supply a stable, fixed ID
    /// (e.g. "idb-png") while normal uploads use an auto-generated GUID.
    /// </summary>
    public string   Id         { get; init; } = Guid.NewGuid().ToString("N");
    public AssetType Type      { get; init; }
    public string   Name       { get; init; } = string.Empty;
    public long     Size       { get; init; }
    public DateTime UploadedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// True when the asset was restored from localStorage metadata on page load.
    /// The file data is NOT in memory — BlobUrl is null and the user must re-upload
    /// before the asset can be used by the map.
    /// </summary>
    public bool IsStub { get; init; }

    // ── Runtime data (cleared on page reload) ────────────────────────────────

    /// <summary>Browser Blob URL created by asset-interop.js; usable by Leaflet.</summary>
    public string? BlobUrl          { get; set; }

    /// <summary>Base64 JPEG data-URL thumbnail; PNG uploads only.</summary>
    public string? ThumbnailDataUrl { get; set; }

    /// <summary>Up to 200 entries from the ZIP central directory; ZIP uploads only.</summary>
    public IReadOnlyList<ZipEntry>? ZipContents { get; set; }

    // ── Upload progress ───────────────────────────────────────────────────────

    public UploadStatus Status          { get; set; } = UploadStatus.None;
    public string?      ErrorMessage    { get; set; }
    public int          ProgressPercent { get; set; }

    // ── Helpers ───────────────────────────────────────────────────────────────

    public string FormattedSize => Size switch
    {
        < 1_024         => $"{Size} B",
        < 1_048_576     => $"{Size / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{Size / 1_048_576.0:F1} MB",
        _               => $"{Size / 1_073_741_824.0:F2} GB"
    };
}
