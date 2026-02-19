/**
 * equipment-interop.js
 * Leaflet markers for equipment placement with SVG div-icons and drag support.
 *
 * Requires:
 *   - window.L (Leaflet 1.9)
 *   - window.LeafletBlazorMap
 *
 * Initialise once: equipmentInterop.init(dotNetRef)
 * Then call addMarker, removeMarker, startPlacementMode, etc.
 */

window.equipmentInterop = (() => {

    /** Markers keyed by id string → L.Marker */
    const _markers = {};

    let _dotNetRef        = null;
    let _placementHandler = null;

    function getMap() {
        return window.LeafletBlazorMap ?? null;
    }

    // ── SVG icons ─────────────────────────────────────────────────────────────

    const ICONS = {
        Valve: (snapped) => `
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
              <circle cx="12" cy="12" r="9" fill="${snapped ? '#00c853' : '#1565c0'}"
                      stroke="white" stroke-width="2"/>
              <text x="12" y="16" text-anchor="middle" fill="white"
                    font-size="9" font-family="monospace" font-weight="bold">V</text>
            </svg>`,

        Pump: (snapped) => `
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
              <polygon points="12,3 22,20 2,20"
                       fill="${snapped ? '#00c853' : '#e65100'}"
                       stroke="white" stroke-width="2"/>
              <text x="12" y="19" text-anchor="middle" fill="white"
                    font-size="8" font-family="monospace" font-weight="bold">P</text>
            </svg>`,

        Sensor: (snapped) => `
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
              <rect x="3" y="3" width="18" height="18" rx="3"
                    fill="${snapped ? '#00c853' : '#6a1b9a'}"
                    stroke="white" stroke-width="2"/>
              <text x="12" y="16" text-anchor="middle" fill="white"
                    font-size="9" font-family="monospace" font-weight="bold">S</text>
            </svg>`,

        Custom: (snapped) => `
            <svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">
              <polygon points="12,2 22,12 12,22 2,12"
                       fill="${snapped ? '#00c853' : '#37474f'}"
                       stroke="white" stroke-width="2"/>
              <text x="12" y="16" text-anchor="middle" fill="white"
                    font-size="9" font-family="monospace" font-weight="bold">C</text>
            </svg>`
    };

    function makeIcon(type, isSnapped) {
        const svg = (ICONS[type] ?? ICONS.Custom)(isSnapped);
        return L.divIcon({
            html:        svg,
            className:   'equip-marker-icon',
            iconSize:    [24, 24],
            iconAnchor:  [12, 12],
            popupAnchor: [0, -14]
        });
    }

    // ── Marker management ─────────────────────────────────────────────────────

    /**
     * Store dotNetRef for callbacks. Call once after Blazor initialises the service.
     */
    function init(dotNetRef) {
        _dotNetRef = dotNetRef;
        console.info('[equipmentInterop] initialised');
    }

    function addMarker(id, type, label, lat, lng, isSnapped) {
        const map = getMap();
        if (!map) { console.warn('[equipmentInterop] addMarker: map not ready'); return; }

        if (_markers[id]) {
            map.removeLayer(_markers[id]);
        }

        const marker = L.marker([lat, lng], {
            draggable: true,
            icon:      makeIcon(type, isSnapped),
            title:     label
        });

        marker.bindTooltip(label, { permanent: false, direction: 'top', offset: [0, -14] });

        marker.on('dragend', function (e) {
            const pos = e.target.getLatLng();
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnMarkerDragged', id, pos.lat, pos.lng);
            }
        });

        marker.addTo(map);
        _markers[id] = marker;
    }

    function removeMarker(id) {
        const map = getMap();
        if (_markers[id]) {
            if (map) map.removeLayer(_markers[id]);
            delete _markers[id];
        }
    }

    function clearAllMarkers() {
        const map = getMap();
        for (const [id, marker] of Object.entries(_markers)) {
            if (map) map.removeLayer(marker);
            delete _markers[id];
        }
    }

    // ── Placement mode ────────────────────────────────────────────────────────

    function startPlacementMode() {
        const map = getMap();
        if (!map) { console.warn('[equipmentInterop] startPlacementMode: map not ready'); return; }

        stopPlacementMode();

        _placementHandler = function (e) {
            _placementHandler = null;
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnEquipmentClicked', e.latlng.lat, e.latlng.lng);
            }
        };

        map.once('click', _placementHandler);
        console.info('[equipmentInterop] placement mode active');
    }

    function stopPlacementMode() {
        const map = getMap();
        if (_placementHandler) {
            if (map) map.off('click', _placementHandler);
            _placementHandler = null;
        }
    }

    // ── GeoJSON download ──────────────────────────────────────────────────────

    function downloadGeoJson(json) {
        const blob = new Blob([json], { type: 'application/geo+json' });
        const url  = URL.createObjectURL(blob);
        const a    = document.createElement('a');
        a.href     = url;
        a.download = 'equipment.geojson';
        a.click();
        URL.revokeObjectURL(url);
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        init,
        addMarker,
        removeMarker,
        clearAllMarkers,
        startPlacementMode,
        stopPlacementMode,
        downloadGeoJson
    };
})();
