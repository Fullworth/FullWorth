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

    function applyProfile(preference) {
        const selected =
            normalizeProfile(
                preference);

        const effective =
            resolveProfile(
                selected);

        document.documentElement.dataset
            .fullworthPerformance =
                effective;

        document.documentElement.dataset
            .fullworthPerformancePreference =
                selected;

        return {
            selected,
            effective
        };
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
        }
    };

    applyProfile(
        getStoredPreference());
})();
