(() => {
    if (!("serviceWorker" in navigator)) {
        return;
    }

    window.addEventListener("load", async () => {
        try {
            await navigator.serviceWorker.register(
                "/service-worker.js",
                {
                    scope: "/",
                    updateViaCache: "none"
                });
        }
        catch {
            // PWA registration is progressive enhancement.
            // A registration failure must never block FullWorth.
        }
    });
})();
