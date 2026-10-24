(() => {
    let busy = false;
    const bytes = value => Uint8Array.from(atob(value.replace(/-/g, "+").replace(/_/g, "/")), c => c.charCodeAt(0));
    const encode = value => btoa(String.fromCharCode(...new Uint8Array(value)))
        .replaceAll("+", "-").replaceAll("/", "_").replace(/=+$/, "");
    function options(json) {
        if (PublicKeyCredential.parseRequestOptionsFromJSON)
            return PublicKeyCredential.parseRequestOptionsFromJSON(json);
        return { ...json, challenge: bytes(json.challenge),
            ...(json.allowCredentials ? { allowCredentials: json.allowCredentials.map(c => ({ ...c, id: bytes(c.id) })) } : {}) };
    }
    function credentialJSON(key) {
        if (key.toJSON) return key.toJSON();
        return { id: key.id, rawId: encode(key.rawId), type: key.type,
            authenticatorAttachment: key.authenticatorAttachment,
            response: { clientDataJSON: encode(key.response.clientDataJSON),
                authenticatorData: encode(key.response.authenticatorData),
                signature: encode(key.response.signature),
                userHandle: key.response.userHandle ? encode(key.response.userHandle) : null },
            clientExtensionResults: key.getClientExtensionResults() };
    }
    async function post(path, body) {
        const token = document.querySelector('input[name="__RequestVerificationToken"]')?.value;
        if (!token) throw new Error("Your sign-in expired. Start again.");
        const response = await fetch(path, { method: "POST", credentials: "same-origin", cache: "no-store",
            headers: { "Content-Type": "application/json", "X-CSRF-TOKEN": token }, body: JSON.stringify(body) });
        if (!response.ok) throw new Error(response.status === 429
            ? "Too many attempts. Wait a minute and try again."
            : "Unable to verify this passkey. Use an authenticator code or start sign-in again.");
        return response.status === 204 ? null : response.json();
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

    document.addEventListener("click", async event => {
        const button = event.target.closest("[data-password-passkey-button]");
        const root = button?.closest("[data-password-passkey]");
        if (!root || busy) return;
        const status = root.querySelector("[data-password-passkey-status]");
        if (!window.isSecureContext || !window.PublicKeyCredential || !navigator.credentials) {
            status.textContent = "This browser does not support passkeys. Use your authenticator code instead.";
            return;
        }
        busy = true;
        button.disabled = true;
        status.textContent = "Follow your device or security key instructions…";
        try {
            const begin = await post("/auth/login/passkey-options", {});
            const credential = await navigator.credentials.get({ publicKey: options(begin.options) });
            if (!credential) throw new Error("No passkey was selected. You can still use an authenticator code.");
            await post("/auth/login/passkey-complete", { ceremonyId: begin.ceremonyId,
                credentialJson: JSON.stringify(credentialJSON(credential)) });
            continueAfterPasskey();
        } catch (error) {
            status.textContent = error?.name === "NotAllowedError"
                ? "Cancelled or timed out. You can try again or use an authenticator code."
                : error instanceof Error ? error.message : "Unable to verify this sign-in.";
        } finally {
            busy = false;
            if (button.isConnected) button.disabled = false;
        }
    });
})();
