namespace MapPOC.Models;

public sealed class MapLayer
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public MapLayerType LayerType { get; set; } = MapLayerType.TileLayer;
    public string UrlTemplate { get; set; } = string.Empty;
    public string Attribution { get; set; } = string.Empty;
    public int MaxZoom { get; set; } = 19;
    public bool IsVisible { get; set; } = true;
}

public enum MapLayerType
{
    TileLayer,
    ImageOverlay,
    GeoJson,
    XyzTiles
}
