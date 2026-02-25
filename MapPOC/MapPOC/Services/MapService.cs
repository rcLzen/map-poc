using MapPOC.Models;

namespace MapPOC.Services;

public sealed class MapService
{
    public MapLayer ActiveBaseLayer { get; private set; } = GetOpenStreetMapLayer();

    public List<MapLayer> BaseLayers { get; } =
    [
        GetOpenStreetMapLayer(),
        GetMapboxLayer()
    ];

    public event Action? OnBaseLayerChanged;

    public void SetBaseLayer(string layerId)
    {
        var layer = BaseLayers.Find(l => l.Id == layerId);
        if (layer is not null)
        {
            ActiveBaseLayer = layer;
            OnBaseLayerChanged?.Invoke();
        }
    }

    private static MapLayer GetOpenStreetMapLayer() => new()
    {
        Name = "OpenStreetMap",
        LayerType = MapLayerType.TileLayer,
        UrlTemplate = "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
        Attribution = "&copy; <a href='https://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors",
        MaxZoom = 19
    };

    private static MapLayer GetMapboxLayer() => new()
    {
        Name = "Mapbox Streets",
        LayerType = MapLayerType.TileLayer,
        UrlTemplate = "https://api.mapbox.com/styles/v1/mapbox/streets-v12/tiles/{z}/{x}/{y}?access_token={accessToken}",
        Attribution = "&copy; <a href='https://www.mapbox.com/'>Mapbox</a>",
        MaxZoom = 22
    };
}
