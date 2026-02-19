/**
 * leaflet-interop.js
 * Bridge between Blazor C# and the Leaflet map instance managed by
 * LeafletForBlazor. All public functions live under `window.leafletInterop`.
 *
 * Map-readiness guard
 * ───────────────────
 * LeafletForBlazor sets `window.LeafletBlazorMap` around the same time it
 * fires the C# `onLoadMap` callback — but the exact ordering is not
 * guaranteed.  Every function that needs the map calls `waitForMap()`, which:
 *
 *   • Fast path  (zero overhead once the map is up): if `window.LeafletBlazorMap`
 *     is already set, returns `Promise.resolve(map)` synchronously.
 *   • Slow path  (startup only, milliseconds): polls at 20 ms intervals until
 *     the reference is set, then resolves.  Times out after 6 s with a
 *     rejected Promise (surfaced as a JS console error).
 *
 * Polling is used deliberately instead of a one-shot signal/Promise because
 * the signal approach would resolve with `undefined` if called before
 * LeafletForBlazor assigns `window.LeafletBlazorMap`, producing silent
 * TypeErrors on all subsequent map operations.
 *
 * Other interop modules (align-interop, equipment-interop) call
 * `window.leafletInterop.waitForMap()` for the same guard.
 *
 * Tile-layer ownership strategy
 * ─────────────────────────────
 * LeafletForBlazor adds whatever is in Map.LoadParameters.basemap.basemap_layers
 * during initialisation. Rather than fighting that, setBaseLayer() performs a
 * clean sweep: it removes every L.TileLayer currently on the map (both from
 * LeafletForBlazor and any previous call to setBaseLayer) before adding the
 * new one. L.ImageOverlay instances (DWG overlays) are unaffected because
 * they are NOT L.TileLayer instances.
 */

window.leafletInterop = (() => {

    // ── Map-readiness polling ─────────────────────────────────────────────────

    /**
     * Returns a Promise<L.Map> that resolves once `window.LeafletBlazorMap`
     * is set by LeafletForBlazor.
     *
     * Fast path: if the map reference is already set the Promise resolves in
     * the current microtask (no polling started).
     *
     * Slow path: checks every 20 ms, gives up after 300 attempts (~6 s).
     *
     * @returns {Promise<L.Map>}
     */
    function waitForMap() {
        if (window.LeafletBlazorMap) {
            return Promise.resolve(window.LeafletBlazorMap);
        }

        return new Promise(function (resolve, reject) {
            var attempts = 0;
            var id = setInterval(function () {
                if (window.LeafletBlazorMap) {
                    clearInterval(id);
                    console.info('[leafletInterop] map ready after', attempts * 20, 'ms');
                    resolve(window.LeafletBlazorMap);
                } else if (++attempts > 300) {          // 6 s timeout
                    clearInterval(id);
                    reject(new Error('[leafletInterop] Timed out waiting for Leaflet map (>6 s)'));
                }
            }, 20);
        });
    }

    // ── State ────────────────────────────────────────────────────────────────

    /** The currently active base tile layer, or null before first swap. */
    let _baseLayer = null;

    /** Active ImageOverlay instances keyed by overlay-id string. */
    const _overlayLayers = {};

    // ── Base-layer management ─────────────────────────────────────────────────

    /**
     * Replaces the current base tile layer with a new one.
     *
     * Called by MapComponent.SetBaseMapAsync after the map is ready.
     * Also called from OnLeafletMapLoaded to apply the initial default layer.
     *
     * @param {string}  urlTemplate  Leaflet tile URL template ({z}/{x}/{y})
     * @param {string}  attribution  HTML attribution string
     * @param {number}  maxZoom      Maximum native zoom level (e.g. 19 or 22)
     * @param {number}  tileSize     Tile pixel size – 256 for most providers
     * @param {number}  zoomOffset   Zoom offset – 0 for 256 px tiles
     */
    async function setBaseLayer(urlTemplate, attribution, maxZoom, tileSize, zoomOffset) {
        const map = await waitForMap();

        // Collect all existing tile layers (LeafletForBlazor's initial layer
        // plus any previously added by us) so we can remove them safely.
        // We collect first, then remove, to avoid mutating the layer list
        // while iterating it.
        const toRemove = [];
        map.eachLayer(layer => {
            if (layer instanceof L.TileLayer) toRemove.push(layer);
        });
        toRemove.forEach(l => map.removeLayer(l));
        _baseLayer = null;

        // Add the new tile layer below any existing overlays (pane: 'tilePane')
        _baseLayer = L.tileLayer(urlTemplate, {
            attribution : attribution || '',
            maxZoom     : maxZoom     || 19,
            tileSize    : tileSize    || 256,
            zoomOffset  : zoomOffset  || 0,
            detectRetina: true
        }).addTo(map);

        console.info('[leafletInterop] setBaseLayer →', urlTemplate.split('/').slice(0, 6).join('/'));
    }

    // ── Map navigation ────────────────────────────────────────────────────────

    /**
     * Flies the map to the given coordinates at the specified zoom level.
     *
     * @param {number} lat
     * @param {number} lng
     * @param {number} zoom
     */
    async function flyTo(lat, lng, zoom) {
        const map = await waitForMap();
        map.flyTo([lat, lng], zoom, { animate: true, duration: 1 });
    }

    // ── DWG image-overlay management ─────────────────────────────────────────

    /**
     * Synchronises the set of Leaflet ImageOverlay layers on the map to exactly
     * match the provided array of overlay descriptors.
     *
     * Layers no longer present in the array are removed; new ones are added.
     * Existing layers whose id is still present have their opacity updated.
     *
     * @param {Array<{
     *   id:           string,
     *   imageUrl:     string,
     *   southWestLat: number, southWestLng: number,
     *   northEastLat: number, northEastLng: number,
     *   opacity:      number
     * }>} overlays
     */
    async function syncOverlays(overlays) {
        const map = await waitForMap();

        const incomingIds = new Set(overlays.map(o => o.id));

        // Remove layers that are no longer in the incoming list
        for (const [id, layer] of Object.entries(_overlayLayers)) {
            if (!incomingIds.has(id)) {
                map.removeLayer(layer);
                delete _overlayLayers[id];
            }
        }

        // Add new layers or update opacity of existing ones
        for (const overlay of overlays) {
            const bounds = L.latLngBounds(
                [overlay.southWestLat, overlay.southWestLng],
                [overlay.northEastLat, overlay.northEastLng]
            );

            if (_overlayLayers[overlay.id]) {
                _overlayLayers[overlay.id].setOpacity(overlay.opacity);
            } else {
                const layer = L.imageOverlay(overlay.imageUrl, bounds, {
                    opacity    : overlay.opacity,
                    interactive: false,
                    crossOrigin: true
                });
                layer.addTo(map);
                _overlayLayers[overlay.id] = layer;
            }
        }
    }

    /**
     * Removes all image overlays from the map and clears the internal registry.
     */
    async function clearAllOverlays() {
        const map = await waitForMap();
        for (const layer of Object.values(_overlayLayers)) {
            map.removeLayer(layer);
        }
        for (const key of Object.keys(_overlayLayers)) {
            delete _overlayLayers[key];
        }
    }

    // ── Current zoom ──────────────────────────────────────────────────────────

    /**
     * Returns the current Leaflet map zoom level.
     * Only called from user interactions (snapping), so the map is always
     * ready; no await needed.
     */
    function getZoom() {
        const map = window.LeafletBlazorMap;
        return map ? map.getZoom() : 15;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { waitForMap, setBaseLayer, flyTo, syncOverlays, clearAllOverlays, getZoom };
})();
