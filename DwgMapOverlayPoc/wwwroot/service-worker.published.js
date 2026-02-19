// Caution! Be sure you understand the caveats before publishing an application with
// offline support. See https://aka.ms/blazor-offline-considerations

self.importScripts('./service-worker-assets.js');
self.addEventListener('install',  event => event.waitUntil(onInstall(event)));
self.addEventListener('activate', event => event.waitUntil(onActivate(event)));
self.addEventListener('fetch',    event => event.respondWith(onFetch(event)));

const cacheNamePrefix = 'offline-cache-';
const cacheName       = `${cacheNamePrefix}${self.assetsManifest.version}`;

// ── Static asset patterns to pre-cache from the Blazor manifest ──────────────
const offlineAssetsInclude = [
    /\.dll$/, /\.pdb$/, /\.wasm/, /\.html/, /\.js$/, /\.json$/,
    /\.css$/, /\.woff$/, /\.woff2$/, /\.png$/, /\.jpe?g$/, /\.gif$/,
    /\.ico$/, /\.blat$/, /\.dat$/, /\.svg$/
];
const offlineAssetsExclude = [/^service-worker\.js$/];

// ── CDN resources pre-cached at install time ──────────────────────────────────
// SRI intentionally omitted (CDN encoding variability — same reason as index.html)
const cdnResourcesToCache = [
    // Leaflet 1.9.4
    'https://unpkg.com/leaflet@1.9.4/dist/leaflet.css',
    'https://unpkg.com/leaflet@1.9.4/dist/leaflet.js',
    // JSZip 3.10
    'https://cdn.jsdelivr.net/npm/jszip@3.10.1/dist/jszip.min.js',
    // Leaflet.ImageOverlay.Rotated (Task 4)
    'https://unpkg.com/leaflet-imageoverlay-rotated@0.2.0/src/ImageOverlay.Rotated.js',
    // Turf.js v6 (Tasks 4 + 6)
    'https://cdn.jsdelivr.net/npm/@turf/turf@6/turf.min.js',
    // html2canvas (Task 10)
    'https://cdn.jsdelivr.net/npm/html2canvas@1.4.1/dist/html2canvas.min.js'
];

// ── Tile origins — stale-while-revalidate strategy ───────────────────────────
const tileOrigins = [
    // OpenStreetMap
    'https://tile.openstreetmap.org',
    'https://a.tile.openstreetmap.org',
    'https://b.tile.openstreetmap.org',
    'https://c.tile.openstreetmap.org',
    // CartoDB Positron (light)
    'https://a.basemaps.cartocdn.com',
    'https://b.basemaps.cartocdn.com',
    'https://c.basemaps.cartocdn.com',
    // Mapbox (only caches if user has a token and pans the map)
    'https://api.mapbox.com'
];
const tileCacheName = 'map-tiles-v1';

// ── Max tile cache size (entries) ─────────────────────────────────────────────
// Keeps the tile cache from growing unbounded between app versions.
const MAX_TILE_CACHE_ENTRIES = 2000;

const base        = '/';
const baseUrl     = new URL(base, self.origin);
const manifestUrlList = self.assetsManifest.assets.map(
    asset => new URL(asset.url, baseUrl).href
);

// ── Install ───────────────────────────────────────────────────────────────────

async function onInstall(event) {
    console.info('[SW] Install — caching Blazor assets and CDN resources');

    const assetsRequests = self.assetsManifest.assets
        .filter(asset => offlineAssetsInclude.some(p => p.test(asset.url)))
        .filter(asset => !offlineAssetsExclude.some(p => p.test(asset.url)))
        .map(asset => new Request(asset.url, { integrity: asset.hash, cache: 'no-cache' }));

    const cdnRequests = cdnResourcesToCache.map(url => new Request(url, { cache: 'no-cache' }));

    const cache = await caches.open(cacheName);

    // CDN resources: cache individually so one failure doesn't block the rest
    await Promise.allSettled(
        cdnRequests.map(req => cache.add(req).catch(err =>
            console.warn(`[SW] CDN pre-cache failed for ${req.url}:`, err)
        ))
    );

    // Blazor assets: must all succeed for the SW to install correctly
    await cache.addAll(assetsRequests);

    // Skip waiting so the new SW activates immediately on next load
    self.skipWaiting();

    console.info('[SW] Install complete');
}

// ── Activate ──────────────────────────────────────────────────────────────────

async function onActivate(event) {
    console.info('[SW] Activate');

    // Delete stale app-shell caches; keep the tile cache across versions
    const cacheKeys = await caches.keys();
    await Promise.all(
        cacheKeys
            .filter(key => key.startsWith(cacheNamePrefix) && key !== cacheName)
            .map(key => {
                console.info(`[SW] Deleting old cache: ${key}`);
                return caches.delete(key);
            })
    );

    // Take control of all open windows immediately
    await self.clients.claim();

    // Trim the tile cache if it has grown large
    await trimTileCache();

    // Notify all controlled clients — triggers the "Offline Ready" toast
    const clients = await self.clients.matchAll({ type: 'window' });
    clients.forEach(client => client.postMessage({ type: 'OFFLINE_READY' }));

    console.info('[SW] Activate complete — clients notified');
}

// ── Fetch ─────────────────────────────────────────────────────────────────────

async function onFetch(event) {
    const { request } = event;

    // ── Blob URLs: always network (they are local, never cached) ─────────────
    if (request.url.startsWith('blob:')) {
        return fetch(request);
    }

    // ── Map tiles: stale-while-revalidate ────────────────────────────────────
    if (tileOrigins.some(origin => request.url.startsWith(origin))) {
        const tileCache = await caches.open(tileCacheName);
        const cached    = await tileCache.match(request);

        const networkFetch = fetch(request)
            .then(response => {
                if (response.ok) tileCache.put(request, response.clone());
                return response;
            })
            .catch(() => null);

        // Return cached immediately if available; update in background
        return cached ?? await networkFetch ?? new Response('', { status: 503 });
    }

    // ── App shell & static assets: cache-first ───────────────────────────────
    if (request.method === 'GET') {
        const isNavigate = request.mode === 'navigate'
            && !manifestUrlList.some(url => url === request.url);
        const cacheKey = isNavigate ? 'index.html' : request;

        const cache          = await caches.open(cacheName);
        const cachedResponse = await cache.match(cacheKey);

        if (cachedResponse) return cachedResponse;
    }

    // Fallback: network
    return fetch(request);
}

// ── Tile cache maintenance ────────────────────────────────────────────────────

async function trimTileCache() {
    try {
        const cache = await caches.open(tileCacheName);
        const keys  = await cache.keys();
        if (keys.length > MAX_TILE_CACHE_ENTRIES) {
            const toDelete = keys.slice(0, keys.length - MAX_TILE_CACHE_ENTRIES);
            await Promise.all(toDelete.map(k => cache.delete(k)));
            console.info(`[SW] Tile cache trimmed: removed ${toDelete.length} old tiles`);
        }
    } catch { /* non-critical */ }
}
