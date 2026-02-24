/**
 * equipment-interop.js
 * Leaflet markers for equipment placement with SVG div-icons and drag support.
 *
 * Requires:
 *   - window.L (Leaflet 1.9)
 *   - window.leafletInterop (leaflet-interop.js — provides whenMapReady)
 *
 * All functions that touch the map await window.leafletInterop.whenMapReady()
 * so calls that arrive during startup queue safely instead of failing.
 *
 * Initialise once: equipmentInterop.init(dotNetRef)
 * Then call addMarker, removeMarker, startPlacementMode, etc.
 */

window.equipmentInterop = (function () {

    /** Markers keyed by id string → L.Marker */
    var _markers = {};

    var _dotNetRef        = null;
    var _placementHandler = null;

    // ── SVG icons ─────────────────────────────────────────────────────────────

    var ICONS = {
        Valve: function (snapped) {
            return '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
              '<circle cx="12" cy="12" r="9" fill="' + (snapped ? '#00c853' : '#1565c0') + '"' +
              ' stroke="white" stroke-width="2"/>' +
              '<text x="12" y="16" text-anchor="middle" fill="white"' +
              ' font-size="9" font-family="monospace" font-weight="bold">V</text></svg>';
        },
        Pump: function (snapped) {
            return '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
              '<polygon points="12,3 22,20 2,20"' +
              ' fill="' + (snapped ? '#00c853' : '#e65100') + '"' +
              ' stroke="white" stroke-width="2"/>' +
              '<text x="12" y="19" text-anchor="middle" fill="white"' +
              ' font-size="8" font-family="monospace" font-weight="bold">P</text></svg>';
        },
        Sensor: function (snapped) {
            return '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
              '<rect x="3" y="3" width="18" height="18" rx="3"' +
              ' fill="' + (snapped ? '#00c853' : '#6a1b9a') + '"' +
              ' stroke="white" stroke-width="2"/>' +
              '<text x="12" y="16" text-anchor="middle" fill="white"' +
              ' font-size="9" font-family="monospace" font-weight="bold">S</text></svg>';
        },
        Custom: function (snapped) {
            return '<svg xmlns="http://www.w3.org/2000/svg" width="24" height="24" viewBox="0 0 24 24">' +
              '<polygon points="12,2 22,12 12,22 2,12"' +
              ' fill="' + (snapped ? '#00c853' : '#37474f') + '"' +
              ' stroke="white" stroke-width="2"/>' +
              '<text x="12" y="16" text-anchor="middle" fill="white"' +
              ' font-size="9" font-family="monospace" font-weight="bold">C</text></svg>';
        }
    };

    function makeIcon(type, isSnapped) {
        var iconFn = ICONS[type] || ICONS.Custom;
        return L.divIcon({
            html:        iconFn(isSnapped),
            className:   'equip-marker-icon',
            iconSize:    [24, 24],
            iconAnchor:  [12, 12],
            popupAnchor: [0, -14]
        });
    }

    // ── Marker management ─────────────────────────────────────────────────────

    /**
     * Store the DotNetObjectReference for drag-end and placement callbacks.
     * Called once after Blazor initialises the service — no map access needed.
     */
    function init(dotNetRef) {
        _dotNetRef = dotNetRef;
        console.info('[Equipment] initialised');
    }

    async function addMarker(id, type, label, lat, lng, isSnapped) {
        var map = await window.leafletInterop.whenMapReady();

        if (_markers[id]) {
            map.removeLayer(_markers[id]);
        }

        var marker = L.marker([lat, lng], {
            draggable: true,
            icon:      makeIcon(type, isSnapped),
            title:     label
        });

        marker.bindTooltip(label, { permanent: false, direction: 'top', offset: [0, -14] });

        marker.on('dragend', function (e) {
            var pos = e.target.getLatLng();
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnMarkerDragged', id, pos.lat, pos.lng);
            }
        });

        marker.addTo(map);
        _markers[id] = marker;
        console.info('[Marker Placed] ' + label + ' at ' + lat.toFixed(5) + ', ' + lng.toFixed(5) +
                     (isSnapped ? ' (snapped)' : ''));
    }

    async function removeMarker(id) {
        var map = await window.leafletInterop.whenMapReady();
        if (_markers[id]) {
            map.removeLayer(_markers[id]);
            delete _markers[id];
            console.info('[Equipment] marker removed: ' + id);
        }
    }

    function clearAllMarkers() {
        // Synchronous: called during dispose / clear-all.
        // Map is always ready by the time the user can trigger this.
        var map = window.leafletInterop.getMapIfReady();
        for (var id in _markers) {
            if (map) map.removeLayer(_markers[id]);
            delete _markers[id];
        }
        console.info('[Equipment] all markers cleared');
    }

    // ── Placement mode ────────────────────────────────────────────────────────

    /**
     * Enable single-click placement mode.  The next click on the map calls
     * _dotNetRef.OnEquipmentClicked(lat, lng) and then disables itself.
     * Awaits map readiness so the button can be clicked immediately after load.
     */
    async function startPlacementMode() {
        var map = await window.leafletInterop.whenMapReady();

        stopPlacementMode();

        console.info('[Equipment] placement mode active — click map to place');

        _placementHandler = function (e) {
            console.info('[Equipment] placement click at ' +
                         e.latlng.lat.toFixed(5) + ', ' + e.latlng.lng.toFixed(5));
            _placementHandler = null;
            if (_dotNetRef) {
                _dotNetRef.invokeMethodAsync('OnEquipmentClicked', e.latlng.lat, e.latlng.lng);
            }
        };

        map.once('click', _placementHandler);
    }

    function stopPlacementMode() {
        if (_placementHandler) {
            var map = window.leafletInterop.getMapIfReady();
            if (map) map.off('click', _placementHandler);
            _placementHandler = null;
        }
    }

    // ── GeoJSON download ──────────────────────────────────────────────────────

    function downloadGeoJson(json) {
        var blob = new Blob([json], { type: 'application/geo+json' });
        var url  = URL.createObjectURL(blob);
        var a    = document.createElement('a');
        a.href     = url;
        a.download = 'equipment.geojson';
        a.click();
        URL.revokeObjectURL(url);
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return {
        init:               init,
        addMarker:          addMarker,
        removeMarker:       removeMarker,
        clearAllMarkers:    clearAllMarkers,
        startPlacementMode: startPlacementMode,
        stopPlacementMode:  stopPlacementMode,
        downloadGeoJson:    downloadGeoJson
    };
})();
