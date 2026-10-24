import assert from "node:assert/strict";
import { readFileSync } from "node:fs";
import { test } from "node:test";
import { runInNewContext } from "node:vm";

// Exercise actual click handlers after synthetic WebAuthn completion.
// Navigation must submit a same-origin fixed-path GET form, never place
// DOM-derived URL data directly in location.assign. No real credentials used.
async function completedPasskeyRedirect(filename, returnUrl) {
    let handler;
    let submitted;
    const status = { textContent: "" };
    const root = {
        isConnected: true,
        querySelector: selector => selector.includes("status") ? status : null
    };
    const button = {
        disabled: false,
        isConnected: true,
        dataset: { passkeyAction: "login" },
        closest: () => root
    };
    const document = {
        documentElement: {},
        addEventListener: (type, callback) => {
            if (type === "click") handler = callback;
        },
        querySelector: selector => {
            if (selector.includes("__RequestVerificationToken")) return { value: "synthetic-csrf" };
            if (selector.includes("returnUrl")) return { value: returnUrl };
            return null;
        },
        querySelectorAll: () => [],
        body: { append: () => {} },
        createElement: tag => {
            if (tag === "input") return { type: "", name: "", value: "" };
            assert.equal(tag, "form");
            return {
                method: "", action: "", hidden: false, fields: [],
                append(input) { this.fields.push(input); },
                submit() {
                    submitted = {
                        action: this.action, method: this.method, hidden: this.hidden,
                        destination: this.fields.find(input => input.name === "returnUrl")?.value
                    };
                }
            };
        }
    };
    const PublicKeyCredential = { parseRequestOptionsFromJSON: json => json };
    const response = {
        ok: true,
        status: 200,
        json: async () => ({ ceremonyId: "synthetic-ceremony", options: { challenge: "test" } })
    };
    const context = {
        document,
        window: { isSecureContext: true, PublicKeyCredential },
        PublicKeyCredential,
        navigator: { credentials: { get: async () => ({ toJSON: () => ({ type: "public-key" }) }) } },
        fetch: async () => response,
        location: { assign: () => { throw new Error("Unsafe JavaScript URL sink"); } },
        MutationObserver: class { observe() {} },
        confirm: () => true,
        Uint8Array,
        console
    };
    const source = readFileSync(new URL("../FullWorth.Web/wwwroot/js/" + filename, import.meta.url), "utf8");
    runInNewContext(source, context, { filename });
    assert.equal(typeof handler, "function", filename + " must register its passkey handler");
    await handler({ target: { closest: () => button } });
    assert.ok(submitted, filename + " must submit a browser navigation after WebAuthn: " + status.textContent);
    assert.equal(submitted.action, "/auth/passkeys/continue");
    assert.equal(submitted.method, "get");
    assert.equal(submitted.hidden, true);
    return submitted.destination;
}

const cases = [
    ["/app", "/app"],
    ["/app?tab=bills", "/app?tab=bills"],
    ["/app/inbox%2Ftoday", "/app/inbox%2Ftoday"],
    ["javascript:alert(1)", "/app"],
    ["https://outside.example/steal", "/app"],
    ["//outside.example", "/app"],
    ["/\\outside.example", "/app"],
    ["/%2f%2foutside.example", "/app"],
    ["/%252f%252foutside.example", "/app"],
    ["/%5coutside.example", "/app"],
    ["/%255coutside.example", "/app"],
    ["/%0d%0aLocation:outside", "/app"],
    ["/app\u0000bad", "/app"],
    ["/incomplete%", "/app"],
    ["/%252525252f", "/app"],
    ["", "/app"]
];

for (const filename of ["passkeys.js", "password-step.js"]) {
    test(filename + " accepts only local destinations after WebAuthn", async () => {
        for (const [input, expected] of cases) {
            assert.equal(await completedPasskeyRedirect(filename, input), expected,
                filename + " unsafe return URL: " + JSON.stringify(input));
        }
    });
}
