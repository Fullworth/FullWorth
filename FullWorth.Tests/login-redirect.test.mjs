import test from "node:test";
import assert from "node:assert/strict";
import { loginRedirectUrl } from "../FullWorth.Web/wwwroot/js/login-redirect.js";
import { getBankTransactionsStream } from "../FullWorth.Web/wwwroot/js/transaction-stream.js";
import { getAdminUsers, setAdminProgram } from "../FullWorth.Web/wwwroot/js/admin-bff.js";

test("expiry redirects preserve local path query and section, with safe fallback", () => {
    assert.equal(loginRedirectUrl({ pathname: "/app/planning", search: "?tab=history", hash: "#recent" }),
        "/login?ReturnUrl=%2Fapp%2Fplanning%3Ftab%3Dhistory%23recent");
    for (const pathname of [undefined, "//evil.example.test", "https://evil.example.test", "/\\evil"])
        assert.equal(loginRedirectUrl({ pathname }), "/login");
});
test("real transaction and admin read modules retain the task on expired session", async () => {
    const previousWindow = globalThis.window, previousFetch = globalThis.fetch;
    try {
        let destination;
        globalThis.window = { location: { pathname: "/app/account/transactions", search: "?account=synthetic", hash: "",
            assign: value => { destination = value; } } };
        globalThis.fetch = async () => new Response(null, { status: 401 });
        await assert.rejects(getBankTransactionsStream(100), /session expired/i);
        assert.equal(destination, "/login?ReturnUrl=%2Fapp%2Faccount%2Ftransactions%3Faccount%3Dsynthetic");
        window.location.pathname = "/app/admin"; window.location.search = "";
        await assert.rejects(getAdminUsers(0, 50), /session expired/i);
        assert.equal(destination, "/login?ReturnUrl=%2Fapp%2Fadmin");
    } finally { globalThis.window = previousWindow; globalThis.fetch = previousFetch; }
});
test("admin credential rejection still preserves the signed-in session", async () => {
    const previousWindow = globalThis.window, previousFetch = globalThis.fetch;
    try {
        globalThis.window = { location: { assign: () => assert.fail("proof failure must not sign out") } };
        globalThis.fetch = async url => url === "/bff/antiforgery" ?
            Response.json({ requestToken: "synthetic-proof-token" }) :
            Response.json({ title: "Current password is incorrect." }, { status: 401 });
        await assert.rejects(setAdminProgram("synthetic-target", "BetaTester",
            { isActive: true, currentPassword: "Synthetic!Proof123" }), /password is incorrect/i);
    } finally { globalThis.window = previousWindow; globalThis.fetch = previousFetch; }
});
