/**
 * snap-interop.js
 * GeoJSON vertex and edge snapping via Turf.js v6.
 *
 * Requires:
 *   - window.turf (Turf.js v6, loaded before Blazor)
 *
 * Usage:
 *   snapInterop.loadGeoJson(blobUrl)          → Promise<void>
 *   snapInterop.snapNearest(lat, lng, tolDeg) → {lat, lng} | null
 */

window.snapInterop = (() => {

    /** Parsed Turf FeatureCollection, or null if not yet loaded. */
    let _geoJson = null;

    // ── GeoJSON loading ───────────────────────────────────────────────────────

    async function loadGeoJson(blobUrl) {
        try {
            const resp = await fetch(blobUrl);
            const data = await resp.json();
            // Normalise: accept either a FeatureCollection or a bare Feature/geometry
            if (data.type === 'FeatureCollection') {
                _geoJson = data;
            } else if (data.type === 'Feature') {
                _geoJson = turf.featureCollection([data]);
            } else {
                _geoJson = null;
                console.warn('[snapInterop] unexpected GeoJSON type:', data.type);
                return;
            }
            console.info('[snapInterop] GeoJSON loaded,', _geoJson.features.length, 'features');
        } catch (err) {
            _geoJson = null;
            console.error('[snapInterop] loadGeoJson failed:', err);
        }
    }

    // ── Snap query ───────────────────────────────────────────────────────────

    /**
     * Find the nearest GeoJSON vertex or edge point within toleranceDeg degrees.
     * @param {number} lat         Candidate latitude.
     * @param {number} lng         Candidate longitude.
     * @param {number} toleranceDeg  Snap tolerance in decimal degrees.
     * @returns {{ lat: number, lng: number } | null}
     */
    function snapNearest(lat, lng, toleranceDeg) {
        if (!_geoJson || typeof turf === 'undefined') return null;

        const pt = turf.point([lng, lat]);
        let bestDist = Infinity;
        let bestPt   = null;

        for (const feature of _geoJson.features) {
            if (!feature.geometry) continue;

            const geomType = feature.geometry.type;

            // ── Point / MultiPoint: snap to vertex ──────────────────────────
            if (geomType === 'Point' || geomType === 'MultiPoint') {
                const coords = geomType === 'Point'
                    ? [feature.geometry.coordinates]
                    : feature.geometry.coordinates;

                for (const c of coords) {
                    const cPt = turf.point(c);
                    const d   = turf.distance(pt, cPt, { units: 'degrees' });
                    if (d < bestDist) { bestDist = d; bestPt = cPt; }
                }
            }

            // ── LineString / MultiLineString: snap to nearest point on line ──
            if (geomType === 'LineString' || geomType === 'MultiLineString') {
                try {
                    const snapped = turf.nearestPointOnLine(feature, pt, { units: 'degrees' });
                    const d       = snapped.properties.dist;
                    if (d < bestDist) { bestDist = d; bestPt = snapped; }
                } catch { /* turf may throw on degenerate lines */ }
            }

            // ── Polygon / MultiPolygon: snap to ring edges ───────────────────
            if (geomType === 'Polygon' || geomType === 'MultiPolygon') {
                const rings = geomType === 'Polygon'
                    ? feature.geometry.coordinates
                    : feature.geometry.coordinates.flat(1);

                for (const ring of rings) {
                    const line = turf.lineString(ring);
                    try {
                        const snapped = turf.nearestPointOnLine(line, pt, { units: 'degrees' });
                        const d       = snapped.properties.dist;
                        if (d < bestDist) { bestDist = d; bestPt = snapped; }
                    } catch { /* skip degenerate rings */ }
                }
            }
        }

        if (bestPt && bestDist <= toleranceDeg) {
            const [sLng, sLat] = bestPt.geometry.coordinates;
            return { lat: sLat, lng: sLng };
        }
        return null;
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { loadGeoJson, snapNearest };
})();
