using DwgMapOverlayPoc.Models;

namespace DwgMapOverlayPoc.Services;

/// <summary>
/// Manages the collection of image overlays rendered on the map.
/// Raises <see cref="OnChange"/> whenever the list is mutated so consumers
/// can call <c>StateHasChanged</c>.
/// </summary>
public class OverlayService
{
    private readonly List<MapOverlayModel> _overlays = [];

    /// <summary>Fired whenever overlays are added, removed, or updated.</summary>
    public event Action? OnChange;

    /// <summary>Read-only view of current overlays.</summary>
    public IReadOnlyList<MapOverlayModel> Overlays => _overlays.AsReadOnly();

    /// <summary>Adds a new overlay and notifies subscribers.</summary>
    public void Add(MapOverlayModel overlay)
    {
        _overlays.Add(overlay);
        OnChange?.Invoke();
    }

    /// <summary>Removes an overlay by id and notifies subscribers.</summary>
    public bool Remove(Guid id)
    {
        var item = _overlays.FirstOrDefault(o => o.Id == id);
        if (item is null) return false;
        _overlays.Remove(item);
        OnChange?.Invoke();
        return true;
    }

    /// <summary>Toggles visibility of an overlay and notifies subscribers.</summary>
    public void ToggleVisibility(Guid id)
    {
        var item = _overlays.FirstOrDefault(o => o.Id == id);
        if (item is null) return;
        item.IsVisible = !item.IsVisible;
        OnChange?.Invoke();
    }

    /// <summary>Updates the opacity of an overlay and notifies subscribers.</summary>
    public void SetOpacity(Guid id, double opacity)
    {
        var item = _overlays.FirstOrDefault(o => o.Id == id);
        if (item is null) return;
        item.Opacity = Math.Clamp(opacity, 0.0, 1.0);
        OnChange?.Invoke();
    }
}
