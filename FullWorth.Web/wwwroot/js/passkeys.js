(() => {
    "use strict";
    let busy = false;
    const listVersions = new WeakMap();
    const message = (root, text) => { root.querySelector("[data-passkey-status]").textContent = text; };
    const value = (root, name) => root.querySelector('[name="' + name + '"]')?.value || "";
    const bytes = text => Uint8Array.from(atob(text.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
    const encoded = buffer => btoa(String.fromCharCode(...new Uint8Array(buffer)))
        .replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");

    function options(json, creating) {
        if (creating && PublicKeyCredential.parseCreationOptionsFromJSON)
            return PublicKeyCredential.parseCreationOptionsFromJSON(json);
        if (!creating && PublicKeyCredential.parseRequestOptionsFromJSON)
            return PublicKeyCredential.parseRequestOptionsFromJSON(json);
        const result = { ...json, challenge: bytes(json.challenge) };
        if (creating) result.user = { ...json.user, id: bytes(json.user.id) };
        for (const name of ["excludeCredentials", "allowCredentials"])
            if (json[name]) result[name] = json[name].map(c => ({ ...c, id: bytes(c.id) }));
        return result;
    }

    function credentialJSON(credential) {
        if (credential.toJSON) return credential.toJSON();
        const response = { clientDataJSON: encoded(credential.response.clientDataJSON) };
        for (const name of ["attestationObject", "authenticatorData", "signature", "userHandle"])
            if (credential.response[name]) response[name] = encoded(credential.response[name]);
        if (credential.response.getTransports) response.transports = credential.response.getTransports();
        return { id: credential.id, rawId: encoded(credential.rawId), type: credential.type,
            authenticatorAttachment: credential.authenticatorAttachment,
            response, clientExtensionResults: credential.getClientExtensionResults() };
    }


    // Never trust a hidden returnUrl after a browser credential ceremony.
    // Match the server's local-only policy, including nested percent encodings.
    function localLoginDestination(value) {
        if (typeof value !== "string" || !value.startsWith("/") || value.length > 2048)
            return "/app";
        let decoded = value;
        try {
            for (let round = 0; round < 4; round++) {
                if (!decoded.startsWith("/") || decoded.startsWith("//") ||
                    decoded.includes("\\") || /[\x00-\x1f\x7f]/.test(decoded))
                    return "/app";
                const next = decodeURIComponent(decoded);
                if (next === decoded) return value;
                decoded = next;
            }
        } catch {
            return "/app";
        }
        return "/app";
    }

    function continueAfterPasskey() {
        // A GET form is a browser navigation, not a JavaScript URL sink.
        // The authenticated server endpoint revalidates the return target.
        const form = document.createElement("form");
        form.method = "get";
        form.action = "/auth/passkeys/continue";
        form.hidden = true;
        const destination = document.createElement("input");
        destination.type = "hidden";
        destination.name = "returnUrl";
        destination.value = localLoginDestination(
            document.querySelector('input[name="returnUrl"]')?.value);
        form.append(destination);
        document.body.append(form);
        form.submit();
    }

    async function post(path, body) {
        let token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) {
            const csrf = await fetch("/bff/antiforgery", { credentials: "same-origin", cache: "no-store" });
            if (!csrf.ok) throw new Error("Sign in again before changing keys.");
            token = (await csrf.json()).requestToken;
        }
        const response = await fetch(path, { method: "POST", credentials: "same-origin", cache: "no-store",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token },
            body: JSON.stringify(body) });
        if (!response.ok) throw new Error(response.status === 429
            ? "Too many attempts. Wait a minute and try again."
            : "Could not verify this request. Check your credentials and authenticator code, then start again.");
        return response.status === 204 ? null : response.json();
    }

    async function list(root) {
        const version = (listVersions.get(root) || 0) + 1;
        listVersions.set(root, version);
        const retry = root.querySelector('[data-passkey-action="retry"]');
        if (retry) { retry.hidden = true; retry.disabled = true; }
        if (root.isConnected) {
            root.dataset.loaded = "pending";
            message(root, "Checking saved keys…");
        }

        try {
            const response = await fetch("/bff/account/security/passkeys",
                { credentials: "same-origin", cache: "no-store" });
            if (response.status === 401)
                throw new Error("Your session expired. Sign in again to review saved keys.");
            if (!response.ok)
                throw new Error("Could not load your keys. Try again.");
            const keys = await response.json();
            if (!Array.isArray(keys) || keys.length > 200 ||
                keys.some(key => !key || typeof key.id !== "string" || !key.id.trim() ||
                    typeof key.name !== "string" || !key.name.trim() ||
                    typeof key.createdAt !== "string" ||
                    !Number.isFinite(Date.parse(key.createdAt))) ||
                new Set(keys.map(key => key.id)).size !== keys.length)
                throw new Error("FullWorth returned an invalid key list. Try again.");

            // An older request or an abandoned hydration root cannot replace
            // the current account's visible list.
            if (!root.isConnected || listVersions.get(root) !== version) return false;
            const target = root.querySelector("[data-passkey-list]");
            const fragment = document.createDocumentFragment();
            for (const key of keys) {
                const item = document.createElement("li");
                const label = document.createElement("span");
                label.textContent = key.name + " — added " + new Date(key.createdAt).toLocaleDateString();
                const button = document.createElement("button");
                button.type = "button"; button.textContent = "Remove";
                button.dataset.passkeyAction = "remove"; button.dataset.keyId = key.id;
                item.append(label, " ", button); fragment.append(item);
            }
            target.replaceChildren(fragment);
            if (!keys.length) target.textContent = "No keys registered yet.";
            root.dataset.loaded = "true";
            if (retry) retry.hidden = true;
            message(root, keys.length ? "Saved keys loaded." : "No keys registered yet.");
            return true;
        } catch (error) {
            // Never erase previously verified keys because a later read failed.
            if (root.isConnected && listVersions.get(root) === version) {
                root.dataset.loaded = "error";
                if (retry) retry.hidden = false;
                message(root, error instanceof Error
                    ? error.message : "Could not load your keys. Try again.");
            }
            return false;
        } finally {
            if (root.isConnected && listVersions.get(root) === version && retry)
                retry.disabled = false;
        }
    }

    document.addEventListener("click", async event => {
        const button = event.target.closest("[data-passkey-action]");
        const root = button?.closest("[data-passkeys]");
        if (!root || busy) return;
        // Listing keys is read-only; no WebAuthn availability or credentials
        // are needed to recover a failed authenticated GET.
        if (button.dataset.passkeyAction === "retry") {
            busy = true; button.disabled = true;
            try { await list(root); }
            finally { busy = false; button.disabled = false; }
            return;
        }
        if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials) {
            message(root, "This browser does not support passkeys. Use your password instead."); return;
        }
        busy = true; button.disabled = true;
        message(root, "Follow your device or security key instructions…");
        try {
            const action = button.dataset.passkeyAction;
            // Capture user input before the asynchronous OS credential prompt.
            const keyName = value(root, "keyName");
            const credentials = { currentPassword: value(root, "currentPassword"),
                twoFactorCode: value(root, "twoFactorCode") || null };
            if (action === "remove") {
                if (!confirm("Remove this key from FullWorth? Your password and other keys will still work.")) return;
                await post("/bff/account/security/passkeys/remove",
                    { ...credentials, credentialId: button.dataset.keyId });
            } else {
                const creating = action === "register";
                const begin = await post(creating ? "/bff/account/security/passkeys/options" : "/auth/passkeys/options",
                    creating ? credentials : {});
                const credential = await navigator.credentials[creating ? "create" : "get"](
                    { publicKey: options(begin.options, creating) });
                if (!credential) throw new Error("No key was selected. Your account has not changed.");
                const body = { ceremonyId: begin.ceremonyId,
                    credentialJson: JSON.stringify(credentialJSON(credential)),
                    name: keyName,
                    twoFactorCode: creating ? null : value(root, "passkeyTwoFactorCode") || null,
                    rememberMe: !!document.querySelector('[name="rememberMe"]')?.checked };
                await post(creating ? "/bff/account/security/passkeys/register" : "/auth/passkeys/login", body);
                if (!creating) { continueAfterPasskey(); return; }
            }
            if (root.isConnected) {
                if (await list(root))
                    message(root, "Saved. Your password and recovery options still work.");
            } else {
                // Refresh only through the current authenticated BFF session.
                // Do not move credentials or old-operation success messages
                // into a replacement region (possibly a different account).
                document.querySelectorAll('[data-passkeys="manage"]').forEach(current => {
                    void list(current);
                });
            }
        } catch (error) {
            message(root, error?.name === "NotAllowedError"
                ? "Cancelled or timed out. You can try again or use your password."
                : error instanceof Error ? error.message : "Unable to complete this request.");
        } finally {
            const password = root.querySelector('[name="currentPassword"]');
            if (password) password.value = "";
            busy = false; button.disabled = false;
        }
    });
    function initialize() {
        document.querySelectorAll('[data-passkeys="manage"]:not([data-loaded])').forEach(root => {
            root.dataset.loaded = "pending";
            void list(root);
        });
    }
    document.addEventListener("DOMContentLoaded", initialize);
    new MutationObserver(initialize).observe(document.documentElement, { childList: true, subtree: true });
    initialize();
})();
