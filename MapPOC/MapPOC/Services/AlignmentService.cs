using MapPOC.Models;

namespace MapPOC.Services;

public sealed class AlignmentService
{
    private readonly List<ControlPoint> _controlPoints = [];

    public IReadOnlyList<ControlPoint> ControlPoints => _controlPoints;
    public bool IsAlignmentMode { get; private set; }
    public string? RasterImageDataUrl { get; private set; }

    public event Action? OnStateChanged;

    public void StartAlignment(string imageDataUrl)
    {
        RasterImageDataUrl = imageDataUrl;
        IsAlignmentMode = true;
        _controlPoints.Clear();
        for (int i = 0; i < 3; i++)
        {
            _controlPoints.Add(new ControlPoint { Index = i });
        }
        OnStateChanged?.Invoke();
    }

    public void SetControlPoint(int index, double pixelX, double pixelY, double lat, double lng)
    {
        if (index < 0 || index >= _controlPoints.Count) return;

        var cp = _controlPoints[index];
        cp.PixelX = pixelX;
        cp.PixelY = pixelY;
        cp.Latitude = lat;
        cp.Longitude = lng;
        cp.IsSet = true;
        OnStateChanged?.Invoke();
    }

    public bool AreAllPointsSet() => _controlPoints.TrueForAll(cp => cp.IsSet);

    public void CancelAlignment()
    {
        IsAlignmentMode = false;
        RasterImageDataUrl = null;
        _controlPoints.Clear();
        OnStateChanged?.Invoke();
    }

    public void Reset()
    {
        CancelAlignment();
    }
}
