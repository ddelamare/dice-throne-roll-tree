(function () {
    const visitorStorageKey = 'dice-throne-visitor-id';

    function buildFallbackVisitorId() {
        if (window.crypto?.getRandomValues) {
            const values = new Uint32Array(4);
            window.crypto.getRandomValues(values);
            return Array.from(values, value => value.toString(16).padStart(8, '0')).join('-');
        }

        return `visitor-${Date.now()}-${Math.random().toString(16).slice(2)}-${performance.now().toString(16).replace('.', '')}`;
    }

    function getVisitorId() {
        let visitorId = localStorage.getItem(visitorStorageKey);
        if (!visitorId) {
            visitorId = window.crypto?.randomUUID?.() || buildFallbackVisitorId();
            localStorage.setItem(visitorStorageKey, visitorId);
        }

        return visitorId;
    }

    async function apiFetch(url, options = {}) {
        const headers = new Headers(options.headers || {});
        headers.set('X-Visitor-Id', getVisitorId());
        return fetch(url, { ...options, headers });
    }

    async function trackVisit(page) {
        try {
            await apiFetch('/api/telemetry/visit', {
                method: 'POST',
                headers: { 'Content-Type': 'application/json' },
                body: JSON.stringify({
                    visitorId: getVisitorId(),
                    page
                })
            });
        } catch (error) {
            console.error('Error recording visit:', error);
        }
    }

    window.telemetryClient = {
        getVisitorId,
        apiFetch,
        trackVisit
    };
})();
