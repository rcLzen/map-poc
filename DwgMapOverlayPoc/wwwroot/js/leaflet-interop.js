/**
 * leaflet-interop.js
 * Bridge between Blazor C# and the Leaflet map instance managed by
 * LeafletForBlazor. All public functions live under `window.leafletInterop`.
 *
 * LeafletForBlazor exposes its Leaflet map object on `window.LeafletBlazorMap`
 * after the onLoadMap event fires in C#. We only call map methods once that
 * variable exists.
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

    // ── State ────────────────────────────────────────────────────────────────
    /** The currently active base tile layer, or null before first swap. */
    let _baseLayer = null;

    /** Active ImageOverlay instances keyed by overlay-id string. */
    const _overlayLayers = {};

    // ── Helpers ──────────────────────────────────────────────────────────────

    /**
     * Returns the Leaflet map instance created by LeafletForBlazor,
     * or null if it has not yet been initialised.
     */
    function getMap() {
        return window.LeafletBlazorMap ?? null;
    }

    // ── Base-layer management ─────────────────────────────────────────────────

    /**
     * Replaces the current base tile layer with a new one.
     *
     * Called by MapComponent.SetBaseMapAsync after the map is ready.
     * Also called from OnMapReady to apply the initial default layer.
     *
     * Expected UI result after each call
     * ────────────────────────────────────
     *  OSM Light   → colourful street map, familiar OpenStreetMap colours
     *  OSM Dark    → CartoDB Dark Matter – charcoal background, white streets
     *  Carto Light → CartoDB Positron – very pale grey, minimal labels;
     *                best backdrop for DWG drawing overlays
     *  Carto Dark  → CartoDB Dark Matter (same as OSM dark)
     *  Mapbox Light → polished Mapbox Streets v12 with rich POI icons
     *  Mapbox Dark  → Mapbox Dark v11 – deep navy, amber road labels
     *
     * @param {string}  urlTemplate  Leaflet tile URL template ({z}/{x}/{y})
     * @param {string}  attribution  HTML attribution string
     * @param {number}  maxZoom      Maximum native zoom level (e.g. 19 or 22)
     * @param {number}  tileSize     Tile pixel size – 256 for most providers
     * @param {number}  zoomOffset   Zoom offset – 0 for 256 px tiles
     */
    function setBaseLayer(urlTemplate, attribution, maxZoom, tileSize, zoomOffset) {
        const map = getMap();
        if (!map) {
            console.warn('[leafletInterop] setBaseLayer: map not ready yet');
            return;
        }

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
     * Uses Leaflet's built-in flyTo animation (1 second duration).
     *
     * @param {number} lat
     * @param {number} lng
     * @param {number} zoom
     */
    function flyTo(lat, lng, zoom) {
        const map = getMap();
        if (!map) { console.warn('[leafletInterop] flyTo: map not ready'); return; }
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
    function syncOverlays(overlays) {
        const map = getMap();
        if (!map) { console.warn('[leafletInterop] syncOverlays: map not ready'); return; }

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
    function clearAllOverlays() {
        const map = getMap();
        for (const layer of Object.values(_overlayLayers)) {
            if (map) map.removeLayer(layer);
        }
        for (const key of Object.keys(_overlayLayers)) {
            delete _overlayLayers[key];
        }
    }

    // ── Current zoom ──────────────────────────────────────────────────────────

    /**
     * Returns the current Leaflet map zoom level.
     * Used by EquipmentService to convert pixel snap radius → degree tolerance.
     */
    function getZoom() {
        const map = getMap();
        return map ? map.getZoom() : 15;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { setBaseLayer, flyTo, syncOverlays, clearAllOverlays, getZoom };
})();
