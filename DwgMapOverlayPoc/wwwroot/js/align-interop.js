/**
 * align-interop.js
 * Manages a Leaflet.ImageOverlay.Rotated overlay for 3-point georeferencing.
 *
 * Requires:
 *   - window.L (Leaflet 1.9)
 *   - Leaflet.ImageOverlay.Rotated (loaded before Blazor)
 *   - window.LeafletBlazorMap (set by LeafletForBlazor after map init)
 */

window.alignInterop = (() => {

    let _rotatedOverlay = null;
    let _clickHandler   = null;

    function getMap() {
        return window.LeafletBlazorMap ?? null;
    }

    // ── Overlay management ───────────────────────────────────────────────────

    /**
     * Replace (or add) the rotated image overlay.
     * @param {string} blobUrl   Browser Blob URL for the PNG.
     * @param {number[]} tl      [lat, lng] of the image top-left corner.
     * @param {number[]} tr      [lat, lng] of the image top-right corner.
     * @param {number[]} bl      [lat, lng] of the image bottom-left corner.
     * @param {number}   opacity 0–1 opacity value.
     */
    function setRotatedOverlay(blobUrl, tl, tr, bl, opacity) {
        const map = getMap();
        if (!map) { console.warn('[alignInterop] setRotatedOverlay: map not ready'); return; }

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

    function clearRotatedOverlay() {
        const map = getMap();
        if (_rotatedOverlay) {
            if (map) map.removeLayer(_rotatedOverlay);
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
     */
    function startMapClickCapture(dotNetRef, methodName) {
        const map = getMap();
        if (!map) { console.warn('[alignInterop] startMapClickCapture: map not ready'); return; }

        stopMapClickCapture();

        _clickHandler = function (e) {
            _clickHandler = null;
            dotNetRef.invokeMethodAsync(methodName, e.latlng.lat, e.latlng.lng);
        };

        map.once('click', _clickHandler);
        console.info('[alignInterop] waiting for map click…');
    }

    function stopMapClickCapture() {
        const map = getMap();
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
