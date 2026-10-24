import { chromium } from "playwright";
import { createHmac } from "node:crypto";
import assert from "node:assert/strict";
import { mkdir } from "node:fs/promises";
import path from "node:path";

// Virtual browser coverage is NOT physical iPhone/Face ID acceptance.
const baseURL = process.env.FULLWORTH_VISUAL_BASE_URL;
assert.equal(process.env.GITHUB_ACTIONS, "true", "Run only in isolated GitHub CI.");
assert.equal(baseURL, "https://web.localhost", "Never enroll test keys on a public host.");
const email = "ci-passkey-" + process.env.GITHUB_RUN_ID + "@fullworth.local";
const password = "FullWorth!PasskeyCi123";
const outputDir = path.resolve("FullWorth.VisualTests/artifacts");
await mkdir(outputDir, { recursive: true });
function code(secret) {
  const alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";
  const bytes = [];
  let buffer = 0, bits = 0;
  for (const character of secret.replace(/ /g, "")) {
    buffer = (buffer << 5) | alphabet.indexOf(character);
    bits += 5;
    if (bits >= 8) { bits -= 8; bytes.push((buffer >> bits) & 255); }
  }
  const counter = Buffer.alloc(8);
  counter.writeBigInt64BE(BigInt(Math.floor(Date.now() / 30000)));
  const hash = createHmac("sha1", Buffer.from(bytes)).update(counter).digest();
  const offset = hash[hash.length - 1] & 15;
  return String((hash.readUInt32BE(offset) & 0x7fffffff) % 1000000).padStart(6, "0");
}
let stage = "browser launch";
const deadline = setTimeout(() => {
  console.error("Passkey acceptance exceeded 120 seconds at: " + stage);
  process.exit(1);
}, 120000);
function checkpoint(next) {
  stage = next;
  console.log("Passkey acceptance stage: " + next);
}
const browser = await chromium.launch({ args: ["--ignore-certificate-errors"] });
try {
  const context = await browser.newContext({
    baseURL, ignoreHTTPSErrors: true, viewport: { width: 390, height: 844 }
  });
  const apiPost = async (route, body, token) => {
    const response = await context.request.post("https://localhost" + route, {
      data: body, headers: token ? { Authorization: "Bearer " + token } : {}
    });
    assert.ok(response.ok(), "Isolated fixture failed at " + route + ": " + response.status());
    return response;
  };
  await apiPost("/api/auth/register", {
    email, password, acceptedTermsAndPrivacy: true, legalTermsVersion: "2026-09-04-beta"
  });
  const page = await context.newPage();
  page.setDefaultTimeout(15000);
  const enrollmentResponses = [];
  page.on("response", response => {
    const url = new URL(response.url());
    if (url.pathname.startsWith("/bff/account/security/passkeys"))
      enrollmentResponses.push(url.pathname + ":" + response.status());
  });
  const cdp = await context.newCDPSession(page);
  await cdp.send("WebAuthn.enable");
  const { authenticatorId } = await cdp.send("WebAuthn.addVirtualAuthenticator", {
    options: { protocol: "ctap2", transport: "internal", hasResidentKey: true,
      hasUserVerification: true, isUserVerified: true, automaticPresenceSimulation: true }
  });
  async function passwordStep() {
    await page.goto("/login");
    await page.waitForTimeout(900);
    await page.locator('input[name="email"]').fill(email);
    await page.locator('input[name="password"]').fill(password);
    await page.locator('input[name="rememberMe"]').check();
    await Promise.all([
      page.waitForResponse(response => response.url().endsWith("/auth/login") &&
        response.request().method() === "POST"),
      page.locator('.auth-form button[type="submit"]').click()
    ]);
  }
  checkpoint("password enrollment session");
  await passwordStep();
  await page.waitForURL(url => url.pathname === "/app" || url.pathname === "/app/setup");
  await page.locator(".app-shell").waitFor();
  const enrollmentSessionEstablishedAt = Date.now();
  await page.goto("/app/setup");
  await page.locator(".whats-new-dialog").waitFor({ state: "visible" });
  await page.locator(".whats-new-primary").click();
  await page.locator(".whats-new-dialog").waitFor({ state: "hidden" });
  checkpoint("passkey list read recovery");
  // Intercept only an authenticated read on the disposable CI host.
  // Never synthesize registration/removal or change real saved credentials.
  let readMode = "error";
  const keyReadPattern = "**/bff/account/security/passkeys";
  const mockedRead = async route => {
    assert.equal(route.request().method(), "GET");
    if (readMode === "error")
      return route.fulfill({ status: 503, body: "{}" });
    if (readMode === "expired")
      return route.fulfill({ status: 401, body: "{}" });
    if (readMode === "malformed")
      return route.fulfill({ json: { unexpected: "object instead of array" } });
    return route.fulfill({ json: [{
      id: "aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa",
      name: "Synthetic recoverable key", createdAt: "2026-10-01T00:00:00Z"
    }] });
  };
  await page.route(keyReadPattern, mockedRead);
  await page.goto("/app/account/security-keys");
  const management = page.locator('[data-passkeys="manage"]');
  const retryKeys = management.locator('[data-passkey-action="retry"]');
  await page.waitForTimeout(900);
  await retryKeys.waitFor({ state: "visible" });
  assert.equal(await management.getAttribute("data-loaded"), "error");
  assert.equal(await management.locator("[data-passkey-list] li").count(), 0);

  readMode = "malformed";
  await retryKeys.click();
  await management.getByText("FullWorth returned an invalid key list.", { exact: false }).waitFor();
  await retryKeys.waitFor({ state: "visible" });
  assert.equal(await management.locator("[data-passkey-list] li").count(), 0);

  readMode = "success";
  await retryKeys.click();
  await management.getByText("Synthetic recoverable key", { exact: false }).waitFor();
  await retryKeys.waitFor({ state: "hidden" });
  assert.equal(await management.getAttribute("data-loaded"), "true");

  // Simulate a later read on a replacement/hydrated management root.
  // A failed refresh must not erase the last verified list.
  readMode = "error";
  await page.evaluate(() => {
    const current = document.querySelector('[data-passkeys="manage"]');
    const replacement = current.cloneNode(true);
    replacement.removeAttribute("data-loaded");
    current.replaceWith(replacement);
  });
  await retryKeys.waitFor({ state: "visible" });
  assert.equal(await management.locator("[data-passkey-list] li").count(), 1);
  assert.ok((await management.locator("[data-passkey-list]").innerText())
    .includes("Synthetic recoverable key"));

  readMode = "expired";
  await retryKeys.click();
  await management.getByText("Your session expired. Sign in again", { exact: false }).waitFor();
  assert.equal(await management.locator("[data-passkey-list] li").count(), 1);
  await page.unroute(keyReadPattern, mockedRead);

  // Resume the real, owner-scoped API before virtual authenticator enrollment.
  await page.reload({ waitUntil: "domcontentloaded" });
  await page.waitForTimeout(900);
  await page.locator('[data-passkeys="manage"][data-loaded="true"]').waitFor();
  assert.equal(await management.getByText("Synthetic recoverable key", { exact: false }).count(), 0);
  await management.locator('input[name="currentPassword"]').fill(password);
  await management.locator('input[name="keyName"]').fill("CI virtual platform passkey");
  // Deterministically reproduce SSR/hydration replacing the management region
  // while the OS credential request is in flight. WebAuthn and HTTP remain real.
  await page.evaluate(() => {
    const original = navigator.credentials.create.bind(navigator.credentials);
    navigator.credentials.create = async (...args) => {
      navigator.credentials.create = original;
      const credential = await original(...args);
      const root = document.querySelector('[data-passkeys="manage"]');
      const replacement = root.cloneNode(true);
      replacement.removeAttribute("data-loaded");
      replacement.querySelectorAll("input").forEach(input => { input.value = ""; });
      replacement.querySelector("[data-passkey-list]").replaceChildren();
      replacement.querySelector("[data-passkey-status]").textContent = "";
      replacement.querySelector('[data-passkey-action="register"]').disabled = false;
      root.replaceWith(replacement);
      return credential;
    };
  });
  await management.locator('[data-passkey-action="register"]').click();
  try {
    await management.locator("[data-passkey-list]").getByText("CI virtual platform passkey", { exact: false }).waitFor();
  } catch {
    const status = await management.locator("[data-passkey-status]").textContent();
    const count = (await cdp.send("WebAuthn.getCredentials", { authenticatorId })).credentials.length;
    throw new Error("Passkey enrollment did not finish. UI status: " + status +
      "; virtual credential count: " + count + "; HTTP statuses: " + enrollmentResponses.join(","));
  }
  assert.equal((await cdp.send("WebAuthn.getCredentials", { authenticatorId })).credentials.length, 1);

  checkpoint("first-factor passkey");
  // The normal sign-in screen must support a registered passkey as the first
  // factor. No password, saved credential or signed assertion reaches the app
  // except through the existing OS WebAuthn prompt and protected Web BFF.
  await context.clearCookies();
  await page.goto("/login");
  await page.waitForTimeout(900);
  const firstStep = page.locator('[data-passkeys="login"]');
  await firstStep.locator('[data-passkey-action="login"]').waitFor();
  assert.equal(await page.locator('form[action="/auth/login"] input[name="password"]').count(), 1,
    "Password fallback must remain available.");

  // Cancellation cannot submit a password or trap the user on the passkey path.
  await page.evaluate(() => {
    const original = navigator.credentials.get.bind(navigator.credentials);
    navigator.credentials.get = async (...args) => {
      navigator.credentials.get = original;
      throw new DOMException("CI simulated cancellation", "NotAllowedError");
    };
  });
  await firstStep.locator('[data-passkey-action="login"]').click();
  await firstStep.getByText(
    "Cancelled or timed out. You can try again or use your password.", { exact: true }).waitFor();
  assert.equal(await page.locator('form[action="/auth/login"] input[name="password"]').count(), 1);

  // A user who opts into a persistent session should get the same remembered
  // cookie behavior as password sign-in, without keeping tokens in the WebView.
  await page.locator('input[name="rememberMe"]').check();
  const primaryLogin = page.waitForRequest(request =>
    request.url().endsWith("/auth/passkeys/login") && request.method() === "POST");
  const primaryResponse = page.waitForResponse(response =>
    response.url().endsWith("/auth/passkeys/login") && response.status() === 204);
  await firstStep.locator('[data-passkey-action="login"]').click();
  const request = (await primaryLogin).postDataJSON();
  assert.equal(request.rememberMe, true);
  assert.equal(request.twoFactorCode, null);
  assert.equal(Object.hasOwn(request, "currentPassword"), false);
  await primaryResponse;
  await page.waitForURL(url => url.pathname === "/app" || url.pathname === "/app/setup");
  await page.locator(".app-shell").waitFor();

  checkpoint("enable authenticator MFA");
  const bootstrap = await (await apiPost("/api/auth/login", { email, password })).json();
  const setup = await (await apiPost("/api/account/security/two-factor/setup",
    { currentPassword: password }, bootstrap.accessToken)).json();
  await apiPost("/api/account/security/two-factor/enable",
    { currentPassword: password, authenticatorCode: code(setup.sharedKey) }, bootstrap.accessToken);
  // Enrollment, hydration reads and first-factor login share the real API's
  // 20-request/minute authentication budget. Begin the separate MFA scenarios
  // in a fresh window. Keep production rate limits and signature checks intact.
  checkpoint("fresh authentication rate-limit window");
  await page.waitForTimeout(Math.max(0, 61000 - (Date.now() - enrollmentSessionEstablishedAt)));
  await context.clearCookies();
  await passwordStep();
  await page.waitForURL(url => url.searchParams.get("twoFactor") === "true");
  await page.locator("[data-password-passkey-button]").waitFor();
  assert.equal(await page.locator('input[name="password"], input[name="email"]').count(), 0);
  for (const width of [320, 390]) {
    await page.setViewportSize({ width, height: 844 });
    const geometry = await page.evaluate(() => ({
      viewport: document.documentElement.clientWidth,
      width: document.documentElement.scrollWidth,
      outside: [...document.querySelectorAll(".site-header, .header-inner, .header-actions, .header-action, .site-footer, .footer-inner, .auth-page, .auth-shell, .auth-card, .auth-glow")]
        .map(element => ({ name: element.className, right: element.getBoundingClientRect().right }))
    }));
    assert.ok(geometry.width <= geometry.viewport + 1,
      "Verification must not scroll sideways: " + JSON.stringify(geometry));
  }
  await page.screenshot({ path: path.join(outputDir, "password-once-passkey-choice-mobile.png"), fullPage: true });

  checkpoint("MFA cancellation and code fallback");
  // Simulated user cancellation, without replacing signature validation.
  await page.evaluate(() => {
    const original = navigator.credentials.get.bind(navigator.credentials);
    navigator.credentials.get = async (...args) => {
      navigator.credentials.get = original;
      throw new DOMException("CI simulated cancellation", "NotAllowedError");
    };
  });
  await page.locator("[data-password-passkey-button]").click();
  await page.getByText("Cancelled or timed out. You can try again or use an authenticator code.", { exact: true }).waitFor();
  assert.equal(await page.locator('input[name="password"], input[name="email"]').count(), 0);
  await page.locator('input[name="twoFactorCode"]').fill(code(setup.sharedKey));
  await page.locator('form[action="/auth/login"]').first().getByRole("button", { name: "Verify and sign in" }).click();
  await page.waitForURL(url => url.pathname === "/app" || url.pathname === "/app/setup");
  await page.locator(".app-shell").waitFor();
  await context.clearCookies();
  await passwordStep();
  await page.waitForURL(url => url.searchParams.get("twoFactor") === "true");
  checkpoint("MFA passkey continuation");
  const completionRequest = page.waitForRequest(request =>
    request.url().endsWith("/auth/login/passkey-complete") && request.method() === "POST");
  const completionResponse = page.waitForResponse(response =>
    response.url().endsWith("/auth/login/passkey-complete") && response.request().method() === "POST");
  await page.locator("[data-password-passkey-button]").click();
  const completion = (await completionRequest).postDataJSON();
  assert.deepEqual(Object.keys(completion).sort(), ["ceremonyId", "credentialJson"]);
  checkpoint("MFA completion response");
  const completed = await completionResponse;
  assert.equal(completed.status(), 204,
    "Password-verified passkey continuation must retain its existing second-factor behavior.");
  await page.waitForURL(url => url.pathname === "/app" || url.pathname === "/app/setup");
  await page.locator(".app-shell").waitFor();
  const credentials = (await cdp.send("WebAuthn.getCredentials", { authenticatorId })).credentials;
  assert.ok(credentials[0].signCount > 0, "Browser must produce a signed assertion.");
  assert.equal((await context.cookies()).filter(cookie =>
    cookie.name === "__Host-FullWorth.PendingPassword").length, 0);
  const account = await context.request.get(baseURL + "/bff/account/security/passkeys");
  assert.equal(account.status(), 200, "Browser must have an authenticated session.");
  await cdp.send("WebAuthn.removeVirtualAuthenticator", { authenticatorId });
  await context.close();
  console.log("PASS: virtual passkey enrollment, first-factor and password-once sign-in, cancellation/code fallback.");
} finally { await browser.close(); clearTimeout(deadline); }
