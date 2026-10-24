import assert from "node:assert/strict";
import { readFile } from "node:fs/promises";

export async function verifyAccountSecurityReadRecovery() {
  const source = await readFile(new URL("../FullWorth.Web/wwwroot/js/account-security.js", import.meta.url), "utf8");
  const originals = Object.fromEntries(["fetch", "window", "document"].map(key => [key, globalThis[key]]));
  const calls = [], redirects = [];
  let reply = () => new Response(JSON.stringify({ linkedProviders: [] }), { status: 200, headers: { "Content-Type": "application/json" } });
  globalThis.window = { location: { assign: target => redirects.push(target) } };
  globalThis.document = { querySelector: () => null, querySelectorAll: () => [] };
  globalThis.fetch = async (url, options) => {
    calls.push({ url, options });
    if (url === "/bff/antiforgery") return new Response(JSON.stringify({ requestToken: "ci-only-token" }), {
      status: 200, headers: { "Content-Type": "application/json" } });
    return reply();
  };
  try {
    const module = await import("data:text/javascript;base64," + Buffer.from(source).toString("base64"));
    // Allow the module's optional linked-provider UI bootstrap to finish.
    await new Promise(resolve => setImmediate(resolve));
    redirects.length = 0; calls.length = 0;
    for (const operation of [module.getAccountSecurity, module.getExternalIdentityStatus]) {
      reply = () => new Response(null, { status: 401 });
      await assert.rejects(operation, /Session expired/);
      assert.deepEqual(redirects, ["/login"]);
      redirects.length = 0;
    }
    for (const code of [403, 429, 500, 503]) {
      reply = () => new Response(JSON.stringify({ title: "PRIVATE-READ-CANARY" }), {
        status: code, headers: { "Content-Type": "application/json" } });
      await assert.rejects(module.getAccountSecurity, error => !error.message.includes("PRIVATE-"));
      assert.deepEqual(redirects, [], "Non-401 read failures must not sign the user out.");
    }
    reply = () => { throw new Error("PRIVATE-NETWORK-CANARY"); };
    await assert.rejects(module.getAccountSecurity, /FullWorth could not load account security settings/);
    reply = () => new Response("not-json", { status: 200 });
    await assert.rejects(module.getAccountSecurity, /FullWorth could not load account security settings/);
    assert.deepEqual(redirects, []);

    // Wrong current credentials are not evidence of an expired Web session.
    reply = () => new Response(JSON.stringify({ title: "The current password is incorrect." }), {
      status: 401, headers: { "Content-Type": "application/json" } });
    await assert.rejects(() => module.requestEmailChange("synthetic-proof", "synthetic@example.test", "123456"),
      /Your current password is incorrect/);
    assert.deepEqual(redirects, []);
    const mutation = calls.find(call => call.options.method === "POST");
    assert.equal(mutation.url, "/bff/account/security/email");
    assert.equal(mutation.options.headers["X-CSRF-TOKEN"], "ci-only-token");
    assert.equal(mutation.options.credentials, "same-origin");
    assert.equal(mutation.options.cache, "no-store");

    const expected = { email: "synthetic@example.test", emailConfirmed: true, twoFactorEnabled: true };
    reply = () => new Response(JSON.stringify(expected), { status: 200, headers: { "Content-Type": "application/json" } });
    assert.deepEqual(await module.getAccountSecurity(), expected);
    const reads = calls.filter(call => call.url === "/bff/account/security");
    assert.ok(reads.every(call => call.options.credentials === "same-origin" &&
      call.options.cache === "no-store" && call.options.headers.Accept === "application/json"));
    assert.deepEqual(redirects, []);
    console.log("Account security read recovery passed: GET 401 redirects, other failures stay safe, POST proof failures preserve the session and CSRF.");
  } finally {
    for (const [key, value] of Object.entries(originals)) {
      if (value === undefined) delete globalThis[key]; else globalThis[key] = value;
    }
  }
}
