using System.Text.Json;
using DwgMapOverlayPoc.Models;
using DwgMapOverlayPoc.Services;
using Microsoft.JSInterop;

namespace DwgMapOverlayPoc.Tests;

public class DwgAssetTests
{
    [Theory]
    [InlineData(512L, "512 B")]
    [InlineData(1023L, "1023 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(1_048_575L, "1024.0 KB")]
    [InlineData(5L * 1_048_576, "5.0 MB")]
    [InlineData(2L * 1_073_741_824, "2.00 GB")]
    public void FormattedSizeUsesExpectedUnit(long size, string expected)
        => Assert.Equal(expected, new DwgAsset { Size = size }.FormattedSize);
}

public class QuickAlignServiceTests
{
    private static ControlPoint P(double x, double y, double lat, double lng) =>
        new() { ImageX = x, ImageY = y, WorldLat = lat, WorldLng = lng, HasMapCoords = true };

    [Fact]
    public async Task CollinearPointsFailWithoutCallingJs()
    {
        var js = new FakeJSRuntime();
        var svc = new QuickAlignService(js);
        var notified = 0;
        svc.OnChanged += () => notified++;

        var ok = await svc.ApplyAsync([P(0, 0, 1, 1), P(10, 10, 2, 2), P(20, 20, 3, 3)], 100, 100, "blob:x", 0.5);

        Assert.False(ok);
        Assert.False(svc.HasOverlay);
        Assert.Contains("collinear", svc.LastError);
        Assert.Empty(js.Calls);
        Assert.Equal(1, notified);
    }

    [Fact]
    public async Task ValidPointsProduceExpectedCorners()
    {
        var js = new FakeJSRuntime();
        var svc = new QuickAlignService(js);

        var ok = await svc.ApplyAsync(
            [P(0, 0, 10, 20), P(100, 0, 10, 21), P(0, 100, 9, 20)], 100, 100, "blob:x", 0.75);

        Assert.True(ok);
        Assert.True(svc.HasOverlay);
        Assert.Null(svc.LastError);
        var args = Assert.Single(js.Calls, c => c.Id == "alignInterop.setRotatedOverlay").Args;
        Assert.Equal("blob:x", args[0]);
        Assert.Equal([10.0, 20.0], (double[])args[1]!, new PrecisionComparer());
        Assert.Equal([10.0, 21.0], (double[])args[2]!, new PrecisionComparer());
        Assert.Equal([9.0, 20.0], (double[])args[3]!, new PrecisionComparer());
        Assert.Equal(0.75, args[4]);
    }

    [Fact]
    public async Task JsExceptionSetsLastError()
    {
        var js = new FakeJSRuntime { Handler = (_, _) => new JSException("boom") };
        var svc = new QuickAlignService(js);

        var ok = await svc.ApplyAsync(
            [P(0, 0, 10, 20), P(100, 0, 10, 21), P(0, 100, 9, 20)], 100, 100, "blob:x", 1);

        Assert.False(ok);
        Assert.False(svc.HasOverlay);
        Assert.Contains("boom", svc.LastError);
    }

    [Fact]
    public async Task RemoveClearsOverlayAndNotifies()
    {
        var js = new FakeJSRuntime();
        var svc = new QuickAlignService(js);
        await svc.ApplyAsync([P(0, 0, 10, 20), P(100, 0, 10, 21), P(0, 100, 9, 20)], 100, 100, "b", 1);
        var notified = 0;
        svc.OnChanged += () => notified++;

        await svc.RemoveAsync();

        Assert.False(svc.HasOverlay);
        Assert.Equal(1, notified);
        Assert.Equal(1, js.Count("alignInterop.clearRotatedOverlay"));
    }

    private sealed class PrecisionComparer : IEqualityComparer<double>
    {
        public bool Equals(double a, double b) => Math.Abs(a - b) < 1e-9;
        public int GetHashCode(double d) => 0;
    }
}

public class SnappingServiceTests
{
    [Fact]
    public async Task DisabledReturnsOriginalWithoutJs()
    {
        var js = new FakeJSRuntime();
        var svc = new SnappingService(js);

        Assert.Equal((1.5, 2.5, false), await svc.SnapAsync(1.5, 2.5, 15));
        Assert.Empty(js.Calls);
    }

    [Fact]
    public async Task ToleranceIsPixelRadiusConvertedToDegrees()
    {
        var js = new FakeJSRuntime();
        var svc = new SnappingService(js) { SnapEnabled = true, SnapRadiusPx = 10 };

        await svc.SnapAsync(1, 2, 15);

        var tol = (double)Assert.Single(js.Calls).Args[2]!;
        Assert.Equal(10 / (256.0 * Math.Pow(2, 15) / 360.0), tol, 12);
    }

    [Fact]
    public async Task SnapHitReturnsSnappedCoordinates()
    {
        var js = new FakeJSRuntime { Handler = (_, _) => "{\"lat\":3.0,\"lng\":4.0}" };
        var svc = new SnappingService(js) { SnapEnabled = true };

        Assert.Equal((3.0, 4.0, true), await svc.SnapAsync(1, 2, 15));
    }

    [Fact]
    public async Task SnapMissReturnsOriginal()
    {
        var svc = new SnappingService(new FakeJSRuntime()) { SnapEnabled = true };

        Assert.Equal((1.0, 2.0, false), await svc.SnapAsync(1, 2, 15));
    }

    [Fact]
    public async Task JsErrorReturnsOriginal()
    {
        var js = new FakeJSRuntime { Handler = (_, _) => new JSException("x") };
        var svc = new SnappingService(js) { SnapEnabled = true };

        Assert.Equal((1.0, 2.0, false), await svc.SnapAsync(1, 2, 15));
    }
}

public class XyzTileServiceTests
{
    [Fact]
    public void InitialStateIsIdle()
    {
        var svc = new XyzTileService(new FakeJSRuntime());

        Assert.Equal(XyzStatus.Idle, svc.Status);
        Assert.Equal(0, svc.TileCount);
        Assert.Empty(svc.ZoomLevels);
        Assert.False(svc.IsLayerVisible);
    }

    [Fact]
    public async Task LoadSuccessGoesLoadingThenReady()
    {
        var js = new FakeJSRuntime { Handler = (_, _) => "{\"tileCount\":42,\"zooms\":[14,15,16]}" };
        var svc = new XyzTileService(js);
        var seen = new List<XyzStatus>();
        svc.OnChanged += () => seen.Add(svc.Status);

        await svc.LoadAsync("blob:zip");

        Assert.Equal([XyzStatus.Loading, XyzStatus.Ready], seen);
        Assert.Equal(42, svc.TileCount);
        Assert.Equal([14, 15, 16], svc.ZoomLevels);
    }

    [Fact]
    public async Task LoadFailureGoesToError()
    {
        var js = new FakeJSRuntime { Handler = (_, _) => new JSException("bad zip") };
        var svc = new XyzTileService(js);

        await svc.LoadAsync("blob:zip");

        Assert.Equal(XyzStatus.Error, svc.Status);
        Assert.Contains("bad zip", svc.LastError);
    }

    [Fact]
    public async Task ActivateAndDeactivateToggleVisibility()
    {
        var svc = new XyzTileService(new FakeJSRuntime());

        await svc.ActivateAsync();
        Assert.True(svc.IsLayerVisible);
        await svc.DeactivateAsync();
        Assert.False(svc.IsLayerVisible);
    }

    [Fact]
    public async Task ActivateFailureKeepsLayerHidden()
    {
        var js = new FakeJSRuntime { Handler = (_, _) => new JSException("no map") };
        var svc = new XyzTileService(js);

        await svc.ActivateAsync();

        Assert.False(svc.IsLayerVisible);
        Assert.Contains("no map", svc.LastError);
    }
}

public class EquipmentServiceTests
{
    private static (EquipmentService Svc, FakeJSRuntime Js) Create()
    {
        var js = new FakeJSRuntime();
        return (new EquipmentService(js, new SnappingService(js)), js);
    }

    [Fact]
    public void EmptyGeoJsonIsEmptyFeatureCollection()
    {
        var (svc, _) = Create();

        using var doc = JsonDocument.Parse(svc.BuildGeoJson());
        Assert.Equal("FeatureCollection", doc.RootElement.GetProperty("type").GetString());
        Assert.Equal(0, doc.RootElement.GetProperty("features").GetArrayLength());
    }

    [Fact]
    public async Task GeoJsonUsesLngLatOrderAndProperties()
    {
        var (svc, _) = Create();
        svc.PlacementType = EquipmentType.Pump;
        await svc.AddMarkerAsync(10.5, 20.5);

        using var doc = JsonDocument.Parse(svc.BuildGeoJson());
        var f = doc.RootElement.GetProperty("features")[0];
        var coords = f.GetProperty("geometry").GetProperty("coordinates");
        Assert.Equal(20.5, coords[0].GetDouble());
        Assert.Equal(10.5, coords[1].GetDouble());
        Assert.Equal("Pump", f.GetProperty("properties").GetProperty("type").GetString());
        Assert.Equal("Pump 1", f.GetProperty("properties").GetProperty("label").GetString());
        Assert.False(f.GetProperty("properties").GetProperty("isSnapped").GetBoolean());
    }

    [Fact]
    public async Task AddMarkerNotifiesAndPersists()
    {
        var (svc, js) = Create();
        var notified = 0;
        svc.OnChanged += () => notified++;

        await svc.AddMarkerAsync(1, 2);

        Assert.Single(svc.Markers);
        Assert.Equal(1, notified);
        Assert.Equal(1, js.Count("equipmentInterop.addMarker"));
        Assert.Equal(1, js.Count("assetInterop.localStorageSet"));
    }

    [Fact]
    public async Task RemoveMarkerRemovesOnlyThatMarker()
    {
        var (svc, js) = Create();
        await svc.AddMarkerAsync(1, 2);
        await svc.AddMarkerAsync(3, 4);
        var keep = svc.Markers[1].Id;

        await svc.RemoveMarkerAsync(svc.Markers[0].Id);

        Assert.Equal(keep, Assert.Single(svc.Markers).Id);
        Assert.Equal(1, js.Count("equipmentInterop.removeMarker"));
    }

    [Fact]
    public async Task RemoveUnknownMarkerIsNoOp()
    {
        var (svc, js) = Create();
        await svc.AddMarkerAsync(1, 2);

        await svc.RemoveMarkerAsync("missing");

        Assert.Single(svc.Markers);
        Assert.Equal(0, js.Count("equipmentInterop.removeMarker"));
    }

    [Fact]
    public async Task ClearAllEmptiesMarkers()
    {
        var (svc, _) = Create();
        await svc.AddMarkerAsync(1, 2);
        await svc.AddMarkerAsync(3, 4);

        await svc.ClearAllAsync();

        Assert.Empty(svc.Markers);
    }

    [Fact]
    public async Task InitIsIdempotent()
    {
        var (svc, js) = Create();

        await svc.InitAsync();
        await svc.InitAsync();

        Assert.Equal(1, js.Count("equipmentInterop.init"));
    }

    [Fact]
    public async Task InitRestoresPersistedMarkers()
    {
        var js = new FakeJSRuntime
        {
            Handler = (id, _) => id == "assetInterop.localStorageGet"
                ? JsonSerializer.Serialize(
                    "[{\"id\":\"a1\",\"type\":\"Sensor\",\"label\":\"S\",\"lat\":1,\"lng\":2,\"isSnapped\":true}," +
                    "{\"id\":\"a2\",\"type\":\"Bogus\",\"label\":\"B\",\"lat\":3,\"lng\":4,\"isSnapped\":false}]")
                : null
        };
        var svc = new EquipmentService(js, new SnappingService(js));

        await svc.InitAsync();

        Assert.Equal(2, svc.Markers.Count);
        Assert.Equal(EquipmentType.Sensor, svc.Markers[0].Type);
        Assert.Equal(EquipmentType.Custom, svc.Markers[1].Type);
    }

    [Fact]
    public async Task PlacementModeCallsMatchingJs()
    {
        var (svc, js) = Create();

        await svc.SetPlacementModeAsync(true);
        Assert.True(svc.PlacementMode);
        await svc.SetPlacementModeAsync(false);

        Assert.Equal(1, js.Count("equipmentInterop.startPlacementMode"));
        Assert.Equal(1, js.Count("equipmentInterop.stopPlacementMode"));
    }

    [Fact]
    public async Task ClickAddsMarkerAndExitsPlacementMode()
    {
        var (svc, _) = Create();
        await svc.SetPlacementModeAsync(true);

        await svc.OnEquipmentClicked(1, 2);

        Assert.False(svc.PlacementMode);
        Assert.Single(svc.Markers);
    }

    [Fact]
    public async Task DragUpdatesCoordinatesAndClearsSnapped()
    {
        var (svc, _) = Create();
        await svc.AddMarkerAsync(1, 2);
        svc.Markers[0].IsSnapped = true;

        await svc.OnMarkerDragged(svc.Markers[0].Id, 5, 6);

        Assert.Equal((5.0, 6.0, false), (svc.Markers[0].Lat, svc.Markers[0].Lng, svc.Markers[0].IsSnapped));
    }
}
