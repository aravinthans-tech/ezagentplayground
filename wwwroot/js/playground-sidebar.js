/**
 * Shared sidebar — V6 design + full API KEY section (Generate + My API Keys).
 */
(function () {
    const ICONS = {
        key: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z"></path>',
        shield: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M9 12l2 2 4-4m5.618-4.016A11.955 11.955 0 0112 2.944a11.955 11.955 0 01-8.618 3.04A12.02 12.02 0 003 9c0 5.591 3.824 10.29 9 11.622 5.176-1.332 9-6.03 9-11.622 0-1.042-.133-2.052-.382-3.016z"></path>',
        list: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M4 6h16M4 10h16M4 14h10"></path>',
        layers: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M5 5h14v4H5zM5 13h14v6H5z"></path>',
        lines: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M7 7h10M7 12h10M7 17h6"></path>',
        doc: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M8 7h8M8 12h8M8 17h8"></path>',
        cloud: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 15a4 4 0 004 4h9a5 5 0 10-.1-9.999 5.002 5.002 0 10-9.78 2.096A4.001 4.001 0 003 15z"></path>',
        chart: '<path stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="M3 3v18h18M7 14l3-3 4 4 5-7"></path>'
    };

    const SECTIONS = [
        {
            title: 'API KEY',
            extraClass: '',
            items: [
                { href: '/apikey.html', label: 'Generate API Key', icon: 'key' },
                { href: '/my-api-keys.html', label: 'My API Keys', icon: 'shield' }
            ]
        },
        {
            title: 'FUNCTIONS',
            extraClass: '',
            items: [
                { href: '/formdetails.html', label: 'Form Details', icon: 'list' },
                { href: '/subformdetails.html', label: 'SubForm Details', icon: 'layers' },
                { href: '/subformfields.html', label: 'SubForm Fields', icon: 'lines' },
                { href: '/subformsubmitarchive.html', label: 'Generate PDF', icon: 'doc' },
                { href: '/getdatafromsalesforce.html', label: 'Get Data From Salesforce', icon: 'cloud' }
            ]
        },
        {
            title: 'REPORTS',
            extraClass: 'pg-sidebar-section--reports',
            items: [
                { href: '/usage-report.html', label: 'API Usage', icon: 'chart' }
            ]
        }
    ];

    function normalizePath(path) {
        if (!path) return '';
        const p = path.split('?')[0];
        return p.endsWith('/') && p.length > 1 ? p.slice(0, -1) : p;
    }

    function svg(icon) {
        return `<svg class="pg-nav-icon" fill="none" stroke="currentColor" viewBox="0 0 24 24" aria-hidden="true">${ICONS[icon] || ICONS.list}</svg>`;
    }

    function linkClass(isActive) {
        const base = 'api-nav pg-nav-item w-full text-left px-3 py-2 text-sm font-semibold button-hover flex items-center';
        return isActive ? `${base} pg-nav-item--active` : base;
    }

    function render(activeHref) {
        const active = normalizePath(activeHref || window.location.pathname);
        let html = `
            <div class="p-4 border-b pg-sidebar-header">
                <h2 class="pg-sidebar-title text-base font-semibold">API Functions</h2>
            </div>
            <div class="flex-1 overflow-y-auto p-2">`;

        SECTIONS.forEach(section => {
            html += `<div class="pg-sidebar-section ${section.extraClass}">`;
            html += `<h3 class="pg-sidebar-heading">${section.title}</h3>`;
            section.items.forEach(item => {
                const isActive = normalizePath(item.href) === active;
                html += `<a href="${item.href}" class="${linkClass(isActive)}">${svg(item.icon)}<span>${item.label}</span></a>`;
            });
            html += '</div>';
        });

        html += '</div>';
        return html;
    }

    function mount(activeHref) {
        const root = document.getElementById('sidebar');
        if (!root) return;
        root.className = 'w-64 bg-white border-r border-gray-200 flex flex-col flex-shrink-0 sticky top-0 h-[calc(100vh-3rem)]';
        root.innerHTML = render(activeHref);
    }

    window.PlaygroundSidebar = { mount, render };
})();
