/**
 * Shared sidebar — V6 design + full API KEY section (Generate + My API Keys).
 * URL ?id=1 → Daimler Benz | ?id=2 → Access2Pay | ?id=3 → Invoice OCR Agent
 */
(function () {
    const PRODUCT_ID_STORAGE_KEY = 'playgroundProductId';

    const ICONS = {
        key: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z"></path>',
        shield: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"></path>',
        list: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 10h16M4 14h10"></path>',
        layers: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 5h14v4H5zM5 13h14v6H5z"></path>',
        lines: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h10M7 12h10M7 17h6"></path>',
        doc: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7h8M8 12h8M8 17h8"></path>',
        cloud: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 15a4 4 0 004 4h9a5 5 0 10-.1-9.999 5.002 5.002 0 10-9.78 2.096A4.001 4.001 0 003 15z"></path>',
        payment: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 10h18M7 15h1m4 0h1m-7 4h12a3 3 0 003-3V8a3 3 0 00-3-3H6a3 3 0 00-3 3v8a3 3 0 003 3z"></path>',
        scan: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2m-3 7h3m-3 4h3m-6-4h.01M9 16h.01"></path>',
        chart: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 3v18h18M7 14l3-3 4 4 5-7"></path>'
    };

    const PRODUCT = {
        1: { label: 'Daimler Benz', defaultPath: '/formdetails.html' },
        2: { label: 'Access2Pay', defaultPath: '/access2pay.html#insert' },
        3: { label: 'Invoice OCR Agent', defaultPath: '/invoiceocr.html#insert' }
    };

    const FUNCTION_ITEMS = [
        { href: '/formdetails.html', label: 'Form Details', icon: 'list', productId: 1 },
        { href: '/subformdetails.html', label: 'SubForm Details', icon: 'layers', productId: 1 },
        { href: '/subformfields.html', label: 'SubForm Fields', icon: 'lines', productId: 1 },
        { href: '/subformsubmitarchive.html', label: 'Generate PDF', icon: 'doc', productId: 1 },
        { href: '/getdatafromsalesforce.html', label: 'Get Data From Salesforce', icon: 'cloud', productId: 1 },
        { href: '/access2pay.html#insert', label: 'Access2Pay Connector Insert', icon: 'payment', productId: 2 },
        { href: '/access2pay.html#get', label: 'Access2Pay Get', icon: 'payment', productId: 2 },
        { href: '/access2pay.html#update', label: 'Access2Pay Update', icon: 'payment', productId: 2 },
        { href: '/invoiceocr.html#insert', label: 'Invoice OCR Insert', icon: 'scan', productId: 3 },
        { href: '/invoiceocr.html#get', label: 'Invoice OCR Get', icon: 'scan', productId: 3 },
        { href: '/invoiceocr.html#update', label: 'Invoice OCR Update', icon: 'scan', productId: 3 }
    ];

    const PAGE_TO_FUNCTION = {
        '/index.html': 'qrcode',
        '/formdetails.html': 'formdetails',
        '/subformdetails.html': 'subformdetails',
        '/subformfields.html': 'subformfields',
        '/subformsubmitarchive.html': 'generatepdf',
        '/getdatafromsalesforce.html': 'getdatafromsalesforce',
        '/access2pay.html': 'access2payinsert',
        '/invoiceocr.html': 'invoiceocrinsert',
        '/apikey.html': 'apikey',
        '/filesummary.html': 'filesummary',
        '/kycagent.html': 'kycagent'
    };

    const PAGE_TO_PRODUCT = {
        '/formdetails.html': 1,
        '/subformdetails.html': 1,
        '/subformfields.html': 1,
        '/subformsubmitarchive.html': 1,
        '/getdatafromsalesforce.html': 1,
        '/access2pay.html': 2,
        '/invoiceocr.html': 3
    };

    function normalizePath(path) {
        if (!path) return '';
        let p = path.split('?')[0];
        if (p.endsWith('/') && p.length > 1) p = p.slice(0, -1);
        if (!p || p === '/') return '/index.html';
        return p;
    }

    const PRODUCT_AWARE_PAGES = new Set([
        '/apikey.html',
        '/my-api-keys.html',
        '/examples.html',
        '/usage-report.html',
        '/playground-documentation.html',
        '/formdetails.html',
        '/subformdetails.html',
        '/subformfields.html',
        '/subformsubmitarchive.html',
        '/getdatafromsalesforce.html',
        '/access2pay.html',
        '/invoiceocr.html'
    ]);

    function readUrlProductId() {
        const id = new URLSearchParams(window.location.search).get('id');
        if (id === '3') return 3;
        if (id === '2') return 2;
        if (id === '1') return 1;
        return null;
    }

    function getProductId() {
        const fromUrl = readUrlProductId();
        if (fromUrl !== null) {
            try { sessionStorage.setItem(PRODUCT_ID_STORAGE_KEY, String(fromUrl)); } catch (_) {}
            return fromUrl;
        }
        try {
            const stored = sessionStorage.getItem(PRODUCT_ID_STORAGE_KEY);
            if (stored === '3') return 3;
            if (stored === '2') return 2;
            if (stored === '1') return 1;
        } catch (_) {}
        return 1;
    }

    function setProductId(productId) {
        const id = (productId === 3 || productId === 2) ? productId : 1;
        try { sessionStorage.setItem(PRODUCT_ID_STORAGE_KEY, String(id)); } catch (_) {}
        return id;
    }

    function ensureProductIdInUrl() {
        const productId = getProductId();
        const params = new URLSearchParams(window.location.search);
        if (params.get('id') === String(productId)) return;
        params.set('id', String(productId));
        const hash = window.location.hash || '';
        history.replaceState(null, '', window.location.pathname + '?' + params.toString() + hash);
    }

    function withProductId(href, productId) {
        if (!href) return href;
        const hashIdx = href.indexOf('#');
        const pathPart = hashIdx >= 0 ? href.slice(0, hashIdx) : href;
        const hashPart = hashIdx >= 0 ? href.slice(hashIdx) : '';
        const url = new URL(pathPart, window.location.origin);
        url.searchParams.set('id', String(productId));
        return url.pathname + url.search + hashPart;
    }

    function productIdForPage(path) {
        return PAGE_TO_PRODUCT[normalizePath(path)] || null;
    }

    function functionForPage(path) {
        const normalized = normalizePath(path);
        if (normalized === '/access2pay.html') {
            const hash = (window.location.hash || '#insert').replace('#', '').toLowerCase();
            if (hash === 'get') return 'access2payget';
            if (hash === 'update') return 'access2payupdate';
            return 'access2payinsert';
        }
        if (normalized === '/invoiceocr.html') {
            const hash = (window.location.hash || '#insert').replace('#', '').toLowerCase();
            if (hash === 'get') return 'invoiceocrget';
            if (hash === 'update') return 'invoiceocrupdate';
            return 'invoiceocrinsert';
        }
        return PAGE_TO_FUNCTION[normalized] || null;
    }

    function rememberCurrentApi(path) {
        const fn = functionForPage(path);
        if (fn) localStorage.setItem('lastApiCall', fn);
    }

    function wireExamplesNavLinks(path) {
        const fn = functionForPage(path);
        if (!fn) return;
        const productId = getProductId();
        const target = '/examples.html?function=' + encodeURIComponent(fn) + '&id=' + productId;
        document.querySelectorAll('nav a[href]').forEach(function (a) {
            const raw = a.getAttribute('href') || '';
            const base = raw.split('?')[0];
            if (base === '/examples.html' || base.endsWith('/examples.html')) {
                a.setAttribute('href', target);
            }
        });
    }

    function wireDocsNavLinks() {
        wireProductAwareLinks();
    }

    function wireProductNavLinks() {
        wireProductAwareLinks();
    }

    function wireProductAwareLinks() {
        const productId = getProductId();
        document.querySelectorAll('a[href]').forEach(function (a) {
            const raw = a.getAttribute('href') || '';
            if (!raw || raw.charAt(0) !== '/' || raw.startsWith('//')) return;
            const hashIdx = raw.indexOf('#');
            const beforeHash = hashIdx >= 0 ? raw.slice(0, hashIdx) : raw;
            const base = beforeHash.split('?')[0];
            if (!PRODUCT_AWARE_PAGES.has(base)) return;
            const hashPart = hashIdx >= 0 ? raw.slice(hashIdx) : '';
            a.setAttribute('href', withProductId(base, productId) + hashPart);
        });
    }

    function wireBrandLink() {
        const brand = document.querySelector('nav a.pg-brand');
        if (!brand) return;
        brand.setAttribute('href', withProductId('/apikey.html', getProductId()));
    }

    function ensureProductRoute() {
        const path = normalizePath(window.location.pathname);
        const pageProduct = productIdForPage(path);
        if (!pageProduct) return;

        const urlProduct = getProductId();
        if (pageProduct === urlProduct) return;

        const target = PRODUCT[urlProduct].defaultPath;
        window.location.replace(withProductId(target, urlProduct));
    }

    function svg(icon) {
        return `<svg class="pg-nav-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">${ICONS[icon] || ICONS.list}</svg>`;
    }

    function linkClass(isActive) {
        const base = 'api-nav pg-nav-item w-full text-left px-3 py-2 text-sm font-semibold button-hover flex items-center';
        return isActive ? `${base} pg-nav-item--active` : base;
    }

    function isItemActive(item, activePath) {
        const itemHref = item.href;
        if (itemHref.includes('#')) {
            const parts = itemHref.split('#');
            const itemPath = normalizePath(parts[0]);
            const itemHash = '#' + (parts[1] || 'insert');
            const currentHash = window.location.hash || '#insert';
            return itemPath === activePath && itemHash === currentHash;
        }
        return normalizePath(itemHref) === activePath;
    }

    function render(activeHref) {
        const active = normalizePath(activeHref || window.location.pathname);
        const productId = getProductId();
        const product = PRODUCT[productId];
        const functionItems = FUNCTION_ITEMS.filter(item => item.productId === productId);

        let html = `
            <div class="p-4 border-b pg-sidebar-header">
                <h2 class="pg-sidebar-title text-base font-semibold">API Functions</h2>
                <p class="text-[11px] text-gray-500 mt-0.5">${product.label}</p>
            </div>
            <div class="flex-1 overflow-y-auto p-2">`;

        html += '<div class="pg-sidebar-section">';
        html += '<h3 class="pg-sidebar-heading">API KEY</h3>';
        html += `<a href="${withProductId('/apikey.html', productId)}" class="${linkClass(active === '/apikey.html')}">${svg('key')}<span>Generate API Key</span></a>`;
        html += `<a href="${withProductId('/my-api-keys.html', productId)}" class="${linkClass(active === '/my-api-keys.html')}">${svg('shield')}<span>My API Keys</span></a>`;
        html += '</div>';

        html += '<div class="pg-sidebar-section">';
        html += '<h3 class="pg-sidebar-heading">FUNCTIONS</h3>';
        functionItems.forEach(item => {
            const href = withProductId(item.href, productId);
            const isActive = isItemActive({ href: item.href }, active);
            html += `<a href="${href}" class="${linkClass(isActive)}">${svg(item.icon)}<span>${item.label}</span></a>`;
        });
        html += '</div>';

        html += '<div class="pg-sidebar-section pg-sidebar-section--reports">';
        html += '<h3 class="pg-sidebar-heading">REPORTS</h3>';
        html += `<a href="${withProductId('/usage-report.html', productId)}" class="${linkClass(active === '/usage-report.html')}">${svg('chart')}<span>API Usage</span></a>`;
        html += '</div>';

        html += '</div>';
        return html;
    }

    function mount(activeHref) {
        ensureProductIdInUrl();
        ensureProductRoute();

        const root = document.getElementById('sidebar');
        if (!root) return;
        root.className = 'w-64 bg-white border-r border-gray-200 flex flex-col flex-shrink-0 sticky top-0 h-[calc(100vh-3rem)]';
        root.innerHTML = render(activeHref);

        const pagePath = window.location.pathname || activeHref;
        if (!normalizePath(pagePath).includes('examples.html')) {
            rememberCurrentApi(pagePath);
            wireExamplesNavLinks(pagePath);
        }
        wireDocsNavLinks();
        wireProductNavLinks();
        wireBrandLink();
    }

    window.PlaygroundSidebar = {
        mount,
        render,
        PAGE_TO_FUNCTION,
        normalizePath,
        functionForPage,
        getProductId,
        setProductId,
        withProductId,
        ensureProductIdInUrl,
        PRODUCT
    };
})();
