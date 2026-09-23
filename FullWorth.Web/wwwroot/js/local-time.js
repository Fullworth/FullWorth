const storageKey = "billwatch.timestamp-display-mode";
const themeStorageKey = "billwatch-theme";
const experienceThemeInitializedKey = "fullworth.experience-theme-initialized";
const localMode = "Local12Hour";
const utcMode = "Utc";

let observer = null;

function normalizeThemePreference(value) {
    switch (String(value ?? "").toLowerCase()) {
        case "light":
            return "Light";
        case "dark":
            return "Dark";
        default:
            return "System";
    }
}

function normalizeTextSizePreference(value) {
    switch (String(value ?? "").toLowerCase()) {
        case "large":
            return "Large";
        case "extralarge":
        case "extra-large":
            return "ExtraLarge";
        default:
            return "Standard";
    }
}

function resolveTheme(preference) {
    const normalized = normalizeThemePreference(preference);

    if (normalized === "Light") {
        return "light";
    }

    if (normalized === "Dark") {
        return "dark";
    }

    return window.matchMedia?.("(prefers-color-scheme: light)")?.matches
        ? "light"
        : "dark";
}

function ensureExperienceAccessibilityStyles() {
    if (document.getElementById("fullworth-experience-preferences")) {
        return;
    }

    const style = document.createElement("style");
    style.id = "fullworth-experience-preferences";
    style.textContent = `
        html[data-fullworth-text-size="large"] { font-size: 112.5%; }
        html[data-fullworth-text-size="extra-large"] { font-size: 125%; }

        html[data-fullworth-high-contrast="true"][data-theme="dark"] {
            --bw-text-muted: #c3ccda;
            --bw-text-subtle: #aab7ca;
            --bw-border: rgba(255, 255, 255, 0.18);
            --bw-border-strong: rgba(255, 255, 255, 0.28);
        }

        html[data-fullworth-high-contrast="true"][data-theme="light"] {
            --bw-text-muted: #475467;
            --bw-text-subtle: #5d6878;
            --bw-border: rgba(15, 23, 42, 0.18);
            --bw-border-strong: rgba(15, 23, 42, 0.28);
        }

        html[data-fullworth-reduce-motion="true"] *,
        html[data-fullworth-reduce-motion="true"] *::before,
        html[data-fullworth-reduce-motion="true"] *::after {
            animation-duration: 0.01ms !important;
            animation-iteration-count: 1 !important;
            scroll-behavior: auto !important;
            transition-duration: 0.01ms !important;
        }
    `;

    document.head.appendChild(style);
}

function applyThemePreference(preference, force = false) {
    if (!force &&
        window.localStorage.getItem(experienceThemeInitializedKey) === "1") {
        return;
    }

    const theme = resolveTheme(preference);

    window.localStorage.setItem(themeStorageKey, theme);
    window.localStorage.setItem(experienceThemeInitializedKey, "1");

    document.documentElement.dataset.theme = theme;
    document.documentElement.style.colorScheme = theme;

    const themeColor =
        document.querySelector('meta[name="theme-color"]');

    if (themeColor) {
        themeColor.setAttribute(
            "content",
            theme === "light"
                ? "#F7F8FB"
                : "#0B1F3B");
    }
}

function applyLanguagePreference(language) {
    const uiCulture =
        String(language ?? "").toLowerCase().startsWith("es")
            ? "es"
            : "en-US";

    const cookieValue =
        encodeURIComponent(
            `c=en-US|uic=${uiCulture}`);

    document.cookie =
        `.AspNetCore.Culture=${cookieValue}; Path=/; Max-Age=31536000; SameSite=Lax; Secure`;
}

export function applyExperiencePreferences(preference, forceTheme = false) {
    if (!preference?.experienceSetupComplete) {
        return;
    }

    ensureExperienceAccessibilityStyles();

    const textSize =
        normalizeTextSizePreference(
            preference.textSizePreference);

    document.documentElement.dataset.fullworthTextSize =
        textSize === "ExtraLarge"
            ? "extra-large"
            : textSize.toLowerCase();

    document.documentElement.dataset.fullworthHighContrast =
        preference.highContrastEnabled
            ? "true"
            : "false";

    document.documentElement.dataset.fullworthReduceMotion =
        preference.reduceMotionEnabled
            ? "true"
            : "false";

    applyThemePreference(
        preference.themePreference,
        forceTheme);

    applyLanguagePreference(
        preference.preferredUiLanguage);
}

function normalizeMode(value) {
    return value === utcMode
        ? utcMode
        : localMode;
}

function getStoredMode() {
    return normalizeMode(
        window.localStorage.getItem(storageKey));
}

function setStoredMode(mode) {
    window.localStorage.setItem(
        storageKey,
        normalizeMode(mode));
}

function parseDate(value) {
    if (!value) {
        return null;
    }

    const date = new Date(value);

    return Number.isNaN(date.getTime())
        ? null
        : date;
}

function pad(value) {
    return String(value).padStart(2, "0");
}

function formatUtc(date, includeTime = true) {
    const dateText =
        `${pad(date.getUTCMonth() + 1)}/${pad(date.getUTCDate())}/${date.getUTCFullYear()}`;

    if (!includeTime) {
        return `${dateText} UTC`;
    }

    return `${dateText} ${pad(date.getUTCHours())}:${pad(date.getUTCMinutes())} UTC`;
}

function formatLocal(date, includeTime = true) {
    const dateText =
        `${pad(date.getMonth() + 1)}/${pad(date.getDate())}/${date.getFullYear()}`;

    if (!includeTime) {
        return dateText;
    }

    const hours = date.getHours();
    const displayHour = hours % 12 || 12;
    const period = hours >= 12 ? "PM" : "AM";

    return `${dateText} ${displayHour}:${pad(date.getMinutes())} ${period}`;
}

function formatByMode(value, includeTime = true) {
    const date = parseDate(value);

    if (!date) {
        return null;
    }

    return getStoredMode() === utcMode
        ? formatUtc(date, includeTime)
        : formatLocal(date, includeTime);
}

function renderTimestampElement(element) {
    const value = element.getAttribute("datetime") ??
        element.dataset.bwTimestamp;
    const formatted = formatByMode(value, true);

    if (formatted) {
        element.textContent = formatted;
    }
}

export function renderTimestamps(root = document) {
    if (root instanceof Element &&
        (root.matches("time[datetime]") ||
         root.matches("[data-bw-timestamp]"))) {
        renderTimestampElement(root);
    }

    root.querySelectorAll?.("time[datetime], [data-bw-timestamp]")
        .forEach(renderTimestampElement);
}

export async function getTimestampPreference() {
    const response = await fetch(
        "/bff/account/preferences",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!response.ok) {
        throw new Error("Could not load timestamp preference.");
    }

    return await response.json();
}

export function getExperiencePreferences() {
    return getTimestampPreference();
}

export async function saveExperiencePreferences(preference) {
    const antiforgeryResponse = await fetch(
        "/bff/antiforgery",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!antiforgeryResponse.ok) {
        throw new Error("Could not initialize secure experience setup.");
    }

    const antiforgery =
        await antiforgeryResponse.json();

    const response = await fetch(
        "/bff/account/preferences/experience",
        {
            method: "PUT",
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": antiforgery.requestToken
            },
            body: JSON.stringify(preference)
        });

    if (!response.ok) {
        throw new Error("Could not save your FullWorth setup.");
    }

    const saved = await response.json();

    applyExperiencePreferences(
        saved,
        true);

    return saved;
}

export async function saveTimestampPreference(mode) {
    const normalizedMode = normalizeMode(mode);

    const antiforgeryResponse = await fetch(
        "/bff/antiforgery",
        {
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json"
            }
        });

    if (!antiforgeryResponse.ok) {
        throw new Error("Could not initialize secure preference update.");
    }

    const antiforgery = await antiforgeryResponse.json();

    const response = await fetch(
        "/bff/account/preferences",
        {
            method: "PUT",
            credentials: "same-origin",
            cache: "no-store",
            headers: {
                Accept: "application/json",
                "Content-Type": "application/json",
                "X-CSRF-TOKEN": antiforgery.requestToken
            },
            body: JSON.stringify({
                timestampDisplayMode: normalizedMode
            })
        });

    if (!response.ok) {
        throw new Error("Could not save timestamp preference.");
    }

    const saved = await response.json();
    setStoredMode(saved.timestampDisplayMode);
    renderTimestamps(document);

    return saved;
}

export async function initializeTimestampPreferences() {
    try {
        const preference = await getTimestampPreference();
        setStoredMode(preference.timestampDisplayMode);
        applyExperiencePreferences(preference);
    }
    catch {
        if (window.localStorage.getItem(storageKey) === null) {
            setStoredMode(localMode);
        }
    }

    renderTimestamps(document);

    if (observer) {
        observer.disconnect();
    }

    observer = new MutationObserver(mutations => {
        for (const mutation of mutations) {
            mutation.addedNodes.forEach(node => {
                if (node instanceof Element) {
                    renderTimestamps(node);
                }
            });
        }
    });

    observer.observe(
        document.body,
        {
            childList: true,
            subtree: true
        });
}

export function formatLocalDate(value) {
    return formatByMode(value, false);
}

export function formatLocalDateTime(value) {
    return formatByMode(value, true);
}

export function formatConnectionTimestamps(entries) {
    if (!Array.isArray(entries)) {
        return [];
    }

    return entries.map(entry => [
        formatByMode(entry?.[0], false),
        entry?.[1]
            ? formatByMode(entry[1], true)
            : null
    ]);
}
