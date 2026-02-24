'use strict';

// leaflet-capture.js
// Captures the Leaflet L.Map instance via L.Map.addInitHook and forwards it
// to leafletInterop.captureMap for deterministic readiness.
(() => {
    function attachHook() {
        if (!window.L || !window.L.Map || typeof window.L.Map.addInitHook !== 'function') return false;
        if (window.L.Map.__interopCaptureHooked) return true;

        window.L.Map.__interopCaptureHooked = true;
        window.L.Map.addInitHook(function () {
            try {
                const containerId = this?._container?.id;
                if (containerId === 'map') {
                    window.leafletInterop?.captureMap?.(this, 'addInitHook');
                }
            } catch (err) {
                console.error('[leaflet-capture] capture failed', err);
            }
        });

        return true;
    }

    const timer = setInterval(() => {
        if (attachHook()) clearInterval(timer);
    }, 25);
})();
