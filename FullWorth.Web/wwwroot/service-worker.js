const cachePrefix =
    "fullworth-pwa-";

const offlineCache =
    `${cachePrefix}offline-v1`;

const offlineUrl =
    "/offline.html";

self.addEventListener(
    "install",
    event => {
        event.waitUntil(
            caches
                .open(offlineCache)
                .then(cache =>
                    cache.add(
                        new Request(
                            offlineUrl,
                            {
                                cache: "reload"
                            }))));

        self.skipWaiting();
    });

self.addEventListener(
    "activate",
    event => {
        event.waitUntil(
            Promise.all([
                caches
                    .keys()
                    .then(keys =>
                        Promise.all(
                            keys
                                .filter(key =>
                                    key.startsWith(
                                        cachePrefix) &&
                                    key !==
                                        offlineCache)
                                .map(key =>
                                    caches.delete(
                                        key)))),
                "navigationPreload" in self.registration
                    ? self.registration.navigationPreload.enable()
                    : Promise.resolve(),
                self.clients.claim()
            ]));
    });

self.addEventListener(
    "message",
    event => {
        if (event.data?.type === "SKIP_WAITING") {
            self.skipWaiting();
        }
    });

self.addEventListener(
    "fetch",
    event => {
        const request =
            event.request;

        if (request.method !== "GET" ||
            request.mode !== "navigate")
        {
            return;
        }

        const url =
            new URL(
                request.url);

        if (url.origin !== self.location.origin)
        {
            return;
        }

        event.respondWith(
            (async () => {
                try
                {
                    const preload =
                        await event.preloadResponse;

                    if (preload)
                    {
                        return preload;
                    }

                    return await fetch(
                        request);
                }
                catch
                {
                    const cache =
                        await caches.open(
                            offlineCache);

                    return (
                        await cache.match(
                            offlineUrl)) ??
                        Response.error();
                }
            })());
    });
