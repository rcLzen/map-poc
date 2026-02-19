/**
 * tms-interop.js
 * Unpacks a gdal2tiles-style ZIP into an in-memory tile cache and serves
 * those tiles as a custom Leaflet GridLayer (no HTTP tile server needed).
 *
 * Requires:
 *   - JSZip (loaded before Blazor)
 *   - window.L (Leaflet 1.9)
 *   - window.LeafletBlazorMap
 *
 * ZIP format expected:
 *   {z}/{x}/{y}.png  (XYZ) or TMS-style Y inverted — both are handled.
 */

window.tmsInterop = (() => {

    /** In-memory tile store: "z/x/y" (XYZ) → object URL */
    const _tileCache = new Map();

    /** Revocable blob URLs for cleanup */
    const _blobUrls = [];

    let _tmsLayer = null;

    function getMap() {
        return window.LeafletBlazorMap ?? null;
    }

    // ── Tile loading ──────────────────────────────────────────────────────────

    /**
     * Fetch the ZIP, decompress every PNG tile, store as Blob URLs.
     * @param {string} blobUrl Browser Blob URL of the ZIP file.
     * @returns {{ tileCount: number, zooms: number[] }}
     */
    async function loadTilesFromZip(blobUrl) {
        // Clear any previous cache
        _blobUrls.forEach(u => URL.revokeObjectURL(u));
        _blobUrls.length = 0;
        _tileCache.clear();

        const resp   = await fetch(blobUrl);
        const ab     = await resp.arrayBuffer();
        const zip    = await JSZip.loadAsync(ab);
        const zoomSet = new Set();

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
        console.info(`[tmsInterop] loaded ${tileCount} tiles, zooms: ${zooms.join(', ')}`);
        return { tileCount, zooms };
    }

    // ── Leaflet GridLayer ─────────────────────────────────────────────────────

    function addTmsLayer() {
        const map = getMap();
        if (!map) { console.warn('[tmsInterop] addTmsLayer: map not ready'); return; }
        if (_tmsLayer) { map.removeLayer(_tmsLayer); _tmsLayer = null; }

        _tmsLayer = L.gridLayer({ tileSize: 256, opacity: 1 });

        _tmsLayer.createTile = function (coords) {
            const img = document.createElement('img');
            img.alt   = '';

            // Try XYZ key first, then TMS-inverted Y
            const xyzKey  = `${coords.z}/${coords.x}/${coords.y}`;
            const tmsY    = (1 << coords.z) - 1 - coords.y;
            const tmsKey  = `${coords.z}/${coords.x}/${tmsY}`;

            const url = _tileCache.get(xyzKey) ?? _tileCache.get(tmsKey) ?? '';
            if (url) {
                img.src = url;
            } else {
                // Transparent placeholder — tile not in pack
                img.style.opacity = '0';
            }
            return img;
        };

        _tmsLayer.addTo(map);
        console.info('[tmsInterop] TMS layer added');
    }

    function removeTmsLayer() {
        const map = getMap();
        if (_tmsLayer) {
            if (map) map.removeLayer(_tmsLayer);
            _tmsLayer = null;
            console.info('[tmsInterop] TMS layer removed');
        }
    }

    function setTmsLayerOpacity(opacity) {
        if (_tmsLayer) {
            _tmsLayer.setOpacity(opacity);
        }
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { loadTilesFromZip, addTmsLayer, removeTmsLayer, setTmsLayerOpacity };
})();
