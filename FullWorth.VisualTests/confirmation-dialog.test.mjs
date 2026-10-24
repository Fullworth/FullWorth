import assert from "node:assert/strict";
import { createServer } from "node:http";
import { readFile, mkdir } from "node:fs/promises";
import { chromium, webkit } from "playwright";
import path from "node:path";

const root = path.resolve("FullWorth.Web/wwwroot");
const output = path.resolve("FullWorth.VisualTests/artifacts/confirmation");
await mkdir(output, { recursive: true });
const allowed = new Set(["/js/bff.js", "/js/plaid-completion.js", "/js/login-redirect.js", "/js/confirmation-dialog.js", "/confirmation-dialog.css", "/theme.css"]);
const fixture = `<!doctype html><html data-theme="dark"><head>
<meta name="viewport" content="width=device-width,initial-scale=1">
<link rel="stylesheet" href="/theme.css"><link rel="stylesheet" href="/confirmation-dialog.css">
</head><body><main><h1>Connected banks</h1><p>Example Credit Union</p>
<button id="trigger">Disconnect</button></main><script type="module">
import { confirmBankDisconnect } from "/js/bff.js";
window.message = "Disconnect Example Credit Union? FullWorth will stop syncing this bank connection.";
window.mutations = 0;
window.ask = () => confirmBankDisconnect("Disconnect bank?", window.message, "Keep connected", "Disconnect bank");
document.querySelector("#trigger").onclick = () => {
    window.result = null;
    window.ask().then(accepted => { window.result = accepted; if (accepted) window.mutations++; });
};
window.ready = true;
</script></body></html>`;
const server = createServer(async (request, response) => {
    const url = new URL(request.url, "http://localhost");
    if (url.pathname === "/") { response.setHeader("Content-Type", "text/html"); response.end(fixture); return; }
    if (!allowed.has(url.pathname)) { response.writeHead(404); response.end(); return; }
    try {
        const body = await readFile(path.join(root, url.pathname.slice(1)));
        response.setHeader("Content-Type", url.pathname.endsWith(".js") ? "text/javascript" : "text/css");
        response.end(body);
    } catch { response.writeHead(500); response.end(); }
});
await new Promise(resolve => server.listen(0, "127.0.0.1", resolve));
const origin = "http://127.0.0.1:" + server.address().port;
let cases = 0;
try {
    const account = await readFile("FullWorth.Web/Components/Pages/App/Account.razor", "utf8");
    assert.ok(account.includes('"confirmBankDisconnect"'));
    assert.ok(!account.includes('"confirm",'));
    assert.ok(account.includes("_isConfirmingDisconnect"));
    assert.ok(account.indexOf("if (!confirmed) return;") < account.indexOf('"disconnectBankConnection"'));

    for (const [name, engine] of [["chromium", chromium], ["webkit", webkit]]) {
        const browser = await engine.launch({ headless: true });
        try {
            for (const theme of ["dark", "light"]) {
                const page = await browser.newPage({ viewport: { width: 390, height: 844 } });
                let nativeDialogs = 0;
                page.on("dialog", async dialog => { nativeDialogs++; await dialog.dismiss(); });
                await page.goto(origin);
                await page.waitForFunction(() => window.ready === true);
                await page.evaluate(value => document.documentElement.dataset.theme = value, theme);
                async function open() {
                    await page.focus("#trigger");
                    await page.click("#trigger");
                    await page.locator("dialog[open]").waitFor();
                }
                async function result(expected) {
                    await page.waitForFunction(value => window.result === value, expected);
                    assert.equal(await page.locator("dialog").count(), 0);
                    cases++;
                }

                await open();
                assert.equal(await page.locator("dialog h2").innerText(), "Disconnect bank?");
                assert.ok(await page.locator("dialog").evaluate(d => d.matches(":modal")));
                assert.equal(await page.evaluate(() => document.activeElement.className), "fw-confirm-cancel");
                const bounds = await page.locator("dialog").boundingBox();
                assert.ok(bounds.x >= 0 && bounds.y >= 0 && bounds.x + bounds.width <= 391 && bounds.y + bounds.height <= 845);
                await page.screenshot({ path: path.join(output, name + "-" + theme + ".png"), animations: "disabled" });
                await page.keyboard.press("Shift+Tab");
                assert.equal(await page.evaluate(() => document.activeElement.className), "fw-confirm-destructive");
                await page.keyboard.press("Tab");
                assert.equal(await page.evaluate(() => document.activeElement.className), "fw-confirm-cancel");
                await page.click(".fw-confirm-cancel");
                await result(false);
                assert.equal(await page.evaluate(() => document.activeElement.id), "trigger");
                assert.equal(await page.evaluate(() => window.mutations), 0);

                await open(); await page.keyboard.press("Escape"); await result(false);
                await open(); await page.mouse.click(2, 2); await result(false);
                await open();
                assert.equal(await page.evaluate(() => window.ask()), false);
                assert.equal(await page.locator("dialog").count(), 1);
                await page.click(".fw-confirm-cancel"); await result(false);

                await page.evaluate(() => window.message = "<img src=x onerror='window.injected=true'>");
                await open();
                assert.equal(await page.locator("dialog img").count(), 0);
                assert.ok((await page.locator("#fw-confirm-message").textContent()).startsWith("<img"));
                await page.click(".fw-confirm-cancel"); await result(false);
                assert.equal(await page.evaluate(() => Boolean(window.injected)), false);

                await open();
                await page.evaluate(() => document.querySelector("dialog").close());
                await result(false);
                await open();
                await page.evaluate(() => window.dispatchEvent(new Event("pagehide")));
                await result(false);
                await open();
                await page.evaluate(() => window.dispatchEvent(new Event("popstate")));
                await result(false);

                await open(); await page.click(".fw-confirm-destructive"); await result(true);
                assert.equal(await page.evaluate(() => window.mutations), 1);
                assert.equal(nativeDialogs, 0);
                await page.close();
            }
            const unsupported = await browser.newPage();
            await unsupported.goto(origin);
            await unsupported.waitForFunction(() => window.ready === true);
            assert.equal(await unsupported.evaluate(async () => {
                HTMLDialogElement.prototype.showModal = undefined;
                try { await window.ask(); return false; } catch { return true; }
            }), true);
            assert.equal(await unsupported.evaluate(() => window.mutations), 0);
            cases++;
            await unsupported.close();
        } finally { await browser.close(); }
    }
    console.log("Confirmation dialog browser regression passed: " + cases + " cases.");
} finally {
    await new Promise(resolve => server.close(resolve));
}
