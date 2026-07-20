(function () {
  const storageKey = "theme";
  const root = document.documentElement;

  function preferredTheme() {
    const saved = localStorage.getItem(storageKey);
    if (saved === "light" || saved === "dark") return saved;
    /* Playground defaults to light; use the nav toggle to switch to dark */
    return "light";
  }

  function applyTheme(theme) {
    const isDark = theme === "dark";
    root.setAttribute("data-theme", theme);
    localStorage.setItem(storageKey, theme);
    document.querySelectorAll("[data-theme-toggle]").forEach((button) => {
      button.setAttribute("aria-label", isDark ? "Switch to light theme" : "Switch to dark theme");
      button.setAttribute("title", isDark ? "Switch to light theme" : "Switch to dark theme");
      button.innerHTML = isDark
        ? '<svg class="theme-toggle-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M12 3v2.25m6.36.39-1.59 1.59M21 12h-2.25m-.39 6.36-1.59-1.59M12 18.75V21m-4.77-4.23-1.59 1.59M5.25 12H3m4.23-4.77L5.64 5.64M15.75 12a3.75 3.75 0 1 1-7.5 0 3.75 3.75 0 0 1 7.5 0Z"/></svg>'
        : '<svg class="theme-toggle-icon" viewBox="0 0 24 24" aria-hidden="true"><path d="M20.35 15.35A9 9 0 0 1 8.65 3.65 9 9 0 1 0 20.35 15.35Z"/></svg>';
    });
    document.querySelectorAll("img").forEach((image) => {
      const src = image.getAttribute("src") || "";
      if (image.dataset.themeLogo === "true" || /\/(?:logo|ezofis-logo|1|2)\.png$/i.test(src)) {
        image.dataset.themeLogo = "true";
        image.src = isDark ? "/2.png" : "/1.png";
      }
    });
  }

  function markActiveNavigation() {
    const currentPath = window.location.pathname.replace(/\/$/, "") || "/index.html";
    document.querySelectorAll("nav a[href]").forEach((link) => {
      if (link.classList.contains("pg-brand")) return;
      const url = new URL(link.getAttribute("href"), window.location.origin);
      const linkPath = url.pathname.replace(/\/$/, "") || "/index.html";
      const isDocs = currentPath.includes("documentation") && linkPath.includes("documentation");
      const isActive = linkPath === currentPath || isDocs;
      link.classList.toggle("theme-nav-active", isActive);
    });
  }

  function toggleTheme() {
    applyTheme(root.getAttribute("data-theme") === "dark" ? "light" : "dark");
  }

  function installToggle() {
    if (document.querySelector("[data-theme-toggle]")) {
      applyTheme(root.getAttribute("data-theme") || preferredTheme());
      return true;
    }

    const nav = document.querySelector("nav");
    if (!nav) return false;

    const navGroups = Array.from(nav.querySelectorAll(":scope > div"));
    const target =
      nav.querySelector(".justify-end") ||
      navGroups.find((group) => group.querySelector('a[href*="examples"], a[href*="documentation"], a[href*="usage-report"]')) ||
      nav.querySelector("[class*='justify-end']") ||
      navGroups[navGroups.length - 1] ||
      nav;

    const button = document.createElement("button");
    button.type = "button";
    button.className = "theme-toggle";
    button.dataset.themeToggle = "true";
    button.addEventListener("click", toggleTheme);

    target.insertBefore(button, target.firstChild);
    applyTheme(root.getAttribute("data-theme") || preferredTheme());
    markActiveNavigation();
    return true;
  }

  applyTheme(preferredTheme());
  markActiveNavigation();

  if (document.readyState === "loading") {
    document.addEventListener("DOMContentLoaded", installToggle);
  } else {
    installToggle();
  }

  const observer = new MutationObserver(() => {
    if (installToggle()) observer.disconnect();
  });
  observer.observe(document.documentElement, { childList: true, subtree: true });

  window.addEventListener("storage", (event) => {
    if (event.key === storageKey && (event.newValue === "light" || event.newValue === "dark")) {
      applyTheme(event.newValue);
    }
  });
})();

(function () {
  function isPageReload() {
    const nav = performance.getEntriesByType && performance.getEntriesByType("navigation")[0];
    return !!(nav && (nav.type === "reload" || nav.type === "back_forward"));
  }

  window.PlaygroundFormStorage = {
    isPageReload: isPageReload,
    shouldRestore: function () {
      return !isPageReload();
    },
    clearOnReload: function (keys) {
      if (!isPageReload()) return;
      (keys || []).forEach(function (k) {
        try {
          localStorage.removeItem(k);
        } catch (_) {}
      });
    }
  };
})();
