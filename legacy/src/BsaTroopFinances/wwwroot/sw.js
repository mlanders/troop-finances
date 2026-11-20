// Service worker for the installed app. Blazor Server needs a live connection, so nothing here
// tries to make the app work offline: it caches the offline page and its look, and every other
// request goes straight to the network. Its jobs are to make the site installable and to show a
// friendly page instead of the browser's own error when the phone has no signal.
// Bump CACHE_VERSION whenever offline.html or the files below change.
const CACHE_VERSION = 'troopfin-v6';
const OFFLINE_URL = '/offline.html';
const PRECACHE = [
    OFFLINE_URL,
    '/favicon.svg',
    '/icons/icon-192.png',
];

self.addEventListener('install', event => {
    event.waitUntil(caches.open(CACHE_VERSION).then(cache => cache.addAll(PRECACHE)));
    self.skipWaiting();
});

self.addEventListener('activate', event => {
    event.waitUntil(
        caches.keys()
            .then(keys => Promise.all(keys.filter(k => k !== CACHE_VERSION).map(k => caches.delete(k))))
            .then(() => self.clients.claim()));
});

self.addEventListener('fetch', event => {
    // Only page navigations get the offline fallback; everything else (Blazor's _framework and
    // _blazor traffic, receipts, CSS, fonts) is left to the browser untouched -- no respondWith.
    if (event.request.mode !== 'navigate') {
        return;
    }
    event.respondWith(
        fetch(event.request).catch(() => caches.match(OFFLINE_URL)));
});
