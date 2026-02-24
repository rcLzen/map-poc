/**
 * align-interop.js
 * Manages a Leaflet.ImageOverlay.Rotated overlay for 3-point georeferencing.
 *
 * Requires:
 *   - window.L (Leaflet 1.9)
 *   - Leaflet.ImageOverlay.Rotated (js/Leaflet.ImageOverlay.Rotated.js)
 *   - window.leafletInterop (leaflet-interop.js — provides whenMapReady)
 *
 * All functions that touch the map await window.leafletInterop.whenMapReady()
 * so calls that arrive during startup (e.g. IDB-restored overlay) queue
 * safely instead of failing.
 */

window.alignInterop = (function () {

    var _rotatedOverlay = null;
    var _clickHandler   = null;

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
        var map = await window.leafletInterop.whenMapReady();

        if (_rotatedOverlay) {
            map.removeLayer(_rotatedOverlay);
            _rotatedOverlay = null;
        }

        if (typeof L.imageOverlay.rotated !== 'function') {
            console.error('[Align] L.imageOverlay.rotated is not available — Leaflet.ImageOverlay.Rotated plugin not loaded');
            return;
        }

        _rotatedOverlay = L.imageOverlay.rotated(
            blobUrl,
            L.latLng(tl[0], tl[1]),
            L.latLng(tr[0], tr[1]),
            L.latLng(bl[0], bl[1]),
            { opacity: opacity != null ? opacity : 0.75, interactive: false }
        ).addTo(map);

        console.info('[Align] rotated overlay applied');
    }

    async function clearRotatedOverlay() {
        var map = await window.leafletInterop.whenMapReady();
        if (_rotatedOverlay) {
            map.removeLayer(_rotatedOverlay);
            _rotatedOverlay = null;
            console.info('[Align] rotated overlay removed');
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
        var map = await window.leafletInterop.whenMapReady();

        // Cancel any previous pending capture before registering a new one.
        stopMapClickCapture();

        console.info('[Align Started] waiting for map click (step capture)');

        _clickHandler = function (e) {
            console.info('[Align] map click received:', e.latlng.lat.toFixed(5), e.latlng.lng.toFixed(5));
            _clickHandler = null;
            dotNetRef.invokeMethodAsync(methodName, e.latlng.lat, e.latlng.lng);
        };

        map.once('click', _clickHandler);
    }

    function stopMapClickCapture() {
        if (_clickHandler) {
            var map = window.leafletInterop.getMapIfReady();
            if (map) map.off('click', _clickHandler);
            _clickHandler = null;
            console.info('[Align] click capture cancelled');
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        setRotatedOverlay:        setRotatedOverlay,
        clearRotatedOverlay:      clearRotatedOverlay,
        setRotatedOverlayOpacity: setRotatedOverlayOpacity,
        startMapClickCapture:     startMapClickCapture,
        stopMapClickCapture:      stopMapClickCapture
    };
})();
