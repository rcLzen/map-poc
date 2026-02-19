/**
 * align-interop.js
 * Manages a Leaflet.ImageOverlay.Rotated overlay for 3-point georeferencing.
 *
 * Requires:
 *   - window.L (Leaflet 1.9)
 *   - Leaflet.ImageOverlay.Rotated (js/Leaflet.ImageOverlay.Rotated.js)
 *   - window.leafletInterop (leaflet-interop.js — provides waitForMap)
 *   - window.LeafletBlazorMap (set by LeafletForBlazor after map init)
 *
 * All functions that touch the map await window.leafletInterop.waitForMap()
 * so calls that arrive during startup (e.g. IDB-restored overlay) queue
 * safely instead of failing with "map not ready yet".
 */

window.alignInterop = (() => {

    let _rotatedOverlay = null;
    let _clickHandler   = null;

    // ── Overlay management ───────────────────────────────────────────────────

    /**
     * Replace (or add) the rotated image overlay.
     * @param {string}   blobUrl  Browser Blob URL for the PNG.
     * @param {number[]} tl       [lat, lng] of the image top-left corner.
     * @param {number[]} tr       [lat, lng] of the image top-right corner.
     * @param {number[]} bl       [lat, lng] of the image bottom-left corner.
     * @param {number}   opacity  0–1 opacity value.
     */
    async function setRotatedOverlay(blobUrl, tl, tr, bl, opacity) {
        const map = await window.leafletInterop.waitForMap();

        if (_rotatedOverlay) {
            map.removeLayer(_rotatedOverlay);
            _rotatedOverlay = null;
        }

        _rotatedOverlay = L.imageOverlay.rotated(
            blobUrl,
            L.latLng(tl[0], tl[1]),
            L.latLng(tr[0], tr[1]),
            L.latLng(bl[0], bl[1]),
            { opacity: opacity ?? 0.75, interactive: false }
        ).addTo(map);

        console.info('[alignInterop] rotated overlay added');
    }

    async function clearRotatedOverlay() {
        const map = await window.leafletInterop.waitForMap();
        if (_rotatedOverlay) {
            map.removeLayer(_rotatedOverlay);
            _rotatedOverlay = null;
            console.info('[alignInterop] rotated overlay removed');
        }
    }

    function setRotatedOverlayOpacity(opacity) {
        if (_rotatedOverlay) {
            _rotatedOverlay.setOpacity(opacity);
        }
    }

    // ── Map click capture ─────────────────────────────────────────────────────

    /**
     * Attach a one-time map click listener.
     * On click, calls dotNetRef.invokeMethodAsync(methodName, lat, lng).
     * Awaits map readiness so the button can be clicked immediately on load
     * without hitting the startup race condition.
     */
    async function startMapClickCapture(dotNetRef, methodName) {
        const map = await window.leafletInterop.waitForMap();

        // Cancel any previous pending capture before registering a new one.
        stopMapClickCapture();

        _clickHandler = function (e) {
            _clickHandler = null;
            dotNetRef.invokeMethodAsync(methodName, e.latlng.lat, e.latlng.lng);
        };

        map.once('click', _clickHandler);
        console.info('[alignInterop] waiting for map click…');
    }

    function stopMapClickCapture() {
        // Synchronous — only detaches from the map if we already have a ref.
        const map = window.LeafletBlazorMap;
        if (_clickHandler) {
            if (map) map.off('click', _clickHandler);
            _clickHandler = null;
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        setRotatedOverlay,
        clearRotatedOverlay,
        setRotatedOverlayOpacity,
        startMapClickCapture,
        stopMapClickCapture
    };
})();
