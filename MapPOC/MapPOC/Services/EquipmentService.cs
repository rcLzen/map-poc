using MapPOC.Models;

namespace MapPOC.Services;

public sealed class EquipmentService
{
    private readonly List<EquipmentMarker> _markers = [];

    public IReadOnlyList<EquipmentMarker> Markers => _markers;

    public event Action? OnMarkersChanged;

    public void AddMarker(EquipmentMarker marker)
    {
        _markers.Add(marker);
        OnMarkersChanged?.Invoke();
    }

    public void RemoveMarker(string markerId)
    {
        _markers.RemoveAll(m => m.Id == markerId);
        OnMarkersChanged?.Invoke();
    }

    public void UpdateMarker(EquipmentMarker marker)
    {
        var index = _markers.FindIndex(m => m.Id == marker.Id);
        if (index >= 0)
        {
            _markers[index] = marker;
            OnMarkersChanged?.Invoke();
        }
    }

    public void ClearMarkers()
    {
        _markers.Clear();
        OnMarkersChanged?.Invoke();
    }
}
