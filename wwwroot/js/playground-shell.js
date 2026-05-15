/**
 * Shared top nav — EZOFIS logo + API Playground title grouped on the left.
 */
(function () {
    function createBrandNode() {
        var brand = document.createElement('a');
        brand.href = '/apikey.html';
        brand.className = 'pg-brand flex items-center gap-2.5 flex-shrink-0 min-w-0 no-underline';

        var img = document.createElement('img');
        img.src = '/logo.png';
        img.alt = 'EZOFIS';
        img.className = 'pg-brand-logo h-8 w-auto max-w-[100px] object-contain flex-shrink-0';
        img.onerror = function () {
            img.style.display = 'none';
            var f = brand.querySelector('.pg-logo-fallback');
            if (f) f.style.display = 'flex';
        };
        brand.appendChild(img);

        var fallback = document.createElement('span');
        fallback.className = 'pg-logo-fallback hidden items-center gap-0.5 text-base font-bold tracking-tight whitespace-nowrap';
        fallback.style.display = 'none';
        fallback.innerHTML =
            '<span class="text-purple-600">ez</span><span class="text-purple-500">o</span><span class="text-purple-700">fis</span>';
        brand.appendChild(fallback);

        var title = document.createElement('h1');
        title.className = 'pg-brand-title';
        title.textContent = 'API Playground';
        brand.appendChild(title);

        return brand;
    }

    function upgradeNavBrand() {
        document.querySelectorAll('nav').forEach(function (nav) {
            if (nav.querySelector('.pg-brand')) return;

            var oldTitle = null;
            nav.querySelectorAll('h1').forEach(function (h) {
                if (/API Playground/i.test((h.textContent || '').trim())) oldTitle = h;
            });
            if (!oldTitle) return;

            var brand = createBrandNode();
            var existingImg = nav.querySelector('img[src*="logo" i], img[alt*="Logo" i]');
            var existingFallback = nav.querySelector('#logoFallback');

            if (existingImg) {
                var src = existingImg.getAttribute('src');
                if (src) brand.querySelector('.pg-brand-logo').setAttribute('src', src);
            }
            if (existingFallback) {
                var slot = brand.querySelector('.pg-logo-fallback');
                slot.innerHTML = existingFallback.innerHTML;
                slot.id = 'logoFallback';
                slot.className = 'pg-logo-fallback hidden items-center gap-0.5 text-base font-bold tracking-tight whitespace-nowrap';
                if (existingFallback.style.display && existingFallback.style.display !== 'none') {
                    slot.style.display = existingFallback.style.display;
                }
            }

            var logoBox = existingImg && existingImg.closest('div');
            if (logoBox && nav.contains(logoBox) && logoBox.querySelector('img')) {
                logoBox.replaceWith(brand);
            } else {
                nav.insertBefore(brand, nav.firstChild);
            }

            if (oldTitle.parentElement !== brand) oldTitle.remove();
        });
    }

    function markActiveTopNav() {
        var path = (window.location.pathname || '').split('?')[0];
        document.querySelectorAll('nav a[href]').forEach(function (a) {
            if (a.classList.contains('pg-brand')) return;
            var href = a.getAttribute('href');
            if (!href || href.indexOf('#') === 0) return;
            var target = href.split('?')[0];
            if (target === path) {
                a.classList.add('pg-nav-top-active', 'theme-nav-active');
            }
        });
    }

    function init() {
        upgradeNavBrand();
        markActiveTopNav();
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', init);
    } else {
        init();
    }

    window.PlaygroundShell = { upgradeNavBrand, markActiveTopNav, init };
})();
