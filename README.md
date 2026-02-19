# DWG Map Overlay PoC

A Blazor WebAssembly Progressive Web App (PWA) for overlaying DWG-derived floor-plan images on a live interactive map. Supports offline use after the first load.

---

## Features

| Feature | Status |
|---|---|
| Base map switching (OpenStreetMap / CartoDB / Mapbox) | ✅ Task 1–2 |
| File upload panel (PNG, GeoJSON, ZIP) with progress | ✅ Task 3 |
| 3-point georeferenced PNG overlay (Quick Align) | ✅ Task 4 |
| Offline TMS tile pack served from gdal2tiles ZIP | ✅ Task 5 |
| GeoJSON snapping layer (vertex + edge, Turf.js) | ✅ Task 6 |
| Equipment marker placement with auto-snap | ✅ Task 7 |
| Bootstrap-only UI controls, opacity sliders | ✅ Task 8 |
| IndexedDB asset persistence (no re-upload on reload) | ✅ Task 9 |
| Offline PWA — service worker caches all assets + tiles | ✅ Task 9 |
| Map screenshot export (html2canvas) | ✅ Task 10 |
| Equipment GeoJSON export | ✅ Task 10 |

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
- **ZIP** — a [gdal2tiles](https://gdal.org/programs/gdal2tiles.html) tile archive for offline TMS (≤ 500 MB)

Assets are persisted to IndexedDB. After a page reload, they are immediately usable without re-uploading.

### 2 — Quick Align (3-Point Georeferencing)

1. Select **OVERLAY MODE → Quick Align**.
2. Enter the image dimensions in pixels (or let the panel auto-detect from the thumbnail).
3. For each of the 3 control points:
   - Click **Click map** and then click the corresponding location on the Leaflet map → latitude/longitude fills in automatically.
   - Type the matching pixel X and Y coordinates from your DWG or image editor.
4. Click **Apply** — the PNG is pinned to the map using an affine transform.
5. Use the **Opacity** slider to blend the overlay with the basemap.
6. Click **Remove** to clear the overlay.

> **Tip**: Choose 3 control points that are well spread across the image (not collinear) for the most accurate alignment.

### 3 — TMS Tiles (Offline Tile Pack)

1. Upload a gdal2tiles ZIP (generated with `gdal2tiles.py -z 14-18 input.tif tiles/`).
2. Select **OVERLAY MODE → TMS Tiles**.
3. Click **Load Tiles** — JSZip decompresses the archive into an in-memory blob-URL cache.
4. Click **Show on Map** — a custom Leaflet `GridLayer` serves tiles from the cache.
5. Use the **Opacity** slider to blend.

### 4 — Equipment Placement

1. In the **EQUIPMENT** section, select a marker type (Valve / Pump / Sensor / Custom).
2. Toggle **Snap to GeoJSON** if a GeoJSON was uploaded — new markers automatically snap to the nearest vertex or edge.
3. Click **Add Equipment** to enter placement mode, then click on the map.
4. Drag markers to reposition; the sidebar list updates in real time.
5. Click **Export Equipment** or the EXPORT section to download all markers as a GeoJSON `FeatureCollection`.

### 5 — Map Screenshot

In the **EXPORT** section, click **Screenshot Map**. html2canvas captures the Leaflet viewport and downloads a timestamped PNG.

> **Note**: Cross-origin basemap tiles (OSM, CartoDB, Mapbox) are rendered as blank squares due to browser CORS restrictions. Blob-sourced tiles (TMS ZIP cache, rotated PNG overlay) are captured correctly.

---

## Preparing Tile Data with GDAL

```bash
# Install GDAL
# Ubuntu: sudo apt install gdal-bin
# macOS:  brew install gdal

# Convert a georeferenced image to a TMS tile archive
gdal2tiles.py \
  --zoom=14-18 \
  --tiledriver=PNG \
  --tmscompatible \
  input.tif \
  tiles/

# Zip the output folder
zip -r tiles.zip tiles/
```

Upload `tiles.zip` in the **ASSETS → ZIP** slot, then use **OVERLAY MODE → TMS Tiles**.

---

## Architecture

```
index.html
  CDN scripts (Leaflet, JSZip, Leaflet.ImageOverlay.Rotated, Turf.js, html2canvas)
  Local JS (leaflet-interop, asset-interop, idb-interop, align-interop,
            tms-interop, snap-interop, equipment-interop, map-export-interop)
  blazor.webassembly.js
    → App.razor → MainLayout.razor (shell + sidebar)
        FileUploadPanel   — DwgAssetService (PNG / GeoJSON / ZIP upload + IDB persist)
        AlignmentPanel    — QuickAlignService (3-point affine georeferencing)
        TmsTilePanel      — TmsTileService (ZIP → tile cache → GridLayer)
        EquipmentPanel    — EquipmentService + SnappingService
        MapExportPanel    — JS mapExportInterop (html2canvas + download)
        MapComponent      — Leaflet host (LeafletForBlazor 1.2.0)
```

### Service → JS interop mapping

| C# Service | JS module | Purpose |
|---|---|---|
| `DwgAssetService` | `assetInterop` + `idbInterop` | Blob URLs, localStorage, IndexedDB |
| `QuickAlignService` | `alignInterop` | Rotated overlay, map click capture |
| `TmsTileService` | `tmsInterop` | JSZip unpack, custom GridLayer |
| `SnappingService` | `snapInterop` | Turf.js nearest-point / nearest-on-line |
| `EquipmentService` | `equipmentInterop` | SVG markers, drag events, GeoJSON download |
| `MapExportPanel` | `mapExportInterop` | html2canvas, PNG/GeoJSON download |

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

- **Screenshot accuracy**: Cross-origin basemap tiles render as blank. Use the TMS ZIP overlay or the rotated PNG overlay — both use blob: URLs and are captured correctly.
- **Large ZIPs**: Decompressing a full TMS archive in-browser keeps all tile blobs in memory. For very large tile sets (> 100 MB uncompressed), consider splitting by zoom level.
- **IDB storage quota**: Browser IndexedDB quotas vary (typically 50–80 % of available disk). Upload failures due to quota are logged to the browser console.
- **Affine accuracy**: The 3-point alignment uses a 2D affine transform. For large areas or images with lens distortion, higher-order transforms (e.g. rubber-sheeting) would give better results.
- **No authentication**: This is a PoC with no server side. All data stays in the browser.
