/*
 * Leaflet.ImageOverlay.Rotated v0.2 — bundled locally to avoid CDN 404.
 *
 * Extends L.ImageOverlay to support arbitrary rotation and skew defined by
 * three corner LatLngs (topLeft, topRight, bottomLeft).  A 2D affine CSS
 * matrix transform is recomputed on every zoom / viewreset event so the
 * image stays pinned to the correct world coordinates at all zoom levels.
 *
 * API:
 *   L.imageOverlay.rotated(url, topLeft, topRight, bottomLeft, options)
 *
 * Options (same as L.ImageOverlay plus):
 *   opacity     {number}  0–1 (default 1)
 *   interactive {boolean} pass mouse events through (default false)
 *
 * MIT License — Iván Sánchez Ortega
 * https://github.com/IvanSanchez/Leaflet.ImageOverlay.Rotated
 */
(function (factory, window) {
    if (typeof define === 'function' && define.amd) {
        define(['leaflet'], factory);
    } else if (typeof exports === 'object') {
        module.exports = factory(require('leaflet'));
    }
    if (typeof window !== 'undefined' && window.L) {
        factory(window.L);
    }
}(function (L) {

    L.ImageOverlay.Rotated = L.ImageOverlay.extend({

        // ── Constructor ───────────────────────────────────────────────────────

        initialize: function (image, topleft, topright, bottomleft, options) {
            if (typeof image === 'string') {
                this._url = image;
            } else {
                // Accept an HTMLImageElement or HTMLCanvasElement directly.
                this._rawImage = image;
            }

            this._topLeft    = L.latLng(topleft);
            this._topRight   = L.latLng(topright);
            this._bottomLeft = L.latLng(bottomleft);

            L.setOptions(this, options);
        },

        // ── Layer lifecycle ───────────────────────────────────────────────────

        onAdd: function (map) {
            if (!this._rawImage) {
                // Create the <img> element the first time we are added to a map.
                this._rawImage       = document.createElement('img');
                this._rawImage.alt   = '';
                this._rawImage.style.position = 'absolute';

                // Re-run _reset once the image's natural dimensions are known.
                this._rawImage.addEventListener('load', this._reset.bind(this));
                this._rawImage.src = this._url;
            }

            L.DomUtil.setOpacity(
                this._rawImage,
                this.options.opacity !== undefined ? this.options.opacity : 1
            );

            if (this.options.interactive) {
                L.DomUtil.addClass(this._rawImage, 'leaflet-interactive');
                this.addInteractiveTarget(this._rawImage);
            }

            map.getPanes().overlayPane.appendChild(this._rawImage);

            // Recompute the transform whenever Leaflet changes the coordinate
            // origin (zoom step or viewreset at end of zoom).
            map.on('zoom viewreset', this._reset, this);

            // Initial position (may be a no-op if image not yet loaded).
            this._reset();
        },

        onRemove: function (map) {
            map.off('zoom viewreset', this._reset, this);

            if (this.options.interactive) {
                this.removeInteractiveTarget(this._rawImage);
            }

            L.DomUtil.remove(this._rawImage);
        },

        // ── Public mutators ───────────────────────────────────────────────────

        setOpacity: function (opacity) {
            this.options.opacity = opacity;
            if (this._rawImage) {
                L.DomUtil.setOpacity(this._rawImage, opacity);
            }
            return this;
        },

        setUrl: function (url) {
            this._url = url;
            if (this._rawImage) {
                this._rawImage.src = url;
            }
            return this;
        },

        /**
         * Move the overlay to new corner positions without removing and
         * re-adding the layer.
         */
        setCorners: function (topleft, topright, bottomleft) {
            this._topLeft    = L.latLng(topleft);
            this._topRight   = L.latLng(topright);
            this._bottomLeft = L.latLng(bottomleft);
            if (this._map) { this._reset(); }
            return this;
        },

        // ── Internal transform ────────────────────────────────────────────────

        /**
         * Recompute the CSS affine matrix that maps image pixels to the
         * overlay-pane layer coordinate system.
         *
         * The overlay pane is repositioned by Leaflet during panning (via a
         * CSS translate on the pane element itself), so we only need to
         * recalculate when zoom changes the pixel-per-degree scale.
         *
         * Math: CSS matrix(a,b,c,d,e,f) applies
         *   x' = a·px + c·py + e
         *   y' = b·px + d·py + f
         *
         * Pinning three image corners to three layer points gives:
         *   (0,0) → tl  ⟹  e = tl.x,  f = tl.y
         *   (w,0) → tr  ⟹  a = (tr.x − tl.x) / w,  b = (tr.y − tl.y) / w
         *   (0,h) → bl  ⟹  c = (bl.x − tl.x) / h,  d = (bl.y − tl.y) / h
         */
        _reset: function () {
            var map = this._map;
            if (!map) { return; }

            var img = this._rawImage;
            var w   = img.naturalWidth  || img.width;
            var h   = img.naturalHeight || img.height;

            // Guard: dimensions not yet available (image still loading).
            if (!w || !h) { return; }

            var tl = map.latLngToLayerPoint(this._topLeft);
            var tr = map.latLngToLayerPoint(this._topRight);
            var bl = map.latLngToLayerPoint(this._bottomLeft);

            var a = (tr.x - tl.x) / w;
            var b = (tr.y - tl.y) / w;
            var c = (bl.x - tl.x) / h;
            var d = (bl.y - tl.y) / h;

            img.style.transformOrigin = '0 0';
            img.style.transform       = 'matrix(' + [a, b, c, d, tl.x, tl.y].join(',') + ')';
            img.style.left            = '0';
            img.style.top             = '0';
            img.style.width           = w + 'px';
            img.style.height          = h + 'px';
        }
    });

    // ── Factory function ──────────────────────────────────────────────────────

    /**
     * @param {string|HTMLImageElement} image      URL or image element.
     * @param {L.LatLngExpression}      topleft    Top-left world coordinate.
     * @param {L.LatLngExpression}      topright   Top-right world coordinate.
     * @param {L.LatLngExpression}      bottomleft Bottom-left world coordinate.
     * @param {Object}                  options    L.ImageOverlay options.
     */
    L.imageOverlay.rotated = function (image, topleft, topright, bottomleft, options) {
        return new L.ImageOverlay.Rotated(image, topleft, topright, bottomleft, options);
    };

}, window));
