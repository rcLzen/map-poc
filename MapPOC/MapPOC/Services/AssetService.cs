namespace MapPOC.Services;

public sealed class AssetService
{
    public string? GeoJsonData { get; private set; }
    public string? XyzTilesUrl { get; private set; }

    public event Action? OnStateChanged;

    public void LoadGeoJson(string geoJson)
    {
        GeoJsonData = geoJson;
        OnStateChanged?.Invoke();
    }

    public void SetXyzTilesUrl(string url)
    {
        XyzTilesUrl = url;
        OnStateChanged?.Invoke();
    }

    public void ClearGeoJson()
    {
        GeoJsonData = null;
        OnStateChanged?.Invoke();
    }

    public void ClearXyzTiles()
    {
        XyzTilesUrl = null;
        OnStateChanged?.Invoke();
    }
}
