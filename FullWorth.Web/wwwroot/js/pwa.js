(() => {
    let deferredInstallPrompt = null;

    function isStandalone() {
        return Boolean(
            window.matchMedia?.(
                "(display-mode: standalone)")?.matches ||
            window.navigator.standalone === true);
    }

    function isIosLike() {
        const userAgent =
            navigator.userAgent ?? "";

        const isClassicIos =
            /iPad|iPhone|iPod/i.test(
                userAgent);

        const isTouchMac =
            navigator.platform ===
                "MacIntel" &&
            navigator.maxTouchPoints >
                1;

        return isClassicIos ||
            isTouchMac;
    }

    window.addEventListener(
        "beforeinstallprompt",
        event => {
            event.preventDefault();

            deferredInstallPrompt =
                event;
        });

    window.addEventListener(
        "appinstalled",
        () => {
            deferredInstallPrompt =
                null;
        });

    window.FullWorthPwa = {
        isStandalone,

        async install() {
            if (isStandalone()) {
                return "installed";
            }

            if (deferredInstallPrompt) {
                const prompt =
                    deferredInstallPrompt;

                deferredInstallPrompt =
                    null;

                await prompt.prompt();

                const choice =
                    await prompt.userChoice;

                return choice?.outcome ===
                    "accepted"
                    ? "accepted"
                    : "dismissed";
            }

            if (isIosLike()) {
                return "ios";
            }

            return "browser";
        }
    };

    if (!("serviceWorker" in navigator)) {
        return;
    }

    window.addEventListener(
        "load",
        async () => {
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
