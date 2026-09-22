(() => {
    let deferredInstallPrompt = null;
    let serviceWorkerRegistration = null;
    let updateCallback = null;
    let updateAvailable = false;

    function announceUpdate() {
        if (updateAvailable) {
            return;
        }

        updateAvailable = true;

        if (updateCallback) {
            void updateCallback.invokeMethodAsync(
                "OnPwaUpdateAvailableAsync");
        }
    }

    function watchRegistration(registration) {
        serviceWorkerRegistration = registration;

        if (registration.waiting &&
            navigator.serviceWorker.controller) {
            announceUpdate();
        }

        registration.addEventListener(
            "updatefound",
            () => {
                const worker = registration.installing;

                if (!worker) {
                    return;
                }

                worker.addEventListener(
                    "statechange",
                    () => {
                        if (worker.state === "installed" &&
                            navigator.serviceWorker.controller) {
                            announceUpdate();
                        }
                    });
            });
    }

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

        setUpdateCallback(callback) {
            updateCallback = callback;

            if (updateAvailable && updateCallback) {
                void updateCallback.invokeMethodAsync(
                    "OnPwaUpdateAvailableAsync");
            }
        },

        async applyUpdate() {
            const registration =
                serviceWorkerRegistration;

            if (!registration) {
                return "unavailable";
            }

            if (!registration.waiting) {
                await registration.update();
            }

            const waitingWorker =
                registration.waiting;

            if (!waitingWorker) {
                return "unavailable";
            }

            return await new Promise(resolve => {
                let settled = false;

                const finish = result => {
                    if (settled) {
                        return;
                    }

                    settled = true;
                    resolve(result);
                };

                navigator.serviceWorker.addEventListener(
                    "controllerchange",
                    () => {
                        finish("reloaded");
                        window.location.reload();
                    },
                    { once: true });

                waitingWorker.postMessage({
                    type: "SKIP_WAITING"
                });

                window.setTimeout(
                    () => finish("timeout"),
                    10000);
            });
        },

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
                const registration =
                    await navigator.serviceWorker.register(
                    "/service-worker.js",
                    {
                        scope: "/",
                        updateViaCache: "none"
                    });

                watchRegistration(registration);
            }
            catch {
                // PWA registration is progressive enhancement.
                // A registration failure must never block FullWorth.
            }
        });
})();
