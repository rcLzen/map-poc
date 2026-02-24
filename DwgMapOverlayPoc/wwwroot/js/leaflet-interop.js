/**
 * leaflet-interop.js
 * Deterministic Leaflet map readiness and shared map access for Blazor interop.
 */

'use strict';

window.leafletInterop = (() => {

    const State = Object.freeze({
        Uninitialized: 'Uninitialized',
        Capturing:     'Capturing',
        Ready:         'Ready',
        Failed:        'Failed'
    });

    let _state = State.Uninitialized;
    let _map = null;
    let _readyResolve = null;
    let _readyReject = null;
    let _readyPromise = new Promise(function (resolve, reject) {
        _readyResolve = resolve;
        _readyReject  = reject;
    });
    let _lastError = null;
    let _readyAt = null;
    let _captureMethodUsed = null;
    let _debug = true;
    let _startupId = Math.random().toString(36).slice(2, 8);
    let _onLoadMapFired = false;
    let _assertedAfterLoad = false;
    let _readyTimeoutId = null;

    function timestamp() {
        return new Date().toISOString();
    }

    function logInfo(message, data) {
        if (!_debug) return;
        if (data !== undefined) {
            console.info(`[leafletInterop:${_startupId}] ${message}`, data);
            return;
        }
        console.info(`[leafletInterop:${_startupId}] ${message}`);
    }

    function logWarn(message, data) {
        if (!_debug) return;
        if (data !== undefined) {
            console.warn(`[leafletInterop:${_startupId}] ${message}`, data);
            return;
        }
        console.warn(`[leafletInterop:${_startupId}] ${message}`);
    }

    function logError(message, data) {
        if (data !== undefined) {
            console.error(`[leafletInterop:${_startupId}] ${message}`, data);
            return;
        }
        console.error(`[leafletInterop:${_startupId}] ${message}`);
    }

    function setDebug(enabled) {
        _debug = !!enabled;
    }

    function captureMap(map, method) {
        if (!map) {
            logError('captureMap called without a valid Leaflet map instance');
            return;
        }

        if (_state === State.Ready && _map) {
            if (_map !== map) {
                logWarn('captureMap called after readiness with a different instance');
            }
            return;
        }

        _map = map;
        _state = State.Ready;
        _readyAt = timestamp();
        _captureMethodUsed = method || 'unknown';
        if (_readyTimeoutId) {
            clearTimeout(_readyTimeoutId);
            _readyTimeoutId = null;
        }
        logInfo(`Map Ready (${_captureMethodUsed})`, {
            mapId: map?._leaflet_id ?? null,
            containerId: map?._container?.id ?? null
        });
        if (_readyResolve) {
            _readyResolve(_map);
            _readyResolve = null;
            _readyReject = null;
        }
    }

    function failReady(message, details) {
        if (_state === State.Ready || _state === State.Failed) return;
        _state = State.Failed;
        _lastError = { message, details, at: timestamp() };
        logError(message, details);
        if (_readyReject) {
            _readyReject(new Error(message));
            _readyReject = null;
            _readyResolve = null;
        }
    }

    function onLoadMap() {
        _onLoadMapFired = true;
        if (_state === State.Uninitialized) {
            _state = State.Capturing;
        }

        if (_map) return;

        if (!_assertedAfterLoad && window.L && !_map) {
            _assertedAfterLoad = true;
            logError('onLoadMap fired but no map captured yet. Ensure `leaflet-capture.js` is loaded after `leaflet-interop.js` and Leaflet is available.');
        }

        if (_readyTimeoutId) clearTimeout(_readyTimeoutId);
        _readyTimeoutId = setTimeout(function () {
            if (_state === State.Ready) return;
            failReady('Map capture timed out after onLoadMap', diagnose());
        }, 5000);
    }

    function whenMapReady(timeoutMs) {
        if (_state === State.Ready && _map) return Promise.resolve(_map);
        if (_state === State.Failed) return Promise.reject(new Error(_lastError?.message || 'Leaflet map readiness failed'));

        const timeout = typeof timeoutMs === 'number' ? timeoutMs : 5000;
        return Promise.race([
            _readyPromise,
            new Promise((_, reject) =>
                setTimeout(function () {
                    const diag = diagnose();
                    logError('whenMapReady timeout', diag);
                    reject(new Error('Leaflet map never became ready'));
                }, timeout)
            )
        ]);
    }

    function getMapIfReady() {
        return _map;
    }

    function getMapOrThrow() {
        if (!_map) throw new Error('[leafletInterop] Map is not ready.');
        return _map;
    }

    function diagnose() {
        return {
            state: _state,
            hasLeaflet: !!window.L,
            hasMap: !!_map,
            mapId: _map?._leaflet_id ?? null,
            containerId: _map?._container?.id ?? null,
            lastError: _lastError,
            readyAt: _readyAt,
            captureMethodUsed: _captureMethodUsed,
            startupId: _startupId,
            onLoadMapFired: _onLoadMapFired
        };
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
        var map = await whenMapReady();

        // Collect all existing tile layers (LeafletForBlazor's initial layer
        // plus any previously added by us) so we can remove them safely.
        // We collect first, then remove, to avoid mutating the layer list
        // while iterating it.
        var toRemove = [];
        map.eachLayer(function (layer) {
            if (layer instanceof L.TileLayer) toRemove.push(layer);
        });
        toRemove.forEach(function (l) { map.removeLayer(l); });
        _baseLayer = null;

        // Add the new tile layer below any existing overlays (pane: 'tilePane')
        _baseLayer = L.tileLayer(urlTemplate, {
            attribution : attribution || '',
            maxZoom     : maxZoom     || 19,
            tileSize    : tileSize    || 256,
            zoomOffset  : zoomOffset  || 0,
            detectRetina: true
        }).addTo(map);

        logInfo('setBaseLayer OK');
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
        logInfo('flyTo called', { lat: lat, lng: lng, zoom: zoom });
        const map = await whenMapReady();
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
        var map = await whenMapReady();

        var incomingIds = new Set(overlays.map(function (o) { return o.id; }));

        // Remove layers that are no longer in the incoming list
        for (var id in _overlayLayers) {
            if (!incomingIds.has(id)) {
                map.removeLayer(_overlayLayers[id]);
                delete _overlayLayers[id];
            }
        }

        // Add new layers or update opacity of existing ones
        for (var i = 0; i < overlays.length; i++) {
            var overlay = overlays[i];
            var bounds = L.latLngBounds(
                [overlay.southWestLat, overlay.southWestLng],
                [overlay.northEastLat, overlay.northEastLng]
            );

            if (_overlayLayers[overlay.id]) {
                _overlayLayers[overlay.id].setOpacity(overlay.opacity);
            } else {
                var layer = L.imageOverlay(overlay.imageUrl, bounds, {
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
        var map = await whenMapReady();
        for (var key in _overlayLayers) {
            map.removeLayer(_overlayLayers[key]);
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
        return _map ? _map.getZoom() : 15;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        setDebug:         setDebug,
        captureMap:       captureMap,
        onLoadMap:        onLoadMap,
        whenMapReady:     whenMapReady,
        getMapIfReady:    getMapIfReady,
        getMapOrThrow:    getMapOrThrow,
        diagnose:         diagnose,
        setBaseLayer:     setBaseLayer,
        flyTo:            flyTo,
        syncOverlays:     syncOverlays,
        clearAllOverlays: clearAllOverlays,
        getZoom:          getZoom
    };
})();
