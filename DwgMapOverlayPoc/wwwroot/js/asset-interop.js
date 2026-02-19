/**
 * asset-interop.js
 * JS side of DwgAssetService.  Handles Blob URL lifecycle, localStorage,
 * image thumbnails, and JSZip-based ZIP preview.
 *
 * All functions live under window.assetInterop.
 *
 * DotNetStreamReference notes
 * ────────────────────────────
 * Blazor passes DotNetStreamReference objects to JS as objects whose
 * .arrayBuffer() method streams the .NET MemoryStream over JS interop.
 * Each reference can only be read ONCE — create a fresh MemoryStream on the
 * C# side for each JS call that needs the bytes.
 */

'use strict';

window.assetInterop = (() => {

    // ── State ────────────────────────────────────────────────────────────────

    /** object URL registry — id (DwgAsset.Id) → URL string */
    const _objectUrls = new Map();

    // ── Internal helpers ──────────────────────────────────────────────────────

    /**
     * Reads a DotNetStreamReference into an ArrayBuffer.
     * @param {object} streamRef  The DotNetStreamReference passed from Blazor.
     * @returns {Promise<ArrayBuffer>}
     */
    async function readStream(streamRef) {
        return streamRef.arrayBuffer();
    }

    /**
     * Registers a Blob URL under the given asset id, revoking any previous URL
     * that was registered under the same id.
     */
    function register(id, url) {
        const prev = _objectUrls.get(id);
        if (prev) URL.revokeObjectURL(prev);
        _objectUrls.set(id, url);
    }

    // ── IDB persistence helper ────────────────────────────────────────────────

    /**
     * Fire-and-forget IDB persistence.  Called after each successful upload so
     * the asset can be restored on the next page load without re-uploading.
     */
    function persistToIdb(assetKey, name, size, mimeType, ab, thumbnail) {
        if (!window.idbInterop) return;
        window.idbInterop
            .putRaw(assetKey, name, size, mimeType, ab, thumbnail ?? null)
            .catch(err => console.warn(`[assetInterop] IDB persist "${assetKey}" failed:`, err));
    }

    // ── Public API ────────────────────────────────────────────────────────────

    return {

        /**
         * Expose the internal register function so idb-interop.js can register
         * Blob URLs created from IDB-restored ArrayBuffers.
         *
         * @param {string} id   DwgAsset.Id
         * @param {string} url  Blob URL to associate with that id
         */
        registerBlobUrl(id, url) {
            register(id, url);
        },

        /**
         * Creates a Blob URL for an arbitrary file stream.
         * Used for GeoJSON uploads.
         *
         * @param {object} streamRef  DotNetStreamReference
         * @param {string} mimeType   e.g. "application/geo+json"
         * @param {string} id         DwgAsset.Id — used to revoke the previous URL
         * @param {string} [name]     Original file name — forwarded to IDB for persistence
         * @returns {Promise<string>} The new object URL
         */
        async createBlobUrl(streamRef, mimeType, id, name) {
            const ab  = await readStream(streamRef);
            const url = URL.createObjectURL(new Blob([ab], { type: mimeType }));
            register(id, url);
            // Persist for offline restoration
            const idbKey = mimeType === 'application/geo+json' ? 'geojson' : 'generic';
            persistToIdb(idbKey, name || '', ab.byteLength, mimeType, ab, null);
            return url;
        },

        /**
         * Creates a Blob URL for a PNG AND generates a downscaled JPEG thumbnail.
         * Both operations share the same ArrayBuffer read, so the stream is only
         * consumed once.
         *
         * @param {object} streamRef
         * @param {string} id
         * @param {number} thumbMaxW  Maximum thumbnail width in pixels
         * @param {number} thumbMaxH  Maximum thumbnail height in pixels
         * @param {string} [name]     Original file name — forwarded to IDB
         * @returns {Promise<{blobUrl: string, thumbnailDataUrl: string|null}>}
         */
        async createPngBlobUrlWithThumbnail(streamRef, id, thumbMaxW, thumbMaxH, name) {
            const ab      = await readStream(streamRef);
            const blob    = new Blob([ab], { type: 'image/png' });
            const blobUrl = URL.createObjectURL(blob);
            register(id, blobUrl);

            // Generate thumbnail on an off-screen canvas.
            const thumbnailDataUrl = await new Promise(resolve => {
                const img = new Image();
                img.onload = () => {
                    const ratio = Math.min(
                        thumbMaxW / img.naturalWidth,
                        thumbMaxH / img.naturalHeight,
                        1          // never upscale
                    );
                    const w = Math.round(img.naturalWidth  * ratio);
                    const h = Math.round(img.naturalHeight * ratio);

                    const canvas = document.createElement('canvas');
                    canvas.width  = w;
                    canvas.height = h;
                    canvas.getContext('2d').drawImage(img, 0, 0, w, h);
                    resolve(canvas.toDataURL('image/jpeg', 0.82));
                };
                img.onerror = () => resolve(null);
                img.src = blobUrl;
            });

            // Persist PNG + thumbnail for offline restoration
            persistToIdb('png', name || '', ab.byteLength, 'image/png', ab, thumbnailDataUrl);

            return { blobUrl, thumbnailDataUrl };
        },

        /**
         * Creates a Blob URL for a ZIP file AND invokes JSZip to list the
         * central-directory entries (up to maxEntries).
         * Both operations share the same ArrayBuffer read.
         *
         * @param {object} streamRef
         * @param {string} id
         * @param {number} maxEntries  Cap on returned entry count (default 200)
         * @param {string} [name]      Original file name — forwarded to IDB
         * @returns {Promise<{blobUrl: string, entries: Array<{name,size,isDirectory}>}>}
         */
        async createZipBlobUrlWithPreview(streamRef, id, maxEntries, name) {
            if (typeof JSZip === 'undefined') {
                throw new Error('[assetInterop] JSZip is not loaded on the page.');
            }

            const ab      = await readStream(streamRef);
            const blobUrl = URL.createObjectURL(new Blob([ab], { type: 'application/zip' }));
            register(id, blobUrl);

            // Parse the ZIP central directory (no decompression of file data).
            const zip     = await JSZip.loadAsync(ab);
            const entries = [];

            zip.forEach((path, file) => {
                entries.push({
                    name       : path,
                    // _data.uncompressedSize is an internal JSZip 3.x property.
                    // It is populated from the central directory without decompressing.
                    size       : file._data?.uncompressedSize ?? 0,
                    isDirectory: file.dir
                });
            });

            // Directories first, then alphabetical.
            entries.sort((a, b) => {
                if (a.isDirectory !== b.isDirectory) return a.isDirectory ? -1 : 1;
                return a.name.localeCompare(b.name);
            });

            // Persist ZIP for offline restoration (large file — fire and forget)
            persistToIdb('zip', name || '', ab.byteLength, 'application/zip', ab, null);

            return { blobUrl, entries: entries.slice(0, maxEntries || 200) };
        },

        /**
         * Revokes the Blob URL registered under the given asset id and removes it
         * from the registry.  Safe to call with an unknown id.
         *
         * @param {string} id  DwgAsset.Id
         */
        revokeBlobUrl(id) {
            const url = _objectUrls.get(id);
            if (url) {
                URL.revokeObjectURL(url);
                _objectUrls.delete(id);
            }
        },

        // ── localStorage wrappers ─────────────────────────────────────────────

        localStorageSet(key, value) { localStorage.setItem(key, value); },
        localStorageGet(key)        { return localStorage.getItem(key); },
        localStorageRemove(key)     { localStorage.removeItem(key); },

        // ── Image natural size ────────────────────────────────────────────────

        /**
         * Returns the natural pixel dimensions [width, height] of an image URL.
         * Used by AlignmentPanel to auto-detect PNG W×H so the user doesn't
         * have to type them manually.
         * @param {string} url  Blob URL or data URL.
         * @returns {Promise<[number, number] | null>}
         */
        getImageNaturalSize(url) {
            return new Promise((resolve) => {
                const img = new Image();
                img.onload  = () => resolve([img.naturalWidth, img.naturalHeight]);
                img.onerror = () => resolve(null);
                img.src = url;
            });
        }
    };

})();
