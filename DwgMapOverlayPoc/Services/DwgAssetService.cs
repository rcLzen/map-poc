using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using DwgMapOverlayPoc.Models;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Manages the three uploadable asset slots (PNG, GeoJSON, ZIP).
///
/// Responsibilities
/// ────────────────
/// • Validates file type and size before reading.
/// • Reads the browser file in 64 KB chunks and reports progress.
/// • Streams bytes to JS (via DotNetStreamReference) to create a Blob URL.
/// • For PNG: also generates a thumbnail via canvas in JS.
/// • For ZIP: also invokes JSZip to list the central directory entries.
/// • Persists asset metadata (name / size / date, NOT the bytes) to
///   localStorage so the sidebar shows the last-uploaded filename after a
///   page reload.  The user must re-upload to make the asset usable again.
/// • Fires OnAssetsChanged after every mutation so components can re-render.
/// </summary>
public sealed class DwgAssetService
{
    // ── Size limits ───────────────────────────────────────────────────────────

    public const long MaxPngBytes     = 200L * 1024 * 1024; // 200 MB
    public const long MaxGeoJsonBytes =  50L * 1024 * 1024; //  50 MB
    public const long MaxZipBytes     = 500L * 1024 * 1024; // 500 MB

    private const int  ChunkSize           = 65_536; // 64 KB read buffer
    private const int  MaxZipEntries       = 200;    // cap on JSZip preview
    private const int  ThumbMaxW           = 200;
    private const int  ThumbMaxH           = 150;
    private const string StorageKey        = "dwg-asset-meta-v1";

    // ── Private JS result types (deserialized from JS interop returns) ────────

    private record PngJsResult(
        [property: JsonPropertyName("blobUrl")]          string  BlobUrl,
        [property: JsonPropertyName("thumbnailDataUrl")] string? ThumbnailDataUrl);

    private record ZipJsResult(
        [property: JsonPropertyName("blobUrl")]  string      BlobUrl,
        [property: JsonPropertyName("entries")]  ZipEntry[]  Entries);

    /// <summary>Shape returned by <c>idbInterop.getAssetBlob</c>.</summary>
    private record IdbRestoreResult(
        [property: JsonPropertyName("blobUrl")]   string  BlobUrl,
        [property: JsonPropertyName("name")]      string  Name,
        [property: JsonPropertyName("size")]      long    Size,
        [property: JsonPropertyName("mimeType")]  string  MimeType,
        [property: JsonPropertyName("thumbnail")] string? Thumbnail);

    // ── State ─────────────────────────────────────────────────────────────────

    private readonly IJSRuntime _js;

    public DwgAsset? PngAsset     { get; private set; }
    public DwgAsset? GeoJsonAsset { get; private set; }
    public DwgAsset? ZipAsset     { get; private set; }

    /// <summary>Fired after every state mutation; components subscribe to re-render.</summary>
    public event Action? OnAssetsChanged;

    public DwgAssetService(IJSRuntime js) => _js = js;

    // ── Metadata bootstrap (call once from OnAfterRenderAsync) ────────────────

    /// <summary>
    /// Phase 1 — restore from IndexedDB (full binary data → immediately usable, no stub).
    /// Phase 2 — for any slot not found in IDB, fall back to localStorage stubs
    ///            so the sidebar still shows the filename and prompts re-upload.
    /// Must be called from OnAfterRenderAsync(firstRender) because it uses JS.
    /// </summary>
    public async Task LoadMetadataAsync()
    {
        // ── Phase 1: IndexedDB restore ────────────────────────────────────────
        PngAsset     = await TryRestoreFromIdbAsync("png",     AssetType.Png);
        GeoJsonAsset = await TryRestoreFromIdbAsync("geojson", AssetType.GeoJson);
        ZipAsset     = await TryRestoreFromIdbAsync("zip",     AssetType.Zip);

        bool anyRestored = PngAsset is not null || GeoJsonAsset is not null || ZipAsset is not null;
        if (anyRestored) NotifyChanged();

        // ── Phase 2: localStorage stubs for slots not in IDB ─────────────────
        if (PngAsset is null || GeoJsonAsset is null || ZipAsset is null)
        {
            await TryRestoreStubsAsync();
        }
    }

    /// <summary>
    /// Attempts to restore a single asset slot from IndexedDB.
    /// Returns a fully-Ready <see cref="DwgAsset"/> on success, or null when the
    /// IDB record does not exist or the read fails.
    /// </summary>
    private async Task<DwgAsset?> TryRestoreFromIdbAsync(string idbKey, AssetType type)
    {
        try
        {
            // Use a fixed, per-slot ID so the Blob URL can be revoked later by
            // assetInterop.revokeBlobUrl when the user removes the asset.
            var assetId = $"idb-{idbKey}";

            var data = await _js.InvokeAsync<IdbRestoreResult?>(
                "idbInterop.getAssetBlob", idbKey, assetId);

            if (data is null) return null;

            return new DwgAsset
            {
                Id               = assetId,
                Type             = type,
                Name             = data.Name,
                Size             = data.Size,
                Status           = UploadStatus.Ready,
                BlobUrl          = data.BlobUrl,
                ThumbnailDataUrl = data.Thumbnail
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DwgAssetService] IDB restore '{idbKey}' failed: {ex.Message}");
            return null;
        }
    }

    /// <summary>Fallback: read localStorage stubs for slots not yet filled by IDB.</summary>
    private async Task TryRestoreStubsAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("assetInterop.localStorageGet", StorageKey);
            if (string.IsNullOrWhiteSpace(json)) return;

            using var doc  = JsonDocument.Parse(json);
            var       root = doc.RootElement;

            if (PngAsset is null
                && root.TryGetProperty("png", out var png) && png.ValueKind != JsonValueKind.Null)
                PngAsset = BuildStub(AssetType.Png, png);

            if (GeoJsonAsset is null
                && root.TryGetProperty("geoJson", out var gj) && gj.ValueKind != JsonValueKind.Null)
                GeoJsonAsset = BuildStub(AssetType.GeoJson, gj);

            if (ZipAsset is null
                && root.TryGetProperty("zip", out var zip) && zip.ValueKind != JsonValueKind.Null)
                ZipAsset = BuildStub(AssetType.Zip, zip);

            if (PngAsset is not null || GeoJsonAsset is not null || ZipAsset is not null)
                NotifyChanged();
        }
        catch
        {
            // Ignore corrupt/missing localStorage data — fail silently.
        }
    }

    // ── Upload ────────────────────────────────────────────────────────────────

    public async Task UploadPngAsync(IBrowserFile file)
    {
        var asset = BeginUpload(AssetType.Png, file);
        PngAsset = asset;
        NotifyChanged();

        try
        {
            Validate(file, MaxPngBytes, "200 MB", ".png");

            var data = await ReadWithProgressAsync(file, MaxPngBytes, asset);

            asset.Status          = UploadStatus.Processing;
            asset.ProgressPercent = 99;
            NotifyChanged();

            await using var ms  = new MemoryStream(data);
            var result = await _js.InvokeAsync<PngJsResult>(
                "assetInterop.createPngBlobUrlWithThumbnail",
                new DotNetStreamReference(ms), asset.Id, ThumbMaxW, ThumbMaxH,
                file.Name); // forwarded to IDB persistence

            asset.BlobUrl          = result.BlobUrl;
            asset.ThumbnailDataUrl = result.ThumbnailDataUrl;
            FinishUpload(asset);
        }
        catch (Exception ex) { FailUpload(asset, ex.Message); }
        finally
        {
            NotifyChanged();
            await PersistAsync();
        }
    }

    public async Task UploadGeoJsonAsync(IBrowserFile file)
    {
        var asset = BeginUpload(AssetType.GeoJson, file);
        GeoJsonAsset = asset;
        NotifyChanged();

        try
        {
            Validate(file, MaxGeoJsonBytes, "50 MB", ".geojson", ".json");

            var data = await ReadWithProgressAsync(file, MaxGeoJsonBytes, asset);

            // Lightweight JSON validity check in C#
            try   { using var _ = JsonDocument.Parse(data); }
            catch { throw new InvalidOperationException("File does not contain valid JSON."); }

            asset.Status          = UploadStatus.Processing;
            asset.ProgressPercent = 99;
            NotifyChanged();

            await using var ms = new MemoryStream(data);
            asset.BlobUrl = await _js.InvokeAsync<string>(
                "assetInterop.createBlobUrl",
                new DotNetStreamReference(ms), "application/geo+json", asset.Id,
                file.Name); // forwarded to IDB persistence

            FinishUpload(asset);
        }
        catch (Exception ex) { FailUpload(asset, ex.Message); }
        finally
        {
            NotifyChanged();
            await PersistAsync();
        }
    }

    public async Task UploadZipAsync(IBrowserFile file)
    {
        var asset = BeginUpload(AssetType.Zip, file);
        ZipAsset = asset;
        NotifyChanged();

        try
        {
            Validate(file, MaxZipBytes, "500 MB", ".zip");

            var data = await ReadWithProgressAsync(file, MaxZipBytes, asset);

            asset.Status          = UploadStatus.Processing;
            asset.ProgressPercent = 99;
            NotifyChanged();

            // Single JS round-trip: create Blob URL + run JSZip preview + persist to IDB
            await using var ms  = new MemoryStream(data);
            var result = await _js.InvokeAsync<ZipJsResult>(
                "assetInterop.createZipBlobUrlWithPreview",
                new DotNetStreamReference(ms), asset.Id, MaxZipEntries,
                file.Name); // forwarded to IDB persistence

            asset.BlobUrl     = result.BlobUrl;
            asset.ZipContents = result.Entries;
            FinishUpload(asset);
        }
        catch (Exception ex) { FailUpload(asset, ex.Message); }
        finally
        {
            NotifyChanged();
            await PersistAsync();
        }
    }

    // ── Remove ────────────────────────────────────────────────────────────────

    public async Task RemovePngAsync()
    {
        await RevokeAsync(PngAsset);
        PngAsset = null;
        NotifyChanged();
        await PersistAsync();
        await DeleteFromIdbAsync("png");
    }

    public async Task RemoveGeoJsonAsync()
    {
        await RevokeAsync(GeoJsonAsset);
        GeoJsonAsset = null;
        NotifyChanged();
        await PersistAsync();
        await DeleteFromIdbAsync("geojson");
    }

    public async Task RemoveZipAsync()
    {
        await RevokeAsync(ZipAsset);
        ZipAsset = null;
        NotifyChanged();
        await PersistAsync();
        await DeleteFromIdbAsync("zip");
    }

    private async Task DeleteFromIdbAsync(string idbKey)
    {
        try   { await _js.InvokeVoidAsync("idbInterop.deleteAsset", idbKey); }
        catch { /* non-critical */ }
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static DwgAsset BeginUpload(AssetType type, IBrowserFile file) => new()
    {
        Type   = type,
        Name   = file.Name,
        Size   = file.Size,
        Status = UploadStatus.Reading
    };

    private static void FinishUpload(DwgAsset a)
    {
        a.Status          = UploadStatus.Ready;
        a.ProgressPercent = 100;
        a.ErrorMessage    = null;
    }

    private static void FailUpload(DwgAsset a, string msg)
    {
        a.Status          = UploadStatus.Error;
        a.ErrorMessage    = msg;
        a.ProgressPercent = 0;
    }

    private static void Validate(IBrowserFile file, long maxBytes, string maxLabel, params string[] exts)
    {
        var ext = Path.GetExtension(file.Name).ToLowerInvariant();
        if (!exts.Contains(ext))
            throw new InvalidOperationException(
                $"Unsupported file type '{ext}'. Expected: {string.Join(", ", exts)}.");

        if (file.Size > maxBytes)
            throw new InvalidOperationException(
                $"File is too large ({FormatBytes(file.Size)}). Maximum allowed: {maxLabel}.");
    }

    /// <summary>
    /// Reads the browser file in 64 KB chunks, reporting progress via the asset.
    /// Progress saturates at 99 % — the caller sets 100 % after JS processing.
    /// NotifyChanged is throttled to fire only when the integer percentage changes
    /// (≤ 99 events per upload rather than one per 64 KB chunk).
    /// </summary>
    private async Task<byte[]> ReadWithProgressAsync(
        IBrowserFile file, long maxBytes, DwgAsset asset)
    {
        await using var stream = file.OpenReadStream(maxBytes);
        var   buffer    = new byte[ChunkSize];
        using var ms    = new MemoryStream((int)Math.Min(file.Size, int.MaxValue));
        long  totalRead = 0;
        int   read;

        while ((read = await stream.ReadAsync(buffer)) > 0)
        {
            await ms.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;
            int newPct = (int)Math.Min(totalRead * 99L / Math.Max(file.Size, 1), 99);
            if (newPct != asset.ProgressPercent)
            {
                asset.ProgressPercent = newPct;
                NotifyChanged();
            }
        }

        return ms.ToArray();
    }

    private async Task RevokeAsync(DwgAsset? asset)
    {
        if (asset?.Id is string id)
            await _js.InvokeVoidAsync("assetInterop.revokeBlobUrl", id);
    }

    private void NotifyChanged() => OnAssetsChanged?.Invoke();

    // ── localStorage ─────────────────────────────────────────────────────────

    private async Task PersistAsync()
    {
        var payload = new
        {
            png     = Snapshot(PngAsset),
            geoJson = Snapshot(GeoJsonAsset),
            zip     = Snapshot(ZipAsset)
        };
        await _js.InvokeVoidAsync(
            "assetInterop.localStorageSet",
            StorageKey,
            JsonSerializer.Serialize(payload));
    }

    private static object? Snapshot(DwgAsset? a) =>
        a is null ? null : new { name = a.Name, size = a.Size, uploadedAt = a.UploadedAt };

    private static DwgAsset BuildStub(AssetType type, JsonElement el) => new()
    {
        Type       = type,
        Name       = el.GetProperty("name").GetString() ?? "(unknown)",
        Size       = el.GetProperty("size").GetInt64(),
        UploadedAt = el.TryGetProperty("uploadedAt", out var d)
                         ? d.GetDateTime() : DateTime.MinValue,
        IsStub     = true,
        Status     = UploadStatus.None
    };

    private static string FormatBytes(long bytes) => bytes switch
    {
        < 1_024         => $"{bytes} B",
        < 1_048_576     => $"{bytes / 1_024.0:F1} KB",
        < 1_073_741_824 => $"{bytes / 1_048_576.0:F1} MB",
        _               => $"{bytes / 1_073_741_824.0:F2} GB"
    };
}
