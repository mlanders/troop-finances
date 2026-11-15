// Makes the site installable and drives the "Add to Home Screen" banner (InstallPrompt.razor).
(function () {
    'use strict';

    if ('serviceWorker' in navigator) {
        window.addEventListener('load', function () {
            navigator.serviceWorker.register('/sw.js').catch(function () { /* not fatal */ });
        });
    }

    var DISMISS_KEY = 'troopfin-install-dismissed';
    var DISMISS_DAYS = 30;
    var deferredPrompt = null;

    function isInstalled() {
        return window.matchMedia('(display-mode: standalone)').matches ||
            window.matchMedia('(display-mode: fullscreen)').matches ||
            window.navigator.standalone === true;
    }

    function recentlyDismissed() {
        try {
            var at = Number(localStorage.getItem(DISMISS_KEY));
            return at > 0 && Date.now() - at < DISMISS_DAYS * 864e5;
        } catch (e) {
            return false;
        }
    }

    function rememberDismissed() {
        try { localStorage.setItem(DISMISS_KEY, String(Date.now())); } catch (e) { /* ignore */ }
    }

    // iPhone/iPad: no install prompt event, so explain the Share menu. iPadOS reports itself as a
    // Mac, so a touch-capable "Mac" counts too. Only Safari can add to the home screen reliably;
    // other iOS browsers and in-app browsers (Gmail, Facebook...) get told to open Safari.
    function iosVariant() {
        var ua = navigator.userAgent;
        var isIos = /iPhone|iPad|iPod/.test(ua) || (/Macintosh/.test(ua) && navigator.maxTouchPoints > 1);
        if (!isIos) {
            return null;
        }
        var isSafari = /Safari\//.test(ua) && !/CriOS|FxiOS|EdgiOS|OPiOS|GSA\/|FBAN|FBAV|Instagram|Line\//.test(ua);
        return isSafari ? 'ios' : 'ios-other';
    }

    function currentVariant() {
        if (isInstalled() || recentlyDismissed()) {
            return null;
        }
        if (deferredPrompt) {
            return 'prompt';
        }
        return iosVariant();
    }

    function render() {
        var banner = document.getElementById('installPrompt');
        if (!banner) {
            return;
        }
        var variant = currentVariant();
        banner.hidden = !variant;
        if (!variant) {
            return;
        }
        banner.querySelectorAll('[data-install-variant]').forEach(function (el) {
            el.hidden = el.getAttribute('data-install-variant') !== variant;
        });
    }

    window.addEventListener('beforeinstallprompt', function (e) {
        e.preventDefault();
        deferredPrompt = e;
        render();
    });

    window.addEventListener('appinstalled', function () {
        deferredPrompt = null;
        render();
    });

    // Delegated, so it keeps working after Blazor's enhanced navigation swaps the markup.
    document.addEventListener('click', function (e) {
        var button = e.target.closest && e.target.closest('#installPrompt [data-install-action]');
        if (!button) {
            return;
        }
        if (button.getAttribute('data-install-action') === 'install' && deferredPrompt) {
            var prompt = deferredPrompt;
            deferredPrompt = null;
            prompt.prompt();
            prompt.userChoice.then(function (choice) {
                if (choice.outcome !== 'accepted') {
                    rememberDismissed();
                }
                render();
            });
        } else {
            rememberDismissed();
            render();
        }
    });

    // Enhanced navigation re-renders the layout with the banner hidden again; re-apply after it.
    function hookBlazor() {
        if (window.Blazor && typeof window.Blazor.addEventListener === 'function') {
            window.Blazor.addEventListener('enhancedload', render);
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', render);
    } else {
        render();
    }
    window.addEventListener('load', function () { hookBlazor(); render(); });
})();
