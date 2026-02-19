using System.Text.Json;
using System.Text.Json.Serialization;
using DwgMapOverlayPoc.Models;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Manages placed equipment markers: CRUD, snapping, GeoJSON export, localStorage persistence.
/// Exposes [JSInvokable] callbacks for map click and marker drag events.
/// Call <see cref="InitAsync"/> once (from EquipmentPanel.OnAfterRenderAsync) to wire up JS.
/// </summary>
public sealed class EquipmentService(IJSRuntime js, SnappingService snapping) : IAsyncDisposable
{
    private readonly IJSRuntime      _js       = js;
    private readonly SnappingService _snapping = snapping;
    private readonly List<EquipmentMarker> _markers = [];

    private DotNetObjectReference<EquipmentService>? _selfRef;
    private bool _initialized;

    private const string StorageKey = "equipment-v1";

    public IReadOnlyList<EquipmentMarker> Markers       => _markers.AsReadOnly();
    public bool          PlacementMode  { get; private set; }
    public EquipmentType PlacementType  { get; set; } = EquipmentType.Valve;

    public event Action? OnChanged;

    // ── Initialisation ───────────────────────────────────────────────────────

    /// <summary>
    /// Wire up the JS dotNetRef and restore persisted markers.
    /// Guard: safe to call multiple times.
    /// </summary>
    public async Task InitAsync()
    {
        if (_initialized) return;
        _initialized = true;

        _selfRef = DotNetObjectReference.Create(this);
        try
        {
            await _js.InvokeVoidAsync("equipmentInterop.init", _selfRef);
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[EquipmentService] init failed: {ex.Message}");
        }

        await LoadFromStorageAsync();
        OnChanged?.Invoke();
    }

    // ── Placement mode ───────────────────────────────────────────────────────

    public async Task SetPlacementModeAsync(bool active)
    {
        PlacementMode = active;
        try
        {
            if (active)
                await _js.InvokeVoidAsync("equipmentInterop.startPlacementMode");
            else
                await _js.InvokeVoidAsync("equipmentInterop.stopPlacementMode");
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[EquipmentService] placement mode failed: {ex.Message}");
        }
        OnChanged?.Invoke();
    }

    // ── JS callbacks ─────────────────────────────────────────────────────────

    [JSInvokable]
    public async Task OnEquipmentClicked(double lat, double lng)
    {
        PlacementMode = false;
        await AddMarkerAsync(lat, lng);
    }

    [JSInvokable]
    public async Task OnMarkerDragged(string id, double lat, double lng)
    {
        var marker = _markers.FirstOrDefault(m => m.Id == id);
        if (marker is null) return;

        marker.Lat       = lat;
        marker.Lng       = lng;
        marker.IsSnapped = false;

        await PersistAsync();
        OnChanged?.Invoke();
    }

    // ── Marker CRUD ──────────────────────────────────────────────────────────

    public async Task AddMarkerAsync(double lat, double lng)
    {
        // Read current map zoom for pixel→degree tolerance conversion
        int zoom = 15;
        try { zoom = await _js.InvokeAsync<int>("leafletInterop.getZoom"); } catch { }

        var (snapLat, snapLng, isSnapped) = await _snapping.SnapAsync(lat, lng, zoom);

        var marker = new EquipmentMarker
        {
            Type      = PlacementType,
            Label     = $"{PlacementType} {_markers.Count + 1}",
            Lat       = snapLat,
            Lng       = snapLng,
            IsSnapped = isSnapped
        };
        _markers.Add(marker);

        try
        {
            await _js.InvokeVoidAsync(
                "equipmentInterop.addMarker",
                marker.Id, marker.Type.ToString(), marker.Label,
                marker.Lat, marker.Lng, marker.IsSnapped);
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[EquipmentService] addMarker failed: {ex.Message}");
        }

        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task RemoveMarkerAsync(string id)
    {
        var marker = _markers.FirstOrDefault(m => m.Id == id);
        if (marker is null) return;

        _markers.Remove(marker);
        try
        {
            await _js.InvokeVoidAsync("equipmentInterop.removeMarker", id);
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[EquipmentService] removeMarker failed: {ex.Message}");
        }

        await PersistAsync();
        OnChanged?.Invoke();
    }

    public async Task ClearAllAsync()
    {
        _markers.Clear();
        try { await _js.InvokeVoidAsync("equipmentInterop.clearAllMarkers"); }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[EquipmentService] clearAll failed: {ex.Message}");
        }

        await PersistAsync();
        OnChanged?.Invoke();
    }

    // ── Export ───────────────────────────────────────────────────────────────

    public string BuildGeoJson()
    {
        var features = _markers.Select(m => new
        {
            type     = "Feature",
            geometry = new { type = "Point", coordinates = new[] { m.Lng, m.Lat } },
            properties = new
            {
                id        = m.Id,
                type      = m.Type.ToString(),
                label     = m.Label,
                isSnapped = m.IsSnapped
            }
        });
        var fc = new { type = "FeatureCollection", features };
        return JsonSerializer.Serialize(fc);
    }

    public async Task DownloadGeoJsonAsync()
    {
        try
        {
            await _js.InvokeVoidAsync("equipmentInterop.downloadGeoJson", BuildGeoJson());
        }
        catch (JSException ex)
        {
            Console.Error.WriteLine($"[EquipmentService] downloadGeoJson failed: {ex.Message}");
        }
    }

    // ── Persistence ──────────────────────────────────────────────────────────

    private async Task PersistAsync()
    {
        try
        {
            var data = _markers.Select(m => new PersistedMarker(
                m.Id, m.Type.ToString(), m.Label, m.Lat, m.Lng, m.IsSnapped));
            var json = JsonSerializer.Serialize(data);
            await _js.InvokeVoidAsync("assetInterop.localStorageSet", StorageKey, json);
        }
        catch { }
    }

    private async Task LoadFromStorageAsync()
    {
        try
        {
            var json = await _js.InvokeAsync<string?>("assetInterop.localStorageGet", StorageKey);
            if (string.IsNullOrEmpty(json)) return;

            var items = JsonSerializer.Deserialize<PersistedMarker[]>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (items is null) return;

            foreach (var item in items)
            {
                var type = Enum.TryParse<EquipmentType>(item.Type, out var t) ? t : EquipmentType.Custom;
                _markers.Add(new EquipmentMarker
                {
                    Id        = item.Id,
                    Type      = type,
                    Label     = item.Label,
                    Lat       = item.Lat,
                    Lng       = item.Lng,
                    IsSnapped = item.IsSnapped
                });
                // Restored markers show in sidebar list; JS markers are recreated via addMarker call
                try
                {
                    await _js.InvokeVoidAsync(
                        "equipmentInterop.addMarker",
                        item.Id, type.ToString(), item.Label, item.Lat, item.Lng, item.IsSnapped);
                }
                catch { /* map may not be ready yet on first load */ }
            }
        }
        catch { }
    }

    private sealed record PersistedMarker(
        [property: JsonPropertyName("id")]        string Id,
        [property: JsonPropertyName("type")]      string Type,
        [property: JsonPropertyName("label")]     string Label,
        [property: JsonPropertyName("lat")]       double Lat,
        [property: JsonPropertyName("lng")]       double Lng,
        [property: JsonPropertyName("isSnapped")] bool   IsSnapped);

    // ── Disposal ─────────────────────────────────────────────────────────────

    public async ValueTask DisposeAsync()
    {
        _selfRef?.Dispose();
        await ValueTask.CompletedTask;
    }
}
