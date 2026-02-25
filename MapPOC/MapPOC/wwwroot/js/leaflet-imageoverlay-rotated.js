/*
 * Leaflet.ImageOverlay.Rotated
 * Rotate and skew image overlays in Leaflet using three control points.
 * Based on: https://github.com/IvanSanchez/Leaflet.ImageOverlay.Rotated
 * License: "THE BEER-WARE LICENSE" (Revision 42)
 */
(function () {
    if (typeof L === "undefined") return;

    L.ImageOverlay.Rotated = L.ImageOverlay.extend({
        initialize: function (image, topleft, topright, bottomleft, options) {
            if (typeof image === "string") {
                this._rawUrl = image;
            } else {
                this._rawImage = image;
            }
            this._topLeft = L.latLng(topleft);
            this._topRight = L.latLng(topright);
            this._bottomLeft = L.latLng(bottomleft);
            L.setOptions(this, options);
        },

        onAdd: function (map) {
            if (!this._image) {
                this._initImage();
            }
            map.on("viewreset", this._reset, this);
            map.on("zoomanim", this._animateZoom, this);
            if (map.options.zoomAnimation && L.Browser.any3d) {
                map.on("zoomanim", this._animateZoom, this);
            }
            this._reset();
            map.getPanes().overlayPane.appendChild(this._image);
        },

        onRemove: function (map) {
            map.getPanes().overlayPane.removeChild(this._image);
            map.off("viewreset", this._reset, this);
            map.off("zoomanim", this._animateZoom, this);
        },

        _initImage: function () {
            var img = this._rawImage;
            if (!img) {
                img = L.DomUtil.create("img");
                img.src = this._rawUrl;
            }
            L.DomUtil.addClass(img, "leaflet-image-layer");
            img.style.position = "absolute";
            this._image = img;
            img.onload = L.bind(this._reset, this);
        },

        _animateZoom: function (e) {
            this._reset(e);
        },

        setCorners: function (topleft, topright, bottomleft) {
            this._topLeft = L.latLng(topleft);
            this._topRight = L.latLng(topright);
            this._bottomLeft = L.latLng(bottomleft);
            if (this._map) this._reset();
        },

        _reset: function () {
            var map = this._map;
            if (!map) return;
            var img = this._image;
            if (!img || !img.naturalWidth) return;

            var topLeft = map.latLngToLayerPoint(this._topLeft);
            var topRight = map.latLngToLayerPoint(this._topRight);
            var bottomLeft = map.latLngToLayerPoint(this._bottomLeft);

            img.style.width = img.naturalWidth + "px";
            img.style.height = img.naturalHeight + "px";

            var w = img.naturalWidth;
            var h = img.naturalHeight;

            // CSS3 2D transform matrix: matrix(a, b, c, d, tx, ty)
            var a = (topRight.x - topLeft.x) / w;
            var b = (topRight.y - topLeft.y) / w;
            var c = (bottomLeft.x - topLeft.x) / h;
            var d = (bottomLeft.y - topLeft.y) / h;
            var tx = topLeft.x;
            var ty = topLeft.y;

            img.style[L.DomUtil.TRANSFORM] =
                "matrix(" + [a, b, c, d, tx, ty].join(",") + ")";
            img.style[L.DomUtil.TRANSFORM + "Origin"] = "0 0";
        }
    });

    L.imageOverlay.rotated = function (image, topleft, topright, bottomleft, options) {
        return new L.ImageOverlay.Rotated(image, topleft, topright, bottomleft, options);
    };
})();
