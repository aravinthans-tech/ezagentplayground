/**
 * Shared top nav — EZOFIS logo + API Playground title grouped on the left.
 */
(function () {
    var LOGO_SRC = (window.PlaygroundBase ? PlaygroundBase.withBase('/ezofis-logo.png?v=20260716') : '/ezofis-logo.png?v=20260716');

    function createBrandNode() {
        var brand = document.createElement('a');
        brand.href = window.PlaygroundBase ? PlaygroundBase.withBase('/apikey.html') : '/apikey.html';
        brand.className = 'pg-brand flex items-center gap-2.5 flex-shrink-0 min-w-0 no-underline';

        var img = document.createElement('img');
        img.src = LOGO_SRC;
        img.alt = 'EZOFIS';
        img.className = 'pg-brand-logo';
        img.width = 36;
        img.height = 36;
        brand.appendChild(img);

        var title = document.createElement('h1');
        title.className = 'pg-brand-title';
        title.textContent = 'API Playground';
        brand.appendChild(title);

        return brand;
    }

    function upgradeNavBrand() {
        document.querySelectorAll('nav').forEach(function (nav) {
            var existingBrand = nav.querySelector('.pg-brand');
            if (existingBrand) {
                var logo = existingBrand.querySelector('.pg-brand-logo, img');
                if (logo) {
                    logo.src = LOGO_SRC;
                    logo.classList.add('pg-brand-logo');
                    logo.removeAttribute('onerror');
                    logo.style.display = '';
                }
                var fb = existingBrand.querySelector('.pg-logo-fallback');
                if (fb) fb.remove();
                return;
            }

            var oldTitle = null;
            nav.querySelectorAll('h1').forEach(function (h) {
                if (/API Playground/i.test((h.textContent || '').trim())) oldTitle = h;
            });
            if (!oldTitle) return;

            var brand = createBrandNode();
            var existingImg = nav.querySelector('img[src*="logo" i], img[alt*="Logo" i]');
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
