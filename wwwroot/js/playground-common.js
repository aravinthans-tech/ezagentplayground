/**
 * Shared helpers for V6 playground pages
 */
(function () {
    const STORAGE = {
        apiKey: 'v6PgApiKey',
        tenantId: 'v6PgTenantId',
        accessToken: 'v6PgAccessToken',
        email: 'v6PgLastEmail'
    };

    function showToast(message, type) {
        const toast = document.getElementById('toast');
        const content = document.getElementById('toastContent');
        const msg = document.getElementById('toastMessage');
        if (!toast || !content || !msg) return;
        msg.textContent = message;
        content.className = 'bg-white rounded-lg shadow-xl p-3 border-l-4 ' + (type === 'error' ? 'border-red-500' : 'border-green-500');
        toast.classList.remove('hidden');
        setTimeout(function () { toast.classList.add('hidden'); }, 3500);
    }

    function prettyJson(value) {
        try {
            if (typeof value === 'string') {
                const t = value.trim();
                if ((t.startsWith('{') && t.endsWith('}')) || (t.startsWith('[') && t.endsWith(']'))) {
                    return JSON.stringify(JSON.parse(t), null, 2);
                }
            }
            return typeof value === 'string' ? value : JSON.stringify(value, null, 2);
        } catch (_) {
            return String(value ?? '');
        }
    }

    function saveSession(data) {
        if (data.apiKey) localStorage.setItem(STORAGE.apiKey, data.apiKey);
        if (data.tenantId) localStorage.setItem(STORAGE.tenantId, data.tenantId);
        if (data.token) localStorage.setItem(STORAGE.accessToken, data.token);
        if (data.accessToken) localStorage.setItem(STORAGE.accessToken, data.accessToken);
        if (data.email) localStorage.setItem(STORAGE.email, data.email);
        if (data.userName) localStorage.setItem(STORAGE.email, data.userName);
    }

    function restoreFields(ids) {
        ids = ids || {};
        const apiKeyEl = document.getElementById(ids.apiKey || 'apiKey');
        const tenantEl = document.getElementById(ids.tenantId || 'tenantId');
        const tokenEl = document.getElementById(ids.token || 'accessToken');
        if (apiKeyEl && !apiKeyEl.value) apiKeyEl.value = localStorage.getItem(STORAGE.apiKey) || '';
        if (tenantEl && !tenantEl.value) tenantEl.value = localStorage.getItem(STORAGE.tenantId) || '';
        if (tokenEl && !tokenEl.value) tokenEl.value = localStorage.getItem(STORAGE.accessToken) || '';
    }

    async function apiFetch(path, options) {
        options = options || {};
        const headers = Object.assign({ 'Content-Type': 'application/json' }, options.headers || {});
        const apiKey = options.apiKey || (document.getElementById('apiKey') || {}).value;
        if (apiKey) headers['X-API-Key'] = apiKey;
        const accessToken = options.accessToken || (document.getElementById('accessToken') || {}).value || localStorage.getItem(STORAGE.accessToken) || '';
        if (accessToken) headers['Authorization'] = 'Bearer ' + accessToken;
        const response = await fetch((window.PlaygroundBase ? PlaygroundBase.apiUrl(path) : (window.location.origin + path)), {
            method: options.method || 'GET',
            headers: headers,
            body: options.body
        });
        const text = await response.text();
        let data;
        try { data = text ? JSON.parse(text) : null; } catch (_) { data = text; }
        return { ok: response.ok, status: response.status, data: data, text: text };
    }

    function getSession() {
        return {
            apiKey: localStorage.getItem(STORAGE.apiKey) || '',
            tenantId: localStorage.getItem(STORAGE.tenantId) || '',
            accessToken: localStorage.getItem(STORAGE.accessToken) || '',
            email: localStorage.getItem(STORAGE.email) || ''
        };
    }

    window.PlaygroundCommon = {
        STORAGE: STORAGE,
        showToast: showToast,
        prettyJson: prettyJson,
        saveSession: saveSession,
        restoreFields: restoreFields,
        getSession: getSession,
        apiFetch: apiFetch
    };
})();
