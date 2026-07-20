/**
 * API Playground sidebar
 */
(function () {
    const ICONS = {
        key: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z"></path>',
        shield: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"></path>',
        token: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M12 11c1.657 0 3-1.343 3-3S13.657 5 12 5 9 6.343 9 8s1.343 3 3 3zm0 2c-2.761 0-5 1.567-5 3.5V19h10v-2.5C17 14.567 14.761 13 12 13z"></path>',
        play: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M14.752 11.168l-5.197-3.028A1 1 0 008 8.944v6.112a1 1 0 001.555.832l5.197-3.028a1 1 0 000-1.664z"></path><path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M21 12a9 9 0 11-18 0 9 9 0 0118 0z"></path>',
        workflow: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 5H7a2 2 0 00-2 2v12a2 2 0 002 2h10a2 2 0 002-2V7a2 2 0 00-2-2h-2M9 5a2 2 0 002 2h2a2 2 0 002-2M9 5a2 2 0 012-2h2a2 2 0 012 2"></path>',
        inbox: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M20 13V7a2 2 0 00-2-2H6a2 2 0 00-2 2v6m16 0v4a2 2 0 01-2 2H6a2 2 0 01-2-2v-4m16 0H4"></path>',
        arrow: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M13 7l5 5m0 0l-5 5m5-5H6"></path>',
        doc: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7h8M8 12h8M8 17h8"></path>',
        chart: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 3v18h18M7 14l3-3 4 4 5-7"></path>'
    };

    const FUNCTION_ITEMS = [
        { href: '/workflow-start.html', label: 'Initiate Request', icon: 'play' },
        { href: '/workflow-get.html', label: 'Workflow List', icon: 'workflow' },
        { href: '/move-next.html', label: 'Advance Workflow', icon: 'arrow' }
    ];

    function href(path) {
        return (window.PlaygroundBase && PlaygroundBase.withBase) ? PlaygroundBase.withBase(path) : path;
    }

    function normalizePath(path) {
        if (!path) return '';
        let p = path.split('?')[0];
        const base = (window.PlaygroundBase && PlaygroundBase.base) || '';
        if (base && p.toLowerCase().startsWith(base.toLowerCase())) {
            p = p.slice(base.length) || '/';
        }
        if (p.endsWith('/') && p.length > 1) p = p.slice(0, -1);
        return p || '/apikey.html';
    }

    function svg(icon) {
        return '<svg class="pg-nav-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">' + (ICONS[icon] || ICONS.workflow) + '</svg>';
    }

    function linkClass(isActive) {
        const base = 'api-nav pg-nav-item w-full text-left px-3 py-2 text-sm font-semibold button-hover flex items-center';
        return isActive ? base + ' pg-nav-item--active' : base;
    }

    function render(activeHref) {
        const active = normalizePath(activeHref || window.location.pathname);
        let html = '<div class="p-4 border-b pg-sidebar-header"><h2 class="pg-sidebar-title text-base font-semibold">API Functions</h2><p class="text-[11px] text-gray-500 mt-0.5">V6 External Playground</p></div><div class="flex-1 overflow-y-auto p-2">';
        html += '<div class="pg-sidebar-section"><h3 class="pg-sidebar-heading">API KEY</h3>';
        html += '<a href="' + href('/apikey.html') + '" class="' + linkClass(active === '/apikey.html') + '">' + svg('key') + '<span>Generate API Key</span></a>';
        html += '<a href="' + href('/my-api-keys.html') + '" class="' + linkClass(active === '/my-api-keys.html') + '">' + svg('shield') + '<span>My API Keys</span></a>';
        html += '</div><div class="pg-sidebar-section"><h3 class="pg-sidebar-heading">FUNCTIONS</h3>';
        FUNCTION_ITEMS.forEach(function (item) {
            html += '<a href="' + href(item.href) + '" class="' + linkClass(active === item.href) + '">' + svg(item.icon) + '<span>' + item.label + '</span></a>';
        });
        html += '</div><div class="pg-sidebar-section"><h3 class="pg-sidebar-heading">TOOLS</h3>';
        html += '<a href="' + href('/examples.html') + '" class="' + linkClass(active === '/examples.html') + '">' + svg('token') + '<span>Code Examples</span></a>';
        html += '<a href="' + href('/playground-documentation.html') + '" class="' + linkClass(active === '/playground-documentation.html') + '">' + svg('doc') + '<span>Developer Docs</span></a>';
        html += '<a href="' + href('/usage-report.html') + '" class="' + linkClass(active === '/usage-report.html') + '">' + svg('chart') + '<span>API Usage</span></a>';
        html += '</div></div>';
        return html;
    }

    function mount(activeHref) {
        const root = document.getElementById('sidebar');
        if (!root) return;
        root.className = 'w-64 bg-white border-r border-gray-200 flex flex-col flex-shrink-0 sticky top-0 h-[calc(100vh-3rem)]';
        root.innerHTML = render(activeHref);
    }

    window.PlaygroundSidebar = { mount, render, normalizePath };
})();
