/**
 * MapPOC - Leaflet JS Interop
 * Provides interop functions for the Blazor MapComponent.
 */
window.MapInterop = {
    _map: null,
    _baseLayer: null,
    _markers: {},
    _imageOverlay: null,
    _geoJsonLayer: null,
    _xyzLayer: null,

    /**
     * Initialize the Leaflet map in the given container element.
     */
    initMap: function (elementId, lat, lng, zoom) {
        if (this._map) {
            this._map.remove();
        }

        this._map = L.map(elementId, {
            center: [lat, lng],
            zoom: zoom,
            zoomControl: true,
            attributionControl: true
        });

        this._setBaseLayer(
            "https://{s}.tile.openstreetmap.org/{z}/{x}/{y}.png",
            "&copy; <a href='https://www.openstreetmap.org/copyright'>OpenStreetMap</a> contributors",
            19
        );

        return true;
    },

    /**
     * Set or switch the base tile layer.
     */
    setBaseLayer: function (urlTemplate, attribution, maxZoom) {
        this._setBaseLayer(urlTemplate, attribution, maxZoom);
    },

    _setBaseLayer: function (urlTemplate, attribution, maxZoom) {
        if (this._baseLayer) {
            this._map.removeLayer(this._baseLayer);
        }
        this._baseLayer = L.tileLayer(urlTemplate, {
            maxZoom: maxZoom,
            attribution: attribution
        }).addTo(this._map);
    },

    /**
     * Add a marker at the given lat/lng.
     */
    addMarker: function (id, lat, lng, popupContent) {
        if (!this._map) return;
        var marker = L.marker([lat, lng]).addTo(this._map);
        if (popupContent) {
            marker.bindPopup(popupContent);
        }
        this._markers[id] = marker;
    },

    /**
     * Remove a marker by id.
     */
    removeMarker: function (id) {
        if (this._markers[id]) {
            this._map.removeLayer(this._markers[id]);
            delete this._markers[id];
        }
    },

    /**
     * Clear all markers.
     */
    clearMarkers: function () {
        for (var id in this._markers) {
            this._map.removeLayer(this._markers[id]);
        }
        this._markers = {};
    },

    /**
     * Add a rotated image overlay using three control points.
     */
    addRotatedImageOverlay: function (imageUrl, topLeft, topRight, bottomLeft, opacity) {
        if (!this._map) return;
        this.removeImageOverlay();

        this._imageOverlay = L.imageOverlay.rotated(
            imageUrl,
            L.latLng(topLeft[0], topLeft[1]),
            L.latLng(topRight[0], topRight[1]),
            L.latLng(bottomLeft[0], bottomLeft[1]),
            { opacity: opacity || 0.7, interactive: false }
        ).addTo(this._map);
    },

    /**
     * Remove the rotated image overlay.
     */
    removeImageOverlay: function () {
        if (this._imageOverlay && this._map) {
            this._map.removeLayer(this._imageOverlay);
            this._imageOverlay = null;
        }
    },

    /**
     * Load GeoJSON data onto the map.
     */
    loadGeoJson: function (geoJsonString) {
        if (!this._map) return;
        this.clearGeoJson();

        try {
            var data = JSON.parse(geoJsonString);
            this._geoJsonLayer = L.geoJSON(data, {
                style: function () {
                    return { color: "#3388ff", weight: 2, opacity: 0.8 };
                }
            }).addTo(this._map);

            this._map.fitBounds(this._geoJsonLayer.getBounds(), { padding: [20, 20] });
        } catch (e) {
            console.error("Failed to load GeoJSON:", e);
        }
    },

    /**
     * Clear GeoJSON layer.
     */
    clearGeoJson: function () {
        if (this._geoJsonLayer && this._map) {
            this._map.removeLayer(this._geoJsonLayer);
            this._geoJsonLayer = null;
        }
    },

    /**
     * Add XYZ tile layer.
     */
    addXyzTileLayer: function (urlTemplate, attribution, maxZoom) {
        if (!this._map) return;
        this.removeXyzTileLayer();

        this._xyzLayer = L.tileLayer(urlTemplate, {
            maxZoom: maxZoom || 22,
            attribution: attribution || ""
        }).addTo(this._map);
    },

    /**
     * Remove XYZ tile layer.
     */
    removeXyzTileLayer: function () {
        if (this._xyzLayer && this._map) {
            this._map.removeLayer(this._xyzLayer);
            this._xyzLayer = null;
        }
    },

    /**
     * Fly/pan the map to a location.
     */
    flyTo: function (lat, lng, zoom) {
        if (this._map) {
            this._map.flyTo([lat, lng], zoom);
        }
    },

    /**
     * Invalidate map size (call after container resize).
     */
    invalidateSize: function () {
        if (this._map) {
            this._map.invalidateSize();
        }
    },

    /**
     * Register a click handler that calls back into Blazor.
     */
    registerClickHandler: function (dotNetRef) {
        if (!this._map) return;
        this._map.on("click", function (e) {
            dotNetRef.invokeMethodAsync("OnMapClicked", e.latlng.lat, e.latlng.lng);
        });
    },

    /**
     * Dispose/cleanup.
     */
    dispose: function () {
        if (this._map) {
            this._map.remove();
            this._map = null;
        }
        this._markers = {};
        this._baseLayer = null;
        this._imageOverlay = null;
        this._geoJsonLayer = null;
        this._xyzLayer = null;
    }
};
