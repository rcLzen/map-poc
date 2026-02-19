/**
 * idb-interop.js
 * IndexedDB persistence layer for uploaded assets (PNG / GeoJSON / ZIP).
 *
 * Database : "dwg-map-poc-v1"
 * Store    : "assets"  — keyPath: "assetKey"  (values: "png" | "geojson" | "zip")
 *
 * Records stored:
 *   { assetKey, name, size, mimeType, data: ArrayBuffer, thumbnail: string|null, storedAt: ms }
 *
 * On page reload the app calls getAssetBlob() for each slot.  If the record
 * exists it creates a Blob URL and returns metadata so DwgAssetService can
 * restore the asset as fully Ready (no re-upload prompt).
 *
 * putRaw() is called directly from assetInterop.js (same JS event loop) so it
 * never needs a DotNetStreamReference — it receives the ArrayBuffer directly.
 * putAsset() is called from C# via JSInterop and receives a DotNetStreamReference.
 */

window.idbInterop = (() => {

    const DB_NAME    = 'dwg-map-poc-v1';
    const DB_VERSION = 1;
    const STORE      = 'assets';

    // Singleton DB promise — shared across all calls
    let _dbPromise = null;

    function openDb() {
        if (_dbPromise) return _dbPromise;

        _dbPromise = new Promise((resolve, reject) => {
            const req = indexedDB.open(DB_NAME, DB_VERSION);

            req.onupgradeneeded = e => {
                const db = e.target.result;
                if (!db.objectStoreNames.contains(STORE)) {
                    db.createObjectStore(STORE, { keyPath: 'assetKey' });
                }
            };

            req.onsuccess = e => resolve(e.target.result);
            req.onerror   = e => {
                _dbPromise = null; // allow retry
                reject(e.target.error);
            };
        });

        return _dbPromise;
    }

    // ── Write ─────────────────────────────────────────────────────────────────

    /**
     * Store raw ArrayBuffer directly.  Called from assetInterop.js (same page).
     */
    async function putRaw(assetKey, name, size, mimeType, data, thumbnail) {
        const db = await openDb();
        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).put({
                assetKey,
                name,
                size,
                mimeType,
                data,
                thumbnail: thumbnail ?? null,
                storedAt:  Date.now()
            });
            tx.oncomplete = () => resolve();
            tx.onerror    = e => reject(e.target.error);
        });
    }

    /**
     * Store from a Blazor DotNetStreamReference.
     * Used when C# explicitly needs to persist bytes after upload.
     */
    async function putAsset(assetKey, name, size, mimeType, streamRef, thumbnailDataUrl) {
        const data = await streamRef.arrayBuffer();
        return putRaw(assetKey, name, size, mimeType, data, thumbnailDataUrl ?? null);
    }

    // ── Read ──────────────────────────────────────────────────────────────────

    /**
     * Read asset from IDB, create a Blob URL, register it with assetInterop,
     * and return metadata for DwgAssetService to build a Ready DwgAsset.
     *
     * @param {string} assetKey     "png" | "geojson" | "zip"
     * @param {string} blazorAssetId  DwgAsset.Id — used to key the Blob URL registry
     * @returns {{ blobUrl, name, size, mimeType, thumbnail } | null}
     */
    async function getAssetBlob(assetKey, blazorAssetId) {
        let db;
        try { db = await openDb(); }
        catch { return null; }

        const record = await new Promise((resolve, reject) => {
            const tx  = db.transaction(STORE, 'readonly');
            const req = tx.objectStore(STORE).get(assetKey);
            req.onsuccess = e => resolve(e.target.result ?? null);
            req.onerror   = e => reject(e.target.error);
        });

        if (!record) return null;

        const blob = new Blob([record.data], { type: record.mimeType });
        const url  = URL.createObjectURL(blob);

        // Register with assetInterop so revokeBlobUrl() can clean it up properly
        if (window.assetInterop && typeof window.assetInterop.registerBlobUrl === 'function') {
            window.assetInterop.registerBlobUrl(blazorAssetId, url);
        }

        return {
            blobUrl:   url,
            name:      record.name,
            size:      record.size,
            mimeType:  record.mimeType,
            thumbnail: record.thumbnail ?? null
        };
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    async function deleteAsset(assetKey) {
        let db;
        try { db = await openDb(); }
        catch { return; }

        return new Promise((resolve, reject) => {
            const tx = db.transaction(STORE, 'readwrite');
            tx.objectStore(STORE).delete(assetKey);
            tx.oncomplete = () => resolve();
            tx.onerror    = e => reject(e.target.error);
        });
    }

    // ── Existence check ───────────────────────────────────────────────────────

    async function hasAsset(assetKey) {
        let db;
        try { db = await openDb(); }
        catch { return false; }

        return new Promise((resolve, reject) => {
            const tx  = db.transaction(STORE, 'readonly');
            const req = tx.objectStore(STORE).count(assetKey);
            req.onsuccess = e => resolve(e.target.result > 0);
            req.onerror   = e => reject(e.target.error);
        });
    }

    // ── Public API ────────────────────────────────────────────────────────────
    return { putRaw, putAsset, getAssetBlob, deleteAsset, hasAsset };
})();
