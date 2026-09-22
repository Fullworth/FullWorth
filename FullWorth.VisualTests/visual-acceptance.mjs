import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";

const baseUrl = process.env.FULLWORTH_VISUAL_BASE_URL ?? "https://web.localhost";
const runId = process.env.GITHUB_RUN_ID ?? String(Date.now());
const outputDir = path.resolve("FullWorth.VisualTests", "artifacts");
const email = `ci-visual-${runId}@fullworth.local`;
const password = "FullWorth!Visual123";

await mkdir(outputDir, { recursive: true });

const browser = await chromium.launch({
  headless: true,
  args: ["--ignore-certificate-errors"]
});

try {
  const context = await browser.newContext({
    baseURL: baseUrl,
    ignoreHTTPSErrors: true,
    serviceWorkers: "allow",
    viewport: { width: 1440, height: 1100 }
  });

  const page = await context.newPage();

  async function settle(selector) {
    await page.waitForLoadState("domcontentloaded");
    if (selector) {
      await page.locator(selector).first().waitFor({ state: "visible", timeout: 15000 });
    }
    await page.waitForTimeout(900);
  }

  async function capture(name, route, selector) {
    await page.goto(route, { waitUntil: "domcontentloaded" });
    await settle(selector);
    await page.screenshot({
      path: path.join(outputDir, `${name}.png`),
      fullPage: true
    });
  }

  await capture("public-home-desktop", "/", ".landing-page");

  const missingPublicAnchors = await page.evaluate(() => {
    return Array.from(document.querySelectorAll('.desktop-nav a[href*="#"]'))
      .map(link => link.getAttribute("href"))
      .filter(Boolean)
      .map(href => new URL(href, window.location.origin).hash)
      .filter(hash => hash && !document.querySelector(hash));
  });

  if (missingPublicAnchors.length > 0) {
    throw new Error(
      `Public navigation targets are missing: ${missingPublicAnchors.join(", ")}`
    );
  }

  await capture("login-desktop", "/login", ".auth-card");
  await capture("register-desktop", "/register", ".auth-card");

  await page.goto("/register", { waitUntil: "domcontentloaded" });
  await settle(".auth-form");
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(password);
  await page.locator('input[name="confirmPassword"]').fill(password);
  await page.locator('input[name="acceptedTermsAndPrivacy"]').check();

  await Promise.all([
    page.waitForURL(url => url.pathname === "/app", { timeout: 20000 }),
    page.locator('button[type="submit"]').click()
  ]);

  await settle(".app-shell");

  await page.evaluate(() => {
    localStorage.setItem("billwatch-theme", "dark");
  });
  await page.reload({ waitUntil: "domcontentloaded" });
  await settle(".app-shell");

  const desktopRoutes = [
    ["overview-desktop-dark", "/app"],
    ["bills-desktop-dark", "/app/bills"],
    ["activity-desktop-dark", "/app/activity"],
    ["account-desktop-dark", "/app/account"],
    ["transactions-desktop-dark", "/app/account/transactions"],
    ["settings-desktop-dark", "/app/account/settings"],
    ["privacy-desktop-dark", "/app/account/privacy"],
    ["subscription-desktop-dark", "/app/subscription"],
    ["profile-desktop-dark", "/app/profile"]
  ];

  for (const [name, route] of desktopRoutes) {
    await capture(name, route, ".app-shell");
  }

  await page.evaluate(() => {
    localStorage.setItem("billwatch-theme", "light");
  });
  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");
  await page.screenshot({
    path: path.join(outputDir, "overview-desktop-light.png"),
    fullPage: true
  });

  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => {
    localStorage.setItem("billwatch-theme", "dark");
  });

  const mobileRoutes = [
    ["public-home-mobile", "/"],
    ["overview-mobile-dark", "/app"],
    ["bills-mobile-dark", "/app/bills"],
    ["activity-mobile-dark", "/app/activity"],
    ["account-mobile-dark", "/app/account"],
    ["transactions-mobile-dark", "/app/account/transactions"],
    ["settings-mobile-dark", "/app/account/settings"],
    ["privacy-mobile-dark", "/app/account/privacy"],
    ["subscription-mobile-dark", "/app/subscription"],
    ["profile-mobile-dark", "/app/profile"]
  ];

  for (const [name, route] of mobileRoutes) {
    const selector = route === "/" ? ".landing-page" : ".app-shell";
    await capture(name, route, selector);
  }

  for (const route of ["/app/subscription", "/app/profile"]) {
    await page.goto(route, { waitUntil: "domcontentloaded" });
    await settle(".mobile-bottom-nav");

    const menuState = page.locator(".mobile-more-menu");
    const primaryActiveCount = await page.locator(
      ".mobile-bottom-nav > .mobile-bottom-link.active"
    ).count();

    if (!(await menuState.evaluate(element => element.classList.contains("is-current")))) {
      throw new Error(`Expected Menu to represent secondary route ${route}.`);
    }

    if (primaryActiveCount !== 0) {
      throw new Error(
        `Expected no primary mobile route to be active on ${route}; found ${primaryActiveCount}.`
      );
    }

    if ((await page.locator(".mobile-menu-trigger").getAttribute("aria-current")) !== "page") {
      throw new Error(`Expected Menu to expose aria-current on ${route}.`);
    }
  }

  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".mobile-bottom-nav");

  const activeRouteBeforeMenu = await page.locator(
    ".mobile-bottom-nav > .mobile-bottom-link.active"
  ).count();

  if (activeRouteBeforeMenu !== 1) {
    throw new Error(
      `Expected exactly one active mobile route before opening Menu; found ${activeRouteBeforeMenu}.`
    );
  }

  await page.locator(".mobile-menu-trigger").click();
  await page.locator("#billwatch-mobile-menu").waitFor({ state: "visible", timeout: 10000 });

  const activeRouteWhileMenuOpen = await page.locator(
    ".mobile-bottom-nav > .mobile-bottom-link.active"
  ).count();

  if (activeRouteWhileMenuOpen !== 0) {
    throw new Error(
      `Expected route highlight to be suppressed while Menu is open; found ${activeRouteWhileMenuOpen} active route item(s).`
    );
  }

  await page.waitForTimeout(250);
  await page.screenshot({
    path: path.join(outputDir, "mobile-menu-dark.png"),
    fullPage: true
  });

  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".mobile-bottom-nav");

  await Promise.all([
    page.waitForURL(url => url.pathname === "/app/bills", { timeout: 10000 }),
    page.locator('.mobile-bottom-nav > a[href="/app/bills"]').click()
  ]);
  await settle(".mobile-bottom-nav");

  await Promise.all([
    page.waitForURL(url => url.pathname === "/app/activity", { timeout: 10000 }),
    page.locator('.mobile-bottom-nav > a[href="/app/activity"]').click()
  ]);
  await settle(".mobile-bottom-nav");

  await Promise.all([
    page.waitForURL(url => url.pathname === "/app/bills", { timeout: 10000 }),
    page.goBack()
  ]);
  await settle(".mobile-bottom-nav");

  const billsBackActiveHref =
    await page.locator(".mobile-bottom-nav > .mobile-bottom-link.active").getAttribute("href");

  if (billsBackActiveHref !== "/app/bills") {
    throw new Error(
      `Expected browser Back to restore Bills as the active mobile route; got ${billsBackActiveHref ?? "none"}.`
    );
  }

  await Promise.all([
    page.waitForURL(url => url.pathname === "/app", { timeout: 10000 }),
    page.goBack()
  ]);
  await settle(".mobile-bottom-nav");

  const overviewBackActiveHref =
    await page.locator(".mobile-bottom-nav > .mobile-bottom-link.active").getAttribute("href");

  if (overviewBackActiveHref !== "/app") {
    throw new Error(
      `Expected browser Back to restore Overview as the active mobile route; got ${overviewBackActiveHref ?? "none"}.`
    );
  }

  await page.goto("/app/account/settings", { waitUntil: "domcontentloaded" });
  await settle(".settings-page");

  await page.getByRole("button", { name: "Change password", exact: true }).click();

  const passwordDialog = page.locator("dialog.settings-dialog");
  await passwordDialog.waitFor({ state: "visible", timeout: 10000 });

  if ((await passwordDialog.getAttribute("open")) === null) {
    throw new Error("Expected Change password security dialog to be open.");
  }

  if ((await passwordDialog.locator("#settings-dialog-title").textContent())?.trim() !== "Change password") {
    throw new Error("Expected Change password dialog title.");
  }

  const labelledBy = await passwordDialog.getAttribute("aria-labelledby");
  const describedBy = await passwordDialog.getAttribute("aria-describedby");

  if (
    labelledBy !== "settings-dialog-title" ||
    describedBy !== "settings-dialog-description" ||
    await passwordDialog.locator("#settings-dialog-title").count() !== 1 ||
    await passwordDialog.locator("#settings-dialog-description").count() !== 1
  ) {
    throw new Error("Security dialog accessibility references are incomplete.");
  }

  await passwordDialog.getByRole("button", { name: "Close", exact: true }).click();
  await passwordDialog.waitFor({ state: "detached", timeout: 10000 });

  await page.getByRole("button", { name: "Change email", exact: true }).click();

  const emailDialog = page.locator("dialog.settings-dialog");
  await emailDialog.waitFor({ state: "visible", timeout: 10000 });

  if ((await emailDialog.locator("#settings-dialog-title").textContent())?.trim() !== "Change email address") {
    throw new Error("Expected Change email dialog title.");
  }

  await page.keyboard.press("Escape");
  await emailDialog.waitFor({ state: "detached", timeout: 10000 });

  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");

  const serviceWorkerState = await page.evaluate(async () => {
    if (!("serviceWorker" in navigator)) {
      return {
        supported: false,
        controlled: false,
        scriptUrl: null,
        updateViaCache: null
      };
    }

    let registration =
      await navigator.serviceWorker.getRegistration("/");

    if (!registration) {
      registration =
        await navigator.serviceWorker.register(
          "/service-worker.js",
          {
            scope: "/",
            updateViaCache: "none"
          }
        );
    }

    await Promise.race([
      navigator.serviceWorker.ready,
      new Promise((_, reject) =>
        setTimeout(
          () => reject(new Error("Service worker did not become ready after registration.")),
          10000
        )
      )
    ]);

    return {
      supported: true,
      controlled: navigator.serviceWorker.controller !== null,
      scriptUrl: registration.active?.scriptURL ?? null,
      updateViaCache: registration.updateViaCache ?? null
    };
  });

  if (!serviceWorkerState.supported) {
    throw new Error("Expected Chromium to support service workers.");
  }

  if (!serviceWorkerState.controlled) {
    await page.waitForFunction(
      () => navigator.serviceWorker?.controller !== null,
      null,
      { timeout: 10000 }
    );
  }

  if (!serviceWorkerState.scriptUrl?.endsWith("/service-worker.js")) {
    throw new Error(
      `Expected FullWorth service worker to be active; got ${serviceWorkerState.scriptUrl ?? "none"}.`
    );
  }

  if (serviceWorkerState.updateViaCache !== "none") {
    throw new Error(
      `Expected service worker updates to bypass the HTTP cache; got ${serviceWorkerState.updateViaCache ?? "unknown"}.`
    );
  }

  await context.setOffline(true);

  try {
    await page.reload({ waitUntil: "domcontentloaded" });

    const offlineHeading = await page.locator("h1").textContent();

    if (offlineHeading?.trim() !== "You're offline") {
      throw new Error(
        `Expected generic offline fallback while disconnected; got heading ${JSON.stringify(offlineHeading)}.`
      );
    }

    if (await page.locator(".app-shell").count() !== 0) {
      throw new Error(
        "Authenticated app shell rendered while offline; financial pages must remain network-first."
      );
    }
  } finally {
    await context.setOffline(false);
  }

  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");

  await context.clearCookies();

  await page.evaluate(() => {
    void import("/js/bff.js")
      .then(module => module.getBillStreams())
      .catch(() => {
        // A 401 is expected after the authenticated BFF cookie is removed.
        // The module must redirect the browser to /login before rejecting.
      });
  });

  await page.waitForURL(
    url => url.pathname === "/login",
    { timeout: 10000 }
  );
  await settle(".auth-card");

  if (await page.locator(".app-shell").count() !== 0) {
    throw new Error(
      "Authenticated app shell remained visible after the BFF session was invalidated."
    );
  }

  await context.close();
} finally {
  await browser.close();
}
