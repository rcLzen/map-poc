/**
 * map-export-interop.js
 * JS side of MapExportPanel.  Provides:
 *   • captureMapPng  — html2canvas screenshot of the Leaflet container
 *   • downloadPng    — trigger browser download of a PNG data URL
 *   • downloadGeoJson — trigger browser download of a GeoJSON string
 *
 * All functions live under window.mapExportInterop.
 *
 * Notes
 * ─────
 * • html2canvas cannot capture tiles served from cross-origin domains (OSM,
 *   CartoDB, Mapbox) unless those servers set permissive CORS headers.
 *   Tiles loaded from the in-memory ZIP cache (blob: URLs) ARE captured.
 * • The rotated PNG overlay (blob: URL) is also captured correctly.
 * • Cross-origin tiles appear as blank squares — this is expected browser
 *   security behaviour, not a bug.
 */

'use strict';

window.mapExportInterop = (() => {

    // ── Internal helpers ──────────────────────────────────────────────────────

    /**
     * Triggers a synthetic <a> click to download content.
     * @param {string} href      URL or data URL
     * @param {string} filename  Suggested file name
     */
    function triggerDownload(href, filename) {
        const a = document.createElement('a');
        a.href     = href;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        document.body.removeChild(a);
    }

    // ── Public API ────────────────────────────────────────────────────────────

    return {

        /**
         * Captures the element matching `selector` as a PNG using html2canvas.
         *
         * @param {string} selector  CSS selector for the map container
         *                           (e.g. ".leaflet-container")
         * @returns {Promise<string>}  PNG data URL  (image/png)
         */
        async captureMapPng(selector) {
            if (typeof html2canvas === 'undefined') {
                throw new Error('[mapExportInterop] html2canvas is not loaded on the page.');
            }

            const el = document.querySelector(selector);
            if (!el) {
                throw new Error(`[mapExportInterop] Element not found: "${selector}"`);
            }

            const canvas = await html2canvas(el, {
                useCORS:    true,   // attempt to draw cross-origin tiles (may be blocked)
                allowTaint: true,   // keep rendering even if cross-origin tiles taint canvas
                logging:    false
            });

            return canvas.toDataURL('image/png');
        },

        /**
         * Triggers a browser download of a PNG data URL.
         *
         * @param {string} dataUrl   image/png data URL returned by captureMapPng
         * @param {string} filename  Suggested file name (default: map-export.png)
         */
        downloadPng(dataUrl, filename) {
            triggerDownload(dataUrl, filename || 'map-export.png');
        },

        /**
         * Serialises a GeoJSON string to a Blob and triggers a download.
         *
         * @param {string} json      GeoJSON text
         * @param {string} filename  Suggested file name (default: export.geojson)
         */
        downloadGeoJson(json, filename) {
            const blob = new Blob([json], { type: 'application/geo+json' });
            const url  = URL.createObjectURL(blob);
            triggerDownload(url, filename || 'export.geojson');
            // Revoke after the browser has had a chance to initiate the download
            setTimeout(() => URL.revokeObjectURL(url), 10_000);
        }
    };

})();
