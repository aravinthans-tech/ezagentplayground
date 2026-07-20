/**
 * Base path helper for IIS sub-app hosting (e.g. http://localhost/V6Playground).
 */
(function () {
    function detectBase() {
        var path = window.location.pathname || '/';
        var m = path.match(/^(.*?\/(?:V6Playground|v6playground))(?=\/|$)/i);
        if (m) return m[1];
        // When opened as site root, no prefix
        return '';
    }

    var base = detectBase();

    function withBase(path) {
        if (!path) return base || '/';
        if (/^https?:\/\//i.test(path)) return path;
        if (path.charAt(0) !== '/') path = '/' + path;
        return base + path;
    }

    function apiUrl(path) {
        return window.location.origin + withBase(path);
    }

    window.PlaygroundBase = {
        base: base,
        withBase: withBase,
        apiUrl: apiUrl
    };
})();
