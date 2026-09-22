(() => {
    const storageKey =
        "fullworth.performance-profile";

    const allowedProfiles =
        new Set([
            "auto",
            "efficiency",
            "balanced",
            "high"
        ]);

    const balancedRouteStylePrefetchUrls = [
        "/account-settings.css",
        "/account-transactions.css"
    ];

    const highRouteStylePrefetchUrls = [
        ...balancedRouteStylePrefetchUrls,
        "/account-privacy.css",
        "/subscription.css"
    ];

    let routeStylePrefetchStarted =
        false;

    let routeStylePrefetchScheduled =
        false;

    function normalizeProfile(value) {
        const normalized =
            String(value ?? "")
                .trim()
                .toLowerCase();

        return allowedProfiles.has(normalized)
            ? normalized
            : "auto";
    }

    function getStoredPreference() {
        try {
            return normalizeProfile(
                window.localStorage.getItem(
                    storageKey));
        }
        catch {
            return "auto";
        }
    }

    function detectAutomaticProfile() {
        const connection =
            navigator.connection ??
            navigator.mozConnection ??
            navigator.webkitConnection;

        if (connection?.saveData === true) {
            return "efficiency";
        }

        const effectiveType =
            String(
                connection?.effectiveType ??
                "")
                .toLowerCase();

        if (effectiveType === "slow-2g" ||
            effectiveType === "2g") {
            return "efficiency";
        }

        const deviceMemory =
            Number(
                navigator.deviceMemory ??
                0);

        const hardwareConcurrency =
            Number(
                navigator.hardwareConcurrency ??
                0);

        if ((deviceMemory > 0 &&
             deviceMemory <= 4) ||
            (hardwareConcurrency > 0 &&
             hardwareConcurrency <= 4) ||
            effectiveType === "3g") {
            return "efficiency";
        }

        if ((deviceMemory >= 8 ||
             deviceMemory === 0) &&
            (hardwareConcurrency >= 8 ||
             hardwareConcurrency === 0) &&
            effectiveType !== "3g") {
            return "high";
        }

        return "balanced";
    }

    function resolveProfile(preference) {
        const normalized =
            normalizeProfile(
                preference);

        return normalized === "auto"
            ? detectAutomaticProfile()
            : normalized;
    }

    function getExecutionModeForProfile(
        effectiveProfile) {

        switch (effectiveProfile) {
            case "high":
                return "local";

            case "balanced":
                return "hybrid";

            default:
                return "server";
        }
    }

    function applyProfile(preference) {
        const selected =
            normalizeProfile(
                preference);

        const effective =
            resolveProfile(
                selected);

        const executionMode =
            getExecutionModeForProfile(
                effective);

        document.documentElement.dataset
            .fullworthPerformance =
                effective;

        document.documentElement.dataset
            .fullworthPerformancePreference =
                selected;

        document.documentElement.dataset
            .fullworthExecution =
                executionMode;

        scheduleRouteStylePrefetch();

        return {
            selected,
            effective,
            executionMode
        };
    }

    function getRouteStylePrefetchUrls() {
        switch (
            document.documentElement.dataset
                .fullworthPerformance) {
            case "high":
                return highRouteStylePrefetchUrls;

            case "balanced":
                return balancedRouteStylePrefetchUrls;

            default:
                return [];
        }
    }

    function prefetchRouteStyles() {
        if (routeStylePrefetchStarted) {
            return;
        }

        const urls =
            getRouteStylePrefetchUrls();

        if (urls.length === 0) {
            return;
        }

        routeStylePrefetchStarted =
            true;

        for (const url of urls) {
            const existing =
                document.querySelector(
                    `link[href="${url}"]`);

            if (existing) {
                continue;
            }

            const link =
                document.createElement(
                    "link");

            link.rel =
                "prefetch";

            link.as =
                "style";

            link.href =
                url;

            document.head.appendChild(
                link);
        }
    }

    function runRouteStylePrefetchWhenIdle() {
        const run =
            () => {
                routeStylePrefetchScheduled =
                    false;

                if (
                    document.documentElement.dataset
                        .fullworthPerformance ===
                    "efficiency") {
                    return;
                }

                prefetchRouteStyles();
            };

        if ("requestIdleCallback" in window) {
            window.requestIdleCallback(
                run,
                {
                    timeout:
                        3000
                });

            return;
        }

        window.setTimeout(
            run,
            1500);
    }

    function scheduleRouteStylePrefetch() {
        if (routeStylePrefetchStarted ||
            routeStylePrefetchScheduled ||
            getRouteStylePrefetchUrls().length === 0) {
            return;
        }

        routeStylePrefetchScheduled =
            true;

        if (document.readyState ===
            "complete") {
            runRouteStylePrefetchWhenIdle();
            return;
        }

        window.addEventListener(
            "load",
            runRouteStylePrefetchWhenIdle,
            {
                once:
                    true
            });
    }

    function transactionLoadLimit() {
        switch (
            document.documentElement.dataset
                .fullworthPerformance) {
            case "efficiency":
                return 100;

            case "balanced":
                return 250;

            default:
                return 500;
        }
    }

    window.FullWorthPerformance = {
        getPreference() {
            return applyProfile(
                getStoredPreference());
        },

        savePreference(value) {
            const selected =
                normalizeProfile(
                    value);

            try {
                window.localStorage.setItem(
                    storageKey,
                    selected);
            }
            catch {
                // Device-local preference storage is an enhancement.
            }

            return applyProfile(
                selected);
        },

        getTransactionLoadLimit() {
            return transactionLoadLimit();
        },

        getExecutionMode() {
            const effective =
                document.documentElement.dataset
                    .fullworthPerformance ??
                resolveProfile(
                    getStoredPreference());

            return getExecutionModeForProfile(
                effective);
        }
    };

    applyProfile(
        getStoredPreference());
})();
