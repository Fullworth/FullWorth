import assert from "node:assert/strict";
import path from "node:path";

// Runs only against the disposable CI stack. No credentials or real financial
// records are created/changed by these navigation probes.
export async function verifyNavigationRecovery(page, outputDir) {
  assert.ok(new URL(page.url()).hostname.endsWith(".localhost"), "Navigation fixture requires isolated CI.");
  for (const width of [1440, 320]) {
    await page.setViewportSize({ width, height: 1000 });
    const response = await page.goto("/missing-ui-fixture", { waitUntil: "domcontentloaded" });
    assert.equal(response.status(), 404, "Unknown document must retain HTTP 404.");
    assert.equal(new URL(page.url()).pathname, "/missing-ui-fixture", "Recovery must preserve the requested URL.");
    await page.locator("#not-found-title").waitFor({ state: "visible" });
    assert.ok(response.headers()["cache-control"].includes("no-store"));
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
    await page.screenshot({ path: path.join(outputDir, "not-found-" + width + ".png"), fullPage: true });
    await page.getByRole("link", { name: "Back to home", exact: true }).click();
    await page.waitForURL(url => url.pathname === "/");
    await page.locator(".landing-page").waitFor({ state: "visible" });
  }

  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await page.locator(".app-shell").waitFor({ state: "visible" });
  await page.setViewportSize({ width: 390, height: 1000 });
  await page.waitForFunction(() => !!document.querySelector(".app-shell")?.style.getPropertyValue("--fw-navigation-inset"));
  const trigger = page.locator(".mobile-menu-trigger");
  await trigger.click();
  const sheet = page.locator("#billwatch-mobile-menu");
  await sheet.waitFor({ state: "visible" });
  await sheet.getByRole("link", { name: "Planning", exact: true }).click();
  await page.waitForURL(url => url.pathname === "/app/planning");
  await page.locator(".planning-workspace").waitFor({ state: "visible" });
  assert.equal(await trigger.getAttribute("aria-current"), "page", "Planning belongs to More navigation.");
  await trigger.click();
  await sheet.waitFor({ state: "visible" });
  await sheet.getByRole("link", { name: "Planning", exact: true }).press("Escape");
  await sheet.waitFor({ state: "hidden" });
  await page.waitForFunction(() => document.activeElement?.matches(".mobile-menu-trigger"));
  assert.equal(await trigger.getAttribute("aria-expanded"), "false");
  await page.screenshot({ path: path.join(outputDir, "planning-menu-keyboard-recovery.png"), fullPage: false });

  // A hidden sheet must not strand keyboard focus after an Escape from a link.
  await trigger.press("Enter");
  await sheet.waitFor({ state: "visible" });
  await sheet.getByRole("link", { name: "Subscription", exact: true }).press("Escape");
  await sheet.waitFor({ state: "hidden" });
  await page.waitForFunction(() => document.activeElement?.matches(".mobile-menu-trigger"));
  // Wait for actual interactive menu events above before probing Router recovery.
  // Keep the synthetic link outside the component render tree so hydration or
  // data refresh cannot detach it before navigation.
  const missingResponse = page.waitForResponse(response =>
    new URL(response.url()).pathname === "/app/missing-ui-fixture" && response.request().isNavigationRequest());
  await page.evaluate(() => {
    const link = document.createElement("a");
    link.href = "/app/missing-ui-fixture";
    link.dataset.ciMissingPage = "true";
    link.textContent = "CI missing page";
    document.body.append(link);
    link.click();
    link.remove();
  });
  await page.waitForURL(url => url.pathname === "/app/missing-ui-fixture");
  await page.locator("#not-found-title").waitFor({ state: "visible" });
  // Blazor deliberately falls back to server navigation for an unmatched link,
  // since it could target non-component content. The server must recover too.
  assert.equal((await missingResponse).status(), 404);
  await page.getByRole("link", { name: "Go to FullWorth", exact: true }).click();
  await page.waitForURL(url => url.pathname === "/app");
  await page.locator(".app-shell").waitFor({ state: "visible" });
  await page.goto("/app", { waitUntil: "domcontentloaded" });
  console.log("Navigation recovery passed: HTTP/intercepted-link 404, recovery links, 320px reflow, Planning current state and Escape focus.");
}
