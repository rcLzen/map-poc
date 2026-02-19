/**
 * xyz-interop.js
 * Unpacks a gdal2tiles --xyz ZIP into an in-memory tile cache and serves
 * those tiles as a custom Leaflet GridLayer (no HTTP tile server needed).
 *
 * Requires:
 *   - JSZip (loaded before Blazor)
 *   - window.L (Leaflet 1.9)
 *   - window.LeafletBlazorMap
 *
 * ZIP format expected:
 *   {z}/{x}/{y}.png  — standard XYZ / slippy-map tile layout
 *   Generated with:  gdal2tiles.py --xyz --zoom=<min>-<max> input.tif tiles/
 */

'use strict';

window.xyzInterop = (() => {

    /** In-memory tile store: "z/x/y" → object URL */
    const _tileCache = new Map();

    /** Revocable blob URLs for cleanup */
    const _blobUrls = [];

    let _xyzLayer = null;

    function getMap() {
        return window.LeafletBlazorMap ?? null;
    }

    // ── Tile loading ──────────────────────────────────────────────────────────

    /**
     * Fetch the ZIP, decompress every PNG tile, store as Blob URLs in XYZ key order.
     * @param {string} blobUrl  Browser Blob URL of the uploaded ZIP file.
     * @returns {{ tileCount: number, zooms: number[] }}
     */
    async function loadTilesFromZip(blobUrl) {
        // Clear any previous cache and revoke stale Blob URLs
        _blobUrls.forEach(u => URL.revokeObjectURL(u));
        _blobUrls.length = 0;
        _tileCache.clear();

        const resp    = await fetch(blobUrl);
        const ab      = await resp.arrayBuffer();
        const zip     = await JSZip.loadAsync(ab);
        const zoomSet = new Set();

        // Match paths like "14/1234/5678.png" (z/x/y)
        const tileRegex = /^(\d+)\/(\d+)\/(\d+)\.png$/i;

        const promises = [];

        zip.forEach((relativePath, file) => {
            if (file.dir) return;
            const m = tileRegex.exec(relativePath);
            if (!m) return;

            const z = parseInt(m[1], 10);
            const x = parseInt(m[2], 10);
            const y = parseInt(m[3], 10);
            zoomSet.add(z);

            const p = file.async('blob').then(blob => {
                const url = URL.createObjectURL(blob);
                _blobUrls.push(url);
                _tileCache.set(`${z}/${x}/${y}`, url);
            });
            promises.push(p);
        });

        await Promise.all(promises);

        const tileCount = _tileCache.size;
        const zooms     = Array.from(zoomSet).sort((a, b) => a - b);
        console.info(`[xyzInterop] loaded ${tileCount} tiles, zooms: ${zooms.join(', ')}`);
        return { tileCount, zooms };
    }

    // ── Leaflet GridLayer ─────────────────────────────────────────────────────

    function addXyzLayer() {
        const map = getMap();
        if (!map) { console.warn('[xyzInterop] addXyzLayer: map not ready'); return; }
        if (_xyzLayer) { map.removeLayer(_xyzLayer); _xyzLayer = null; }

        _xyzLayer = L.gridLayer({ tileSize: 256, opacity: 1 });

        _xyzLayer.createTile = function (coords) {
            const img = document.createElement('img');
            img.alt   = '';

            // Standard XYZ lookup — Z/X/Y matches gdal2tiles --xyz output directly
            const key = `${coords.z}/${coords.x}/${coords.y}`;
            const url = _tileCache.get(key);
            if (url) {
                img.src = url;
            } else {
                // Transparent placeholder — tile not present in pack
                img.style.opacity = '0';
            }
            return img;
        };

        _xyzLayer.addTo(map);
        console.info('[xyzInterop] XYZ layer added');
    }

    function removeXyzLayer() {
        const map = getMap();
        if (_xyzLayer) {
            if (map) map.removeLayer(_xyzLayer);
            _xyzLayer = null;
            console.info('[xyzInterop] XYZ layer removed');
        }
    }

    function setXyzLayerOpacity(opacity) {
        if (_xyzLayer) {
            _xyzLayer.setOpacity(opacity);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { loadTilesFromZip, addXyzLayer, removeXyzLayer, setXyzLayerOpacity };

})();
