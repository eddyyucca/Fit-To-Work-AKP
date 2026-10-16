const CACHE_NAME = "akp-sehat-v2";
const urlsToCache = [
  "/",
  "/css/site.css",
  "/css/akp-theme.css",
  "/favicon.ico",
  "/images/icon-192.png",
  "/images/icon-512.png"
];

self.addEventListener("install", (event) => {
  event.waitUntil(
    caches.open(CACHE_NAME).then((cache) => cache.addAll(urlsToCache))
  );
  self.skipWaiting();
});

self.addEventListener("activate", (event) => {
  event.waitUntil(
    caches.keys().then((keys) =>
      Promise.all(
        keys.filter((key) => key !== CACHE_NAME).map((key) => caches.delete(key))
      )
    )
  );
  self.clients.claim();
});

self.addEventListener("fetch", (event) => {
  // Network-first untuk halaman (biar data form selalu fresh)
  event.respondWith(
    fetch(event.request).catch(() => caches.match(event.request))
  );
});