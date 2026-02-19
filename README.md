# DWG Map Overlay PoC

A Blazor WebAssembly Progressive Web App (PWA) for overlaying DWG-derived floor-plan images on a live interactive map. Supports offline use after the first load.

---

## Features

| Feature | Status |
|---|---|
| Base map switching (OpenStreetMap / CartoDB / Mapbox) | ✅ |
| File upload panel (PNG, GeoJSON, ZIP) with progress | ✅ |
| 3-point georeferenced PNG overlay (Quick Align) | ✅ |
| Offline XYZ tile pack served from gdal2tiles --xyz ZIP | ✅ |
| GeoJSON snapping layer (vertex + edge, Turf.js) | ✅ |
| Equipment marker placement with auto-snap | ✅ |
| Bootstrap-only UI controls, opacity sliders | ✅ |
| IndexedDB asset persistence (no re-upload on reload) | ✅ |
| Offline PWA — service worker caches all assets + tiles | ✅ |
| Map screenshot export (html2canvas) | ✅ |
| Equipment GeoJSON export | ✅ |

---

## Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- A modern browser (Chrome 90+, Edge 90+, Firefox 90+)
- _(Optional)_ A Mapbox public token if you want the Mapbox Streets basemap

---

## Quick Start

```bash
# 1. Clone or unzip the project
cd DwgMapOverlayPoc

# 2. (Optional) Set your Mapbox token
#    Edit wwwroot/appsettings.json and replace the placeholder:
#    "Mapbox": { "Token": "pk.YOUR_TOKEN_HERE" }

# 3. Run the dev server
dotnet run --project DwgMapOverlayPoc.csproj

# 4. Open  https://localhost:5001  in Chrome
```

### PWA / Offline Build

The service worker with full offline caching is only active in the **published** build:

```bash
dotnet publish DwgMapOverlayPoc.csproj -c Release -o ../publish
# Serve ../publish/wwwroot with any static HTTPS server, e.g.:
npx serve -s ../publish/wwwroot
```

---

## Typical Usage Workflow

### 1 — Upload Assets

Open the sidebar **ASSETS** section and upload:

- **PNG** — a floor-plan or DWG export image (≤ 200 MB)
- **GeoJSON** — pipe/network geometry for snapping (≤ 50 MB)
- **ZIP** — a gdal2tiles `--xyz` tile archive for offline XYZ tiles (≤ 500 MB)

Assets are persisted to IndexedDB. After a page reload they are immediately usable without re-uploading.

### 2 — Quick Align (3-Point Georeferencing)

1. Select **OVERLAY MODE → Quick Align**.
2. Enter the image dimensions in pixels (or let the panel auto-detect from the thumbnail).
3. For each of the 3 control points:
   - Click **Click map** and then click the corresponding location on the Leaflet map → latitude/longitude fills in automatically.
   - Type the matching pixel X and Y coordinates from your DWG or image editor.
4. Click **Apply** — the PNG is pinned to the map using an affine transform.
5. Use the **Opacity** slider to blend the overlay with the basemap.
6. Click the active **Quick Align** button again (or **Remove**) to dismiss the overlay.

> **Tip**: Choose 3 control points that are well spread across the image (not collinear) for the most accurate alignment.

### 3 — XYZ Tiles (Offline Tile Pack)

1. Generate a tile archive with gdal2tiles using the `--xyz` flag (see [Preparing Tile Data](#preparing-tile-data-with-gdal) below).
2. Upload the ZIP in the **ASSETS → XYZ Tiles ZIP** slot.
3. Select **OVERLAY MODE → XYZ Tiles**.
4. Click **Load Tiles from ZIP** — JSZip decompresses the archive into an in-memory blob-URL cache.
5. Click **Show on Map** — a custom Leaflet `GridLayer` serves tiles from memory using standard Z/X/Y addressing.
6. Use the **Opacity** slider to blend.
7. Click the active **XYZ Tiles** button again to dismiss the layer.

### 4 — Equipment Placement

1. In the **EQUIPMENT** section, select a marker type (Valve / Pump / Sensor / Custom).
2. Toggle **Snap to GeoJSON** if a GeoJSON was uploaded — new markers automatically snap to the nearest vertex or edge.
3. Click **Add Equipment** to enter placement mode, then click on the map.
4. Drag markers to reposition; the sidebar list updates in real time.
5. Click **Export Equipment** to download all markers as a GeoJSON `FeatureCollection`.

### 5 — Map Screenshot

In the **EXPORT** section, click **Screenshot Map**. html2canvas captures the Leaflet viewport and downloads a timestamped PNG.

> **Note**: Cross-origin basemap tiles (OSM, CartoDB, Mapbox) are rendered as blank squares due to browser CORS restrictions. Blob-sourced tiles (XYZ ZIP cache, Quick Align overlay) are captured correctly.

---

## Preparing Tile Data with GDAL

```bash
# Install GDAL
# Ubuntu: sudo apt install gdal-bin
# macOS:  brew install gdal

# Convert a georeferenced image to an XYZ tile archive
gdal2tiles.py \
  --xyz \
  --zoom=14-18 \
  --tiledriver=PNG \
  input.tif \
  tiles/

# Zip the output folder
zip -r tiles.zip tiles/
```

Upload `tiles.zip` in the **ASSETS → XYZ Tiles ZIP** slot, then use **OVERLAY MODE → XYZ Tiles**.

> The `--xyz` flag produces standard slippy-map Z/X/Y tile coordinates (north-up Y axis). Do **not** omit it; without `--xyz`, gdal2tiles produces TMS-style Y-inverted tiles that will not align correctly.

---

## Architecture

```
index.html
  CDN scripts (Leaflet, JSZip, Leaflet.ImageOverlay.Rotated, Turf.js, html2canvas)
  Local JS (leaflet-interop, asset-interop, idb-interop, align-interop,
            xyz-interop, snap-interop, equipment-interop, map-export-interop)
  blazor.webassembly.js
    → App.razor → MainLayout.razor (shell + sidebar)
        FileUploadPanel   — DwgAssetService (PNG / GeoJSON / ZIP upload + IDB persist)
        AlignmentPanel    — QuickAlignService (3-point affine georeferencing)
        XyzTilePanel      — XyzTileService (ZIP → XYZ tile cache → GridLayer)
        EquipmentPanel    — EquipmentService + SnappingService
        MapExportPanel    — JS mapExportInterop (html2canvas + download)
        MapComponent      — Leaflet host (LeafletForBlazor 1.2.0)
```

### Service → JS interop mapping

| C# Service | JS module | Purpose |
|---|---|---|
| `DwgAssetService` | `assetInterop` + `idbInterop` | Blob URLs, localStorage, IndexedDB |
| `QuickAlignService` | `alignInterop` | Rotated overlay, map click capture |
| `XyzTileService` | `xyzInterop` | JSZip unpack, custom GridLayer (XYZ) |
| `SnappingService` | `snapInterop` | Turf.js nearest-point / nearest-on-line |
| `EquipmentService` | `equipmentInterop` | SVG markers, drag events, GeoJSON download |
| `MapExportPanel` | `mapExportInterop` | html2canvas, PNG/GeoJSON download |

### Overlay modes

| Mode | What it does |
|---|---|
| **Quick Align** | Pins an affine-transformed PNG to 3 user-picked map coordinates |
| **XYZ Tiles** | Serves a gdal2tiles `--xyz` ZIP from memory as a Leaflet GridLayer |

Clicking the active mode button again deactivates it (returns to base map only).

### Offline strategy (service-worker.published.js)

| Request type | Strategy |
|---|---|
| Blazor `.dll`, `.wasm`, static assets | Cache-first (pre-cached at install) |
| CDN libraries (Leaflet, JSZip, Turf, html2canvas) | Cache-first (pre-cached at install) |
| Map tiles (OSM, CartoDB, Mapbox) | Stale-while-revalidate (up to 2 000 tiles) |
| Blob URLs | Always network (they are local memory) |

---

## Configuration

**`wwwroot/appsettings.json`** — runtime configuration loaded by Blazor:

```json
{
  "Mapbox": {
    "Token": "pk.REPLACE_WITH_YOUR_MAPBOX_TOKEN"
  }
}
```

`BaseMapService.HasValidMapboxToken` returns `false` when the token equals the placeholder, and the sidebar shows a warning badge next to the Mapbox option.

---

## Known Limitations

- **Screenshot accuracy**: Cross-origin basemap tiles render as blank. Use the XYZ ZIP overlay or the Quick Align PNG — both use blob: URLs and are captured correctly.
- **Large ZIPs**: Decompressing a full XYZ tile archive in-browser keeps all tile blobs in memory. For very large tile sets (> 100 MB uncompressed), consider splitting by zoom level.
- **IDB storage quota**: Browser IndexedDB quotas vary (typically 50–80 % of available disk). Upload failures due to quota are logged to the browser console.
- **Affine accuracy**: The 3-point alignment uses a 2D affine transform. For large areas or images with lens distortion, higher-order transforms would give better results.
- **No authentication**: This is a PoC with no server side. All data stays in the browser.
