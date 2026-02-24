# Map readiness inventory (refactor note)

## Prior acquisition paths (pre-refactor)

- `wwwroot/js/leaflet-hook.js` patched `L.Map.prototype.initialize` and wrote to `window._leafletMapInstance`.
- `wwwroot/js/dotnet-hook.js` attempted to discover a map by patching `DotNet.invokeMethodAsync` calls.
- `wwwroot/js/leaflet-interop.js` polled or queried `window._leafletMapInstance` and container IDs, exposing `waitForMap()` / `notifyMapReady()` and `__setMapInstance()`.
- `wwwroot/js/xyz-interop.js`, `equipment-interop.js`, and `align-interop.js` accessed `window._leafletMapInstance` directly for sync operations.
- `Components/Map/MapComponent.razor` called JS readiness methods from `OnLeafletMapLoaded`.

## Canonical path after refactor

- `wwwroot/js/leaflet-capture.js` uses `L.Map.addInitHook` and calls `leafletInterop.captureMap(...)`.
- `wwwroot/js/leaflet-interop.js` owns the sole map state machine and exposes `whenMapReady()` / `getMapIfReady()`.
- `Components/Map/MapComponent.razor` calls `leafletInterop.onLoadMap()` once to finalize readiness sequencing.
