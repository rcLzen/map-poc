# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run Commands

```bash
# Restore dependencies
dotnet restore DwgMapOverlayPoc/DwgMapOverlayPoc.csproj

# Build
dotnet build DwgMapOverlayPoc/DwgMapOverlayPoc.csproj

# Run (hot-reload dev server on https://localhost:5001)
dotnet run --project DwgMapOverlayPoc/DwgMapOverlayPoc.csproj

# Run tests
dotnet test DwgMapOverlayPoc/DwgMapOverlayPoc.Tests/DwgMapOverlayPoc.Tests.csproj

# Publish (produces pre-compressed assets + service worker)
dotnet publish DwgMapOverlayPoc/DwgMapOverlayPoc.csproj -c Release -o publish/
```

No linter is configured beyond the compiler.

## Architecture Overview

**Stack**: .NET 8 Blazor WebAssembly PWA + Leaflet 1.9.4 (CDN) + LeafletForBlazor 1.2.0 (NuGet)

**CDN dependencies** (loaded in `index.html` before Blazor boots): Leaflet 1.9.4, JSZip 3.10.1, Turf.js v6, html2canvas 1.4.1. `Leaflet.ImageOverlay.Rotated` is bundled locally.

### Boot Sequence

```
index.html
  → Leaflet JS (CDN, window.L)
  → JSZip, Turf.js, html2canvas (CDN)
  → Leaflet.ImageOverlay.Rotated.js (local, extends L)
  → idb-interop.js → asset-interop.js (must be in this order)
  → leaflet-interop.js (exposes whenMapReady + captureMap)
  → leaflet-capture.js (L.Map addInitHook -> captureMap)
  → align-interop.js, xyz-interop.js, snap-interop.js,
    equipment-interop.js, map-export-interop.js
  → blazor.webassembly.js (boots last)
      → MapComponent.razor
          → <Map> (LeafletForBlazor)
          → OnLeafletMapLoaded() → onLoadMap() → setBaseLayer / syncOverlays
          → fires OnMapReady EventCallback to MainLayout
```

### JS Interop Architecture

Each feature area has its own `window.*` JS module. C# services call them via `IJSRuntime.InvokeVoidAsync("moduleName.method", ...)`.

| JS Module (`window.*`) | C# Consumer | Purpose |
|---|---|---|
| `leafletInterop` | `MapComponent` | Base layer, flyTo, overlay sync, `whenMapReady()` |
| `alignInterop` | `QuickAlignService` | Rotated image overlay, map-click capture for 3-point alignment |
| `xyzInterop` | `XyzTileService` | JSZip unpack → in-memory tile cache → custom GridLayer |
| `snapInterop` | `SnappingService` | Turf.js nearest-point/nearest-on-line |
| `equipmentInterop` | `EquipmentService` | SVG markers, drag events, GeoJSON export |
| `assetInterop` | `DwgAssetService` | Blob URL creation, thumbnails, localStorage |
| `idbInterop` | `DwgAssetService` | IndexedDB persistence (DB: `"dwg-map-poc-v1"`, store: `"assets"`) |
| `mapExportInterop` | `MapExportPanel` | html2canvas screenshot, file downloads |

### Map Readiness Pattern

**Critical**: LeafletForBlazor 1.2.0 does **not** expose the Leaflet map instance to `window`. The project captures the map using `L.Map.addInitHook` in `leaflet-capture.js`, which calls `leafletInterop.captureMap(...)`.

`leafletInterop.whenMapReady()` returns a Promise that resolves with the map instance. It uses a deterministic flow:
1. `leaflet-capture.js` captures the map instance as soon as Leaflet constructs it.
2. `MapComponent.OnLeafletMapLoaded` calls `leafletInterop.onLoadMap()` to finalize readiness sequencing.
3. All pending `whenMapReady()` callers resolve immediately when the map is captured.

**All interop functions that touch the map must `await whenMapReady()` first.** Synchronous cleanup functions use `leafletInterop.getMapIfReady()` when needed.

`MapComponent.razor` fires `[Parameter] EventCallback OnMapReady` after `OnLeafletMapLoaded` successfully calls `onLoadMap` + `setBaseLayer`. `MainLayout` subscribes to know when `SetBaseMapAsync` is safe.

### Services (all Scoped)

| Service | JS Interop | Purpose |
|---|---|---|
| `BaseMapService` | none | Provider catalogue (OSM/CartoDB/Mapbox); reads `Mapbox:Token` from config |
| `OverlayService` | none | In-memory `MapOverlayModel` list; fires `event Action? OnChange` |
| `TileService` | none | Built-in `TileSourceModel` list (OSM, OSM HOT, ESRI) |
| `DwgAssetService` | `assetInterop`, `idbInterop` | Upload slots (PNG/GeoJSON/ZIP): validate, stream, blob URLs, IDB persist/restore |
| `QuickAlignService` | `alignInterop` | 3-point affine transform (Cramer's rule in C#) → `L.imageOverlay.rotated` |
| `XyzTileService` | `xyzInterop` | ZIP → in-memory XYZ tile cache → Leaflet GridLayer |
| `SnappingService` | `snapInterop` | GeoJSON snap; converts pixel radius to degree tolerance |
| `EquipmentService` | `equipmentInterop`, `leafletInterop` | Marker CRUD + snap + GeoJSON export; holds `DotNetObjectReference` (implements `IAsyncDisposable`) |

### LeafletForBlazor v1.2.0 API

The NuGet README documents a different API version. Actual types in v1.2.0:

```csharp
Map.LoadParameters { basemap: Map.Basemap { basemap_layers: List<Map.BasemapConfigLayer> } }
Map.OnLoadEventParameters  // callback parameter from OnLeafletMapLoaded
```

`RealTimeMap.*` types do **not** exist in 1.2.0.

### CSS Height Chain

The Leaflet map needs an unbroken chain of defined heights from `html` to its container:

```
html(100%) → body(100%) → #app(100%) → .shell(100vh)
  → .map-area(flex:1) → .map-wrapper(100%) → .leaflet-container(100%)
```

`app.css` owns `html/body/#app`. `MainLayout.razor.css` owns `.shell/.map-area`. `MapComponent.razor.css` owns `.map-wrapper` and uses `::deep .leaflet-container` to pierce Blazor CSS isolation into LeafletForBlazor's DOM.

### CSS Isolation Quirk

`::deep` in `.razor.css` compiles to a descendant selector without the scope hash on the leaf element — e.g., `.map-wrapper[b-hash] .leaflet-container`. Required to reach elements rendered by child components.

### CDN SRI

`integrity=` attributes are intentionally absent from CDN tags in `index.html`. unpkg.com's variable content-encoding causes Chrome to silently reject resources even when bytes are correct.

### IndexedDB Persistence

`DwgAssetService` stores uploaded asset blobs in IndexedDB via `idbInterop`. On page reload, `LoadMetadataAsync()` attempts IDB restore first (assets become immediately usable without re-upload), then falls back to localStorage stubs. DB name: `"dwg-map-poc-v1"`, object store: `"assets"`, keys: `"png" | "geojson" | "zip"`.

### Mapbox Token

`wwwroot/appsettings.json` holds `{ "Mapbox": { "Token": "pk.REPLACE_WITH_YOUR_MAPBOX_TOKEN" } }`. `BaseMapService.HasValidMapboxToken` returns false when the token equals that placeholder, and the sidebar shows a warning badge.

### PWA / Service Worker

`service-worker.js` (dev) is a no-op passthrough. `service-worker.published.js` (Release publish) pre-caches Blazor assets and CDN resources, and uses stale-while-revalidate for map tiles (`map-tiles-v1` cache, up to 2000 tiles). The `.csproj` ServiceWorker item maps dev → published at publish time.

## Razor Gotchas

These patterns **break the Razor parser** silently — use the alternatives:

| Broken pattern | Safe alternative |
|---|---|
| `@if (x is { Prop: > 0 } y)` — property pattern with `{}` in condition | `@if (x?.Prop > 0)` + null-check via method |
| `@expr!.Member` — null-forgiving `!` in implicit Razor expression | `@(expr!.Member)` — explicit `@(...)` |
| Helper methods returning `RenderFragment` with lambda `{ }` bodies inside `@code` | Move to `.razor.cs` partial class code-behind |
| `@{ var x = ...; }` inside `@if` body when Razor parser is confused | Move variable to `@code` method |

**Rule of thumb**: Put any C# containing `{ }` in lambdas, switch expressions, or string interpolations into a `.razor.cs` code-behind file. Keep `@code` blocks to lifecycle methods + event handlers only.

## Component Architecture

`MainLayout.razor` is the shell: collapsible sidebar (280 px) + full-screen map. It owns all sidebar state (`_activeGroupKey`, `_activeTheme`, `_overlayMode` enum: `None/QuickAlign/Xyz`) and holds `@ref` to `MapComponent`. No `.razor.cs` code-behind — all code is inline `@code`. `MapComponent.razor` also has no `.razor.cs`.

Panels with code-behind (`.razor.cs`):
- `AlignmentPanel` — manages `ControlPoint[]`, pixel/world coordinates, map-click capture
- `EquipmentPanel` — monitors `DwgAssetService` for GeoJSON readiness, auto-loads snap layer
- `XyzTilePanel` — drives ZIP→tile-cache→GridLayer lifecycle
- `MapExportPanel` — screenshot capture + download
- `FileUploadPanel` — three upload slots with rotating `Guid` keys for stable `InputFile` elements
