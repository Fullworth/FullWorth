import { verifyAccountSecurityReadRecovery } from "./account-security-reads.test.mjs";
import assert from "node:assert/strict";
import path from "node:path";

// Real authenticated components in the disposable CI stack. All reads below
// and every profile/billing mutation are synthetic intercepted responses.
export async function verifyAccountPageRecovery(page, outputDir) {
  assert.ok(new URL(page.url()).hostname.endsWith(".localhost"), "Account recovery requires isolated CI.");
  await verifyAccountSecurityReadRecovery();
  const counts = new Map(), writes = [], failures = new Set(), malformed = new Set();
  let displayName = "Synthetic profile", profileMode = "ok", statusMode = "ok", plansMode = "ok";
  let active = false, paid = false, billing = true, redeemMode = "lost", holdProfile, releaseProfile, holdCheckout, releaseCheckout;
  let releaseRead, holdRead = false;
  const security = () => ({ displayName, email: "synthetic-account@example.test", emailConfirmed: true,
    twoFactorEnabled: true, hasAuthenticatorKey: true, recoveryCodesLeft: 3 });
  const status = () => ({ isActive: active, tier: active ? "FullWorth" : null, startsAtUtc: null,
    endsAtUtc: null, source: active ? "Synthetic fixture" : null, billingAvailable: billing,
    isPaid: paid, billingInterval: paid ? "monthly" : null, cancelAtPeriodEnd: false, providerStatus: null });
  const count = endpoint => counts.get(endpoint) || 0;
  const pattern = /\/bff\/(?:account\/(?:security(?:\/profile)?|preferences)|subscription(?:\/.*)?)$/;
  const handler = async route => {
    const request = route.request(), endpoint = new URL(request.url()).pathname;
    counts.set(endpoint, count(endpoint) + 1);
    if (request.method() !== "GET") {
      writes.push({ endpoint, method: request.method(), body: request.postData() ? request.postDataJSON() : null });
      if (endpoint === "/bff/account/security/profile") {
        const name = request.postDataJSON().displayName || "";
        if (profileMode === "hold") await new Promise(resolve => { releaseProfile = resolve; });
        if (profileMode !== "reject") displayName = name;
        if (profileMode === "lost" || profileMode === "hold" || profileMode === "reject")
          return route.fulfill({ status: 503, json: { title: "PRIVATE-PROFILE-CANARY" } }).catch(() => {});
        return route.fulfill({ json: profileMode === "malformed" ? {} : security() });
      }
      if (endpoint === "/bff/subscription/access-keys/redeem") {
        if (redeemMode !== "inactive") active = true;
        return route.fulfill(redeemMode === "lost"
          ? { status: 503, json: { title: "PRIVATE-REDEMPTION-CANARY" } }
          : { json: { entitlementId: "ci-only", tier: "FullWorth", endsAtUtc: null } });
      }
      if (endpoint === "/bff/subscription/checkout") {
        if (holdCheckout) await new Promise(resolve => { releaseCheckout = resolve; });
        return route.fulfill({ status: 503, json: { title: "PRIVATE-CHECKOUT-CANARY" } }).catch(() => {});
      }
      if (endpoint === "/bff/subscription/billing-portal")
        return route.fulfill({ status: 503, json: { title: "PRIVATE-PORTAL-CANARY" } });
      if (endpoint === "/bff/subscription/sync") return route.fulfill({ json: { succeeded: true } });
      throw new Error("Unexpected account fixture write: " + endpoint);
    }
    if (endpoint === "/bff/account/security" && holdRead)
      await new Promise(resolve => { releaseRead = resolve; });
    if (failures.has(endpoint)) return route.fulfill({ status: 503, json: { title: "PRIVATE-READ-CANARY" } }).catch(() => {});
    if (malformed.has(endpoint)) return route.fulfill({ json: {} }).catch(() => {});
    if (endpoint === "/bff/account/security") return route.fulfill({ json: security() }).catch(() => {});
    if (endpoint === "/bff/account/preferences")
      return route.fulfill({ json: { timestampDisplayMode: "Utc" } });
    if (endpoint === "/bff/subscription")
      return route.fulfill(statusMode === "failed" ? { status: 503, json: {} }
        : { json: statusMode === "malformed" ? {} : status() });
    if (endpoint === "/bff/subscription/plans")
      return route.fulfill(plansMode === "failed" ? { status: 503, json: {} }
        : { json: plansMode === "empty" ? [] : [
          { billingInterval: "monthly", unitAmount: 900, currency: "USD" },
          { billingInterval: "yearly", unitAmount: 9000, currency: "USD" }] });
    throw new Error("Unexpected account fixture read: " + endpoint);
  };
  await page.route(pattern, handler);
  // This test-only boot hook simulates a device-local read failure without
  // modifying production preferences, scripts or storage. Other URLs are unaffected.
  await page.addInitScript(() => {
    if (!new URL(location.href).searchParams.has("ciPerformanceFailure")) return;
    window.__ciPerformanceUnavailable = true;
    Object.defineProperty(window, "FullWorthPerformance", {
      configurable: true,
      set(api) {
        const original = api.getPreference;
        api.getPreference = function () {
          if (window.__ciPerformanceUnavailable) throw new Error("Synthetic device read unavailable");
          return original.call(api);
        };
        Object.defineProperty(window, "FullWorthPerformance", { configurable: true, writable: true, value: api });
      }
    });
  });
  const button = name => page.getByRole("button", { name, exact: true });
  const goto = async route => { await page.goto(route, { waitUntil: "domcontentloaded" }); await page.locator(".app-shell").waitFor(); };
  const photo = async name => {
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
    await page.screenshot({ path: path.join(outputDir, "account-recovery-" + name + ".png"), fullPage: true });
  };
  const checkNoCanary = async () => assert.ok(!(await page.locator("main").textContent()).includes("PRIVATE-"));
  const profilePosts = () => writes.filter(w => w.endpoint === "/bff/account/security/profile").length;
  const redemptions = () => writes.filter(w => w.endpoint.endsWith("/access-keys/redeem")).length;
  const waitHeld = async read => {
    await page.waitForFunction(() => true);
    for (let attempt = 0; attempt < 100 && !read(); attempt++) await page.waitForTimeout(25);
    assert.ok(read(), "Synthetic request must reach the hold boundary.");
  };
  try {
    await page.setViewportSize({ width: 390, height: 1000 });
    await page.context().addCookies([{ name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=en-US"),
      url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);

    // One failed security read must not erase successful preference reads/drafts.
    failures.add("/bff/account/security");
    await goto("/app/account/settings");
    await button("Retry security settings").waitFor();
    await page.locator("#timestamp-display-mode").waitFor();
    await page.locator("#experience-performance-profile").waitFor();
    await page.locator("#timestamp-display-mode").selectOption("Local12Hour");
    await page.locator("#experience-performance-profile").selectOption("efficiency");
    assert.equal(await button("Change password").count(), 0);
    await photo("settings-security-failed-390");
    const securityReads = count("/bff/account/security");
    failures.clear();
    await button("Retry security settings").click();
    await button("Change password").waitFor();
    assert.equal(count("/bff/account/security"), securityReads + 1);
    assert.equal(await page.locator("#timestamp-display-mode").inputValue(), "Local12Hour");
    assert.equal(await page.locator("#experience-performance-profile").inputValue(), "efficiency");
    assert.equal(writes.length, 0, "Read recovery must never save a default preference.");

    failures.add("/bff/account/preferences");
    await goto("/app/account/settings");
    await button("Retry timestamp preference").waitFor();
    await button("Change password").waitFor();
    assert.ok((await page.locator(".security-status-pills").textContent()).includes("2FA enabled"));
    assert.equal(await page.locator("#timestamp-display-mode").count(), 0);
    assert.equal(await button("Save preference").count(), 0);
    await photo("settings-timestamp-failed-390");
    const stableSecurityReads = count("/bff/account/security");
    failures.clear();
    await button("Retry timestamp preference").click();
    await page.locator("#timestamp-display-mode").waitFor();
    assert.equal(await page.locator("#timestamp-display-mode").inputValue(), "Utc");
    assert.equal(count("/bff/account/security"), stableSecurityReads);

    await goto("/app/account/settings?ciPerformanceFailure=1");
    await button("Retry performance preference").waitFor();
    await button("Change password").waitFor();
    await page.locator("#timestamp-display-mode").waitFor();
    assert.equal(await page.locator("#experience-performance-profile").count(), 0);
    assert.equal(await button("Apply to this device").count(), 0);
    await page.evaluate(() => { window.__ciPerformanceUnavailable = false; });
    await button("Retry performance preference").click();
    await page.locator("#experience-performance-profile").waitFor();

    // Malformed success bodies are unknown, not disabled security/default preferences.
    malformed.add("/bff/account/security"); malformed.add("/bff/account/preferences");
    await goto("/app/account/settings");
    await button("Retry security settings").waitFor();
    await button("Retry timestamp preference").waitFor();
    assert.equal(await button("Change password").count(), 0);
    assert.equal(await page.locator("#timestamp-display-mode").count(), 0);
    malformed.clear();
    await button("Retry security settings").click(); await button("Change password").waitFor();
    await button("Retry timestamp preference").click(); await page.locator("#timestamp-display-mode").waitFor();

    // Profile: initial failure must terminate loading and offer a real retry.
    failures.add("/bff/account/security");
    await goto("/app/profile");
    await button("Retry profile").waitFor();
    assert.equal(await page.locator(".fw-profile-loading").count(), 0);
    assert.equal(await button("Save profile").count(), 0);
    await photo("profile-load-failed-390");
    failures.clear();
    await button("Retry profile").click();
    await page.getByLabel("Display name", { exact: true }).waitFor();
    const name = page.getByLabel("Display name", { exact: true });
    await name.fill("Synthetic saved despite lost response"); await name.blur();
    profileMode = "lost";
    await name.press("Enter"); await button("Check saved profile").waitFor();
    assert.equal(await name.inputValue(), "Synthetic saved despite lost response");
    assert.equal(await button("Save profile").isEnabled(), false);
    assert.equal(profilePosts(), 1);
    await page.locator(".fw-profile-card form").evaluate(form => form.requestSubmit());
    assert.equal(profilePosts(), 1, "Form submission must not replay an uncertain profile save.");
    await checkNoCanary(); await photo("profile-uncertain-save-390");
    failures.add("/bff/account/security");
    await button("Check saved profile").click();
    await page.getByText("FullWorth could not load your profile.", { exact: true }).waitFor();
    assert.equal(await name.inputValue(), "Synthetic saved despite lost response");
    assert.equal(await button("Save profile").isEnabled(), false);
    assert.equal(profilePosts(), 1);
    failures.clear();
    await button("Check saved profile").click();
    await page.locator(".settings-success-banner").waitFor();
    assert.ok((await page.locator(".settings-success-banner").textContent()).includes("Your saved profile has been confirmed."));
    assert.equal(profilePosts(), 1, "Profile recovery must be a GET, not a repeated save.");
    assert.equal(await button("Save profile").isEnabled(), true);

    profileMode = "reject";
    await name.fill("Synthetic unsaved draft"); await name.blur();
    await button("Save profile").click(); await button("Check saved profile").waitFor();
    await button("Check saved profile").click();
    await page.getByText("The saved profile was checked. Your draft is kept; review it before saving again.", { exact: true }).waitFor();
    assert.equal(await name.inputValue(), "Synthetic unsaved draft");
    assert.equal(profilePosts(), 2);
    assert.ok((await page.locator("#profile-saved-name").textContent()).includes("Synthetic saved despite lost response"));
    assert.equal(await button("Save profile").isEnabled(), true);
    profileMode = "malformed";
    await button("Save profile").click(); await button("Check saved profile").waitFor();
    await button("Check saved profile").click();
    await page.locator(".settings-success-banner").waitFor();
    assert.ok((await page.locator(".settings-success-banner").textContent()).includes("Your saved profile has been confirmed."));
    assert.equal(profilePosts(), 3);
    profileMode = "ok";
    await name.fill(""); await name.press("Enter");
    await page.locator(".settings-success-banner").filter({ hasText: "Display name cleared." }).waitFor();
    assert.ok((await page.locator(".settings-success-banner").textContent()).includes("Display name cleared."));
    assert.equal(await name.inputValue(), "");
    assert.ok((await page.locator("#profile-saved-name").textContent()).includes("Not set"));
    assert.equal(profilePosts(), 4);

    malformed.add("/bff/account/security");
    await goto("/app/profile"); await button("Retry profile").waitFor(); malformed.clear();
    await button("Retry profile").click(); await name.waitFor();
    profileMode = "hold"; releaseProfile = null;
    await name.fill("Synthetic in-flight draft"); await name.blur();
    await button("Save profile").click(); await waitHeld(() => releaseProfile);
    assert.equal(await name.isEnabled(), false);
    await button("Save profile").evaluate(element => element.click());
    assert.equal(profilePosts(), 5, "Busy profile form must not dispatch a second write.");
    await page.getByRole("link", { name: "Account", exact: true }).first().click();
    await page.waitForURL(url => url.pathname === "/app/account");
    releaseProfile(); releaseProfile = null;
    await page.waitForTimeout(100);
    assert.equal(await page.locator(".fw-profile-page").count(), 0);
    profileMode = "ok";
    displayName = "W".repeat(80);
    await page.setViewportSize({ width: 320, height: 1000 });
    await goto("/app/profile"); await button("Save profile").waitFor();
    assert.equal(await name.inputValue(), displayName);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
      "A maximum-length saved name must wrap rather than widen the page.");
    const nameLayout = await page.evaluate(() => {
      const saved = document.querySelector("#profile-saved-name");
      const fields = document.querySelector(".fw-profile-grid").getBoundingClientRect();
      const actions = document.querySelector(".fw-profile-actions").getBoundingClientRect();
      return { fontSize: parseFloat(getComputedStyle(saved).fontSize), actionGap: actions.top - fields.bottom };
    });
    assert.ok(nameLayout.fontSize >= 14 && nameLayout.actionGap >= 12,
      "Saved-name recovery must stay readable and separated from submit actions.");
    await photo("profile-long-saved-name-320");
    await page.setViewportSize({ width: 390, height: 1000 });

    // Access failure never means unconfigured billing or an inactive entitlement.
    statusMode = "failed";
    await goto("/app/subscription"); await button("Retry access status").waitFor();
    assert.equal(await page.getByText("Paid plans not configured", { exact: true }).count(), 0);
    assert.equal(await button("Redeem key").count(), 0);
    assert.equal(await page.getByRole("link", { name: "Data controls", exact: true }).count(), 1);
    await checkNoCanary(); await photo("subscription-status-failed-390");
    statusMode = "ok"; active = true; paid = true;
    const plansBeforeActive = count("/bff/subscription/plans");
    await button("Retry access status").click(); await button("Manage billing").waitFor();
    assert.equal(count("/bff/subscription/plans"), plansBeforeActive, "Paid access must not depend on a plans read.");
    await button("Manage billing").click();
    await page.getByText("FullWorth could not open subscription management.", { exact: true }).waitFor();
    assert.equal(await button("Manage billing").isEnabled(), true); await checkNoCanary();

    statusMode = "malformed";
    await goto("/app/subscription"); await button("Retry access status").waitFor();
    assert.equal(await button("Manage billing").count(), 0);
    statusMode = "ok"; active = false; paid = false; plansMode = "failed";
    await button("Retry access status").click(); await button("Retry paid plans").waitFor();
    await button("Redeem key").waitFor();
    const statusReadsBeforePlansRetry = count("/bff/subscription");
    plansMode = "ok"; await button("Retry paid plans").click(); await button("Choose monthly").waitFor();
    assert.equal(count("/bff/subscription"), statusReadsBeforePlansRetry);

    plansMode = "empty"; await goto("/app/subscription"); await button("Retry paid plans").waitFor();
    await page.getByText("No paid plans are available right now.", { exact: true }).waitFor();
    assert.equal(await button("Choose monthly").count(), 0);
    plansMode = "ok"; await button("Retry paid plans").click(); await button("Choose monthly").waitFor();
    holdCheckout = true; releaseCheckout = null;
    await button("Choose monthly").click(); await waitHeld(() => releaseCheckout);
    assert.equal(await page.locator(".subscription-key-panel button[type=submit]").isEnabled(), false);
    assert.equal((await page.locator(".subscription-key-panel button[type=submit]").textContent()).trim(), "Redeem key",
      "Checkout must not claim that an access key is being activated.");
    assert.equal(await page.locator("#access-key").isEnabled(), false);
    assert.equal(writes.filter(w => w.endpoint.endsWith("/checkout")).length, 1);
    assert.equal(writes.find(w => w.endpoint.endsWith("/checkout")).body.billingInterval, "monthly");
    releaseCheckout(); releaseCheckout = null; holdCheckout = false;
    await page.getByText("FullWorth could not start secure checkout.", { exact: true }).waitFor();
    await checkNoCanary();

    redeemMode = "lost"; await page.locator("#access-key").fill("SYNTHETIC-ONE-USE");
    await page.locator("#access-key").press("Enter");
    await button("Check current access").waitFor();
    assert.equal(redemptions(), 1);
    assert.equal(await page.locator("#access-key").inputValue(), "");
    assert.equal(await button("Redeem key").isEnabled(), false);
    assert.equal(await page.locator("#access-key").isEnabled(), false);
    assert.equal(await page.locator(".subscription-toast.is-success").count(), 0);
    await checkNoCanary(); await photo("subscription-uncertain-redemption-390");
    await button("Check current access").click();
    await page.getByRole("heading", { name: "FullWorth access is active", exact: true }).waitFor();
    assert.equal(redemptions(), 1);

    active = false; redeemMode = "inactive";
    await goto("/app/subscription"); await button("Redeem key").waitFor();
    await page.locator("#access-key").fill("SYNTHETIC-NO-ACTIVE-STATUS"); await page.locator("#access-key").blur();
    await button("Redeem key").click(); await button("Check current access").waitFor();
    assert.equal(await page.locator(".subscription-toast.is-success").count(), 0, "POST success alone is not active-access evidence.");
    assert.equal(redemptions(), 2);
    statusMode = "failed"; await button("Check current access").click();
    await button("Retry access status").waitFor();
    assert.equal(redemptions(), 2);
    statusMode = "ok"; await button("Retry access status").click(); await button("Redeem key").waitFor();
    await page.getByText("Current access was checked. Review it before redeeming a key again.", { exact: true }).waitFor();
    assert.equal(redemptions(), 2);

    const syncsBefore = writes.filter(w => w.endpoint.endsWith("/sync")).length;
    await goto("/app/subscription?note=checkout=success"); await button("Redeem key").waitFor();
    await goto("/app/subscription?checkout=success&checkout=cancelled"); await button("Redeem key").waitFor();
    assert.equal(writes.filter(w => w.endpoint.endsWith("/sync")).length, syncsBefore);
    await goto("/app/subscription?checkout=success");
    await page.getByText("Checkout returned, but paid access is not confirmed yet. Refresh this page shortly.", { exact: true }).waitFor();
    assert.equal(writes.filter(w => w.endpoint.endsWith("/sync")).length, syncsBefore + 1);
    assert.equal(await page.locator(".subscription-toast.is-success").count(), 0);

    // Reflow/translation captures of the actual failed-read surface.
    statusMode = "failed";
    for (const [width, language] of [[320, "en-US"], [1440, "en-US"], [390, "es"]]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.context().addCookies([{ name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=" + language),
        url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);
      await goto("/app/subscription");
      await button(language === "es" ? "Reintentar estado de acceso" : "Retry access status").waitFor();
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
      await photo("subscription-status-failed-" + width + "-" + language);
    }
    assert.equal(writes.filter(w => w.endpoint === "/bff/account/preferences").length, 0);
    console.log("Account page recovery passed: independent settings reads/retries, malformed responses, preserved drafts, uncertain profile/redemption recovery without replay, paid status/plans isolation, busy guards, callback parsing, disposal, 320px reflow and Spanish.");
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "account-recovery-failure.png"), fullPage: true });
    throw error;
  } finally {
    releaseProfile?.(); releaseCheckout?.(); releaseRead?.();
    await page.unroute(pattern, handler);
    await page.context().addCookies([{ name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=en-US"),
      url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);
    await page.setViewportSize({ width: 390, height: 844 });
    await goto("/app");
  }
}
