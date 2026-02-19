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

# Publish (produces dist/ with pre-compressed assets + service worker)
dotnet publish DwgMapOverlayPoc/DwgMapOverlayPoc.csproj -c Release -o publish/
```

There are no unit tests in this repo yet. No linter is configured beyond the compiler.

## Architecture Overview

**Stack**: .NET 8 Blazor WebAssembly PWA + Leaflet 1.9 (CDN) + LeafletForBlazor 1.2.0 (NuGet)

### Key Wiring

```
index.html
  → Leaflet JS (CDN, window.L)    ← must exist before Blazor boots
  → leaflet-interop.js            ← window.leafletInterop IIFE, uses window.LeafletBlazorMap
  → blazor.webassembly.js
      → MapComponent.razor
          → <Map> (LeafletForBlazor) → sets window.LeafletBlazorMap
          → OnLeafletMapLoaded()    → calls leafletInterop.setBaseLayer / syncOverlays
```

### JS Interop Pattern

`wwwroot/js/leaflet-interop.js` is the sole bridge between C# and Leaflet. All map mutations go through `window.leafletInterop`:

- `setBaseLayer(urlTemplate, attribution, maxZoom, tileSize, zoomOffset)` — sweeps **all** `L.TileLayer` instances off the map (including the one LeafletForBlazor adds at init), then adds the new one. This is intentional: we own tile layer management after first call.
- `flyTo(lat, lng, zoom)` — Leaflet's `flyTo`
- `syncOverlays(overlays)` — diff-based: removes layers not in the incoming array, adds new `L.imageOverlay` for new ones, updates opacity for existing ones. State is kept in `_overlayLayers` (id → L.ImageOverlay).

C# calls interop via `IJSRuntime.InvokeVoidAsync("leafletInterop.methodName", ...)`.

### LeafletForBlazor v1.2.0 API

The NuGet README documents a different API version. Actual types in v1.2.0:

```csharp
Map.LoadParameters {
    basemap: Map.Basemap {
        basemap_layers: List<Map.BasemapConfigLayer>
    }
}
Map.OnLoadEventParameters  // callback parameter from OnLeafletMapLoaded
```

`RealTimeMap.*` types do **not** exist in 1.2.0.

### CSS Height Chain

The Leaflet map must have a continuous chain of defined heights all the way from `html` down to its container `div`. Every level matters:

```
html(100%) → body(100%) → #app(100%) → .shell(100vh)
  → .map-area(flex:1) → .map-wrapper(100%) → .leaflet-container(100%)
```

`app.css` owns the top of the chain (`html`, `body`, `#app`). `MainLayout.razor.css` owns `.shell` and `.map-area`. `MapComponent.razor.css` owns `.map-wrapper` and uses `::deep .leaflet-container` to pierce Blazor CSS isolation into LeafletForBlazor's DOM.

### CSS Isolation Quirk

`::deep` in a `.razor.css` file compiles to a descendant selector without the scope hash on the leaf element — e.g., `.map-wrapper[b-hash] .leaflet-container`. This is required to reach elements rendered by child components (LeafletForBlazor) that are not in your own template.

### CDN SRI

`integrity=` attributes are intentionally absent from all CDN `<link>` and `<script>` tags in `index.html`. unpkg.com's variable content-encoding causes Chrome to silently reject resources even when the bytes are correct.

### Services

| Service | Scope | Purpose |
|---|---|---|
| `BaseMapService` | Scoped | URL templates & attribution for OSM / CartoDB / Mapbox providers; reads Mapbox token from `IConfiguration` ("Mapbox:Token" in `wwwroot/appsettings.json`) |
| `OverlayService` | Scoped | In-memory list of `MapOverlayModel`; fires `event Action? OnChange` on mutations |
| `TileService` | Scoped | Predefined `TileSourceModel` list (OSM, ESRI); reserved for future offline tile-pack work — not yet consumed by any component |

### Mapbox Token

`wwwroot/appsettings.json` holds `{ "Mapbox": { "Token": "pk.REPLACE_WITH_YOUR_MAPBOX_TOKEN" } }`. `BaseMapService.HasValidMapboxToken` returns false when the token equals that placeholder string, and the sidebar shows a warning badge.

### PWA / Service Worker

`service-worker.js` (dev) is a no-op passthrough. `service-worker.published.js` (Release publish) pre-caches Blazor assets and CDN resources, and uses stale-while-revalidate for map tiles (`map-tiles-v1` cache). The `.csproj` ServiceWorker item maps dev → published at publish time.

## Razor Gotchas (learned in Task 3)

These patterns **break the Razor parser** silently — use the alternatives:

| Broken pattern | Safe alternative |
|---|---|
| `@if (x is { Prop: > 0 } y)` — property pattern with `{}` in condition | `@if (x?.Prop > 0)` + null-check via method |
| `@expr!.Member` — null-forgiving `!` in implicit Razor expression | `@(expr!.Member)` — explicit `@(...)` |
| Helper methods returning `RenderFragment` with lambda `{ }` bodies inside `@code` | Move to `.razor.cs` partial class code-behind |
| `@{ var x = ...; }` inside `@if` body when Razor parser is confused | Move variable to `@code` method |

**Rule of thumb**: Put any C# containing `{ }` in lambdas, switch expressions, or string interpolations into a `.razor.cs` code-behind file. Keep `@code` blocks to lifecycle methods + event handlers only.

## Project Layout

```
DwgMapOverlayPoc/
  Components/Map/
    MapComponent.razor       # Leaflet host; public API: SetBaseMapAsync, FlyToAsync
    MapComponent.razor.css
  Layout/
    MainLayout.razor         # Shell + sidebar controls; holds @ref to MapComponent
    MainLayout.razor.css
  Models/
    BasemapGroup.cs          # Groups light+dark TileSourceModel per provider
    MapOverlayModel.cs       # DWG/image overlay (bounds + opacity + visibility)
    TileSourceModel.cs       # Single tile source; includes MapTheme enum
  Services/
    BaseMapService.cs
    OverlayService.cs
    TileService.cs
  wwwroot/
    appsettings.json         # Mapbox token (gitignore or replace before deploy)
    css/app.css              # Viewport reset — do not remove height/margin rules
    js/leaflet-interop.js    # All Leaflet JS lives here
    tiles/                   # Reserved for offline tile packs
```
