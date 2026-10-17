import { chromium } from "playwright";
import { createHmac } from "node:crypto";
import assert from "node:assert/strict";
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
    page.waitForURL(url =>
      url.pathname === "/login" &&
      url.searchParams.get("message") ===
        "Registration request received. Sign in to continue.",
      { timeout: 20000 }),
    page.locator('button[type="submit"]').click()
  ]);

  await settle(".auth-form");
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(password);

  await Promise.all([
    page.waitForURL(url => url.pathname.startsWith("/app"), { timeout: 20000 }),
    page.locator('button[type="submit"]').click()
  ]);

  await page.goto("/app/setup", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");

  const whatsNewDialog = page.locator(".whats-new-dialog");

  await whatsNewDialog.waitFor({
    state: "visible",
    timeout: 10000
  });

  await page.screenshot({
    path: path.join(outputDir, "whats-new-desktop.png"),
    fullPage: true
  });

  await page.locator(".whats-new-primary").click();

  await whatsNewDialog.waitFor({
    state: "hidden",
    timeout: 10000
  });

  await page.reload({ waitUntil: "domcontentloaded" });
  await settle(".app-shell");

  if (await whatsNewDialog.isVisible()) {
    throw new Error(
      "Expected What's new to remain dismissed for the current release."
    );
  }

  await page.screenshot({
    path: path.join(outputDir, "setup-desktop-dark.png"),
    fullPage: true
  });

  await page.goto("/app", { waitUntil: "domcontentloaded" });
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

  // Render the real components with synthetic BFF evidence. The API contract
  // tests separately verify ownership and provider-currency projection.
  const currencyBillId = "11111111-1111-1111-1111-111111111111";
  const currencyBills = [
    { id: currencyBillId, providerName: "Synthetic Euro Utility", category: "Utility",
      isActive: true, source: "Manual", currentAmount: 20, previousAverage: 10,
      currencyCode: "EUR", hasTransactionEvidence: true, hasMonthlyCadenceEvidence: true },
    { id: "22222222-2222-2222-2222-222222222222", providerName: "Synthetic Dollar Service",
      category: "Other", isActive: true, source: "Manual", currentAmount: 10, previousAverage: 8,
      currencyCode: "USD", hasTransactionEvidence: true, hasMonthlyCadenceEvidence: true },
    { id: "33333333-3333-3333-3333-333333333333", providerName: "Synthetic Unknown Currency",
      category: "Other", isActive: true, source: "Manual", currentAmount: 999, previousAverage: 0,
      currencyCode: null, hasTransactionEvidence: true, hasMonthlyCadenceEvidence: false },
    { id: "44444444-4444-4444-4444-444444444444", providerName: "Synthetic Unavailable Amount",
      category: "Other", isActive: true, source: "Manual", currentAmount: 0, previousAverage: 0,
      currencyCode: null, hasTransactionEvidence: false, hasMonthlyCadenceEvidence: false }
  ];
  const currencyDetail = { ...currencyBills[0],
    statements: [{ id: "55555555-5555-5555-5555-555555555555",
      periodStart: "2026-01-01", periodEnd: "2026-01-31", totalAmount: 25, currencyCode: "EUR" }],
    changes: [{ id: "66666666-6666-6666-6666-666666666666",
      currentStatementId: "55555555-5555-5555-5555-555555555555",
      previousAmount: 20, currentAmount: 25, amountDifference: 5, annualizedImpact: 60,
      currencyCode: "EUR", changeType: "TotalIncrease", confidence: "Confirmed",
      description: "Synthetic statement evidence", isAcknowledged: false,
      detectedAtUtc: "2026-02-01T00:00:00Z" }]
  };
  const currencyRoute = async route => {
    const pathname = new URL(route.request().url()).pathname;
    if (route.request().method() !== "GET") return route.continue();
    await route.fulfill({ status: 200, contentType: "application/json",
      body: JSON.stringify(pathname === "/bff/bill-streams" ? currencyBills : currencyDetail) });
  };
  const currencyConnectionRoute = async route => {
    if (route.request().method() !== "GET") return route.continue();
    await route.fulfill({ status: 200, contentType: "application/json",
      body: JSON.stringify([{ id: "77777777-7777-7777-7777-777777777777",
        institutionName: "Synthetic bank", status: 0,
        lastSuccessfulSyncAtUtc: "2026-02-01T00:00:00Z", createdAtUtc: "2026-01-01T00:00:00Z" }]) });
  };
  const currencyBillRoutePattern = /\/bff\/bill-streams(?:\/[^?]*)?(?:\?.*)?$/;
  await page.route(currencyBillRoutePattern, currencyRoute);
  await page.route("**/bff/bank-connections", currencyConnectionRoute);
  try {
    for (const width of [1440, 320]) {
      await page.setViewportSize({ width, height: 1100 });
      await page.goto("/app/bills", { waitUntil: "domcontentloaded" });
      await settle(".fw-bills-row");
      assert.equal((await page.locator(".fw-bills-total > strong").innerText()).trim(), "EUR 20.00 · $10.00");
      const euro = page.locator(".fw-bills-row").filter({ hasText: "Synthetic Euro Utility" });
      assert.ok((await euro.innerText()).includes("EUR 20.00"));
      assert.ok(!(await euro.innerText()).includes("$20.00"));
      const unknown = page.locator(".fw-bills-row").filter({ hasText: "Synthetic Unknown Currency" });
      assert.ok((await unknown.innerText()).includes("999.00 (Currency unavailable)"));
      assert.ok((await page.locator(".fw-bills-row").filter({
        hasText: "Synthetic Unavailable Amount" }).innerText()).includes("Amount unavailable"));
      const totalText = await page.locator(".fw-bills-total").innerText();
      assert.ok(!totalText.includes("999.00"));
      assert.match(totalText, /bills\s+Some bill amounts are unavailable/);
      assert.ok(!(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)),
        "Currency totals must fit the viewport");
      await page.screenshot({ path: path.join(outputDir, "bills-currencies-" + width + ".png"), fullPage: true });

      await page.goto("/app", { waitUntil: "domcontentloaded" });
      await settle(".fw-pulse-amount");
      assert.equal((await page.locator(".fw-pulse-amount").innerText()).trim(), "EUR 20.00 · $10.00");
      assert.ok((await page.locator(".fw-signal-card").innerText()).includes("Bills need review"));
      assert.ok(!(await page.locator(".fw-signal-card").innerText()).includes("Everything looks steady"));

      await page.goto("/app/bills/" + currencyBillId, { waitUntil: "domcontentloaded" });
      await settle(".fw-detail-current");
      const detailText = await page.locator(".app-page").innerText();
      assert.ok(detailText.includes("EUR 20.00"));
      assert.ok(detailText.includes("EUR 25.00"));
      assert.ok(detailText.includes("+EUR 60.00"));
      assert.ok(!detailText.includes("$20.00") && !detailText.includes("$25.00"));
      await page.screenshot({ path: path.join(outputDir, "bill-detail-currencies-" + width + ".png"), fullPage: true });
    }
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "currency-acceptance-failure.png"), fullPage: true });
    throw error;
  } finally {
    await page.unroute(currencyBillRoutePattern, currencyRoute);
    await page.unroute("**/bff/bank-connections", currencyConnectionRoute);
  }

  // Exercise the real authenticated Bills page, not duplicated fixture markup.
  // This account and every bank/transaction record are disposable CI data.
  const billsDestinations = [
    ["Check bank connections", "/app/account"],
    ["Review imported transactions", "/app/account/transactions"],
    ["Check monitoring progress", "/app"]
  ];
  for (const [width, theme, enlarged] of [
    [390, "dark", false],
    [390, "light", false],
    [320, "dark", false],
    [320, "light", false],
    [390, "dark", true]
  ]) {
    await page.setViewportSize({ width, height: 844 });
    await page.evaluate(value => localStorage.setItem("billwatch-theme", value), theme);
    await page.goto("/app/bills", { waitUntil: "domcontentloaded" });
    await settle(".bills-empty-state");
    if (enlarged) {
      await page.evaluate(() => document.documentElement.style.fontSize = "200%");
    }
    const empty = page.locator(".bills-empty-state");
    assert.equal(await empty.locator("h2").innerText(), "No recurring bills to show yet.");
    assert.equal(await empty.locator("a").count(), 3);
    assert.equal(await empty.locator("button, form").count(), 0);
    assert.ok((await empty.innerText()).includes(
      "An empty list does not confirm that your bank is connected or syncing."));
    assert.equal(await page.locator("html").getAttribute("data-theme"), theme);

    const unsafeRequests = [];
    const recordMutation = request => {
      const pathname = new URL(request.url()).pathname;
      if (pathname.startsWith("/bff/") && !["GET", "HEAD", "OPTIONS"].includes(request.method())) {
        unsafeRequests.push(request.method() + " " + pathname);
      }
    };
    page.on("request", recordMutation);
    try {
      for (const [label, destination] of billsDestinations) {
        const link = empty.getByRole("link", { name: label, exact: true });
        assert.equal(await link.getAttribute("href"), destination);
        // Fixed navigation overlaps part of the viewport; center the action as a
        // person scrolling to it would, then verify it is actually unobstructed.
        await link.evaluate(element => element.scrollIntoView({
          block: "center", inline: "nearest", behavior: "instant"
        }));
        await link.focus();
        const geometry = await link.evaluate(element => {
          const rect = element.getBoundingClientRect();
          const nav = document.querySelector(".mobile-bottom-nav")?.getBoundingClientRect();
          const style = getComputedStyle(element);
          const center = document.elementFromPoint(rect.x + rect.width / 2, rect.y + rect.height / 2);
          return {
            height: rect.height,
            left: rect.left,
            right: rect.right,
            bottom: rect.bottom,
            navTop: nav?.top ?? innerHeight,
            reachable: element === center || element.contains(center),
            focused: document.activeElement === element,
            outline: style.outlineStyle,
            outlineWidth: parseFloat(style.outlineWidth),
            overflow: document.documentElement.scrollWidth > innerWidth + 1,
            viewportWidth: innerWidth
          };
        });
        assert.ok(geometry.height >= 48, "Empty-state actions need 48px touch targets.");
        assert.ok(geometry.left >= 0 && geometry.right <= geometry.viewportWidth + 1);
        assert.ok(geometry.bottom <= geometry.navTop,
          `Action must clear the fixed bottom navigation (${width}px ${theme} ` +
          `largeText=${enlarged} ${label}; bottom=${geometry.bottom}, navTop=${geometry.navTop}).`);
        assert.ok(geometry.reachable && geometry.focused, "Action must be reachable and keyboard focusable.");
        // Programmatic focus followed by a keyboard event opts into :focus-visible.
        await page.keyboard.press("Tab");
        await page.keyboard.press("Shift+Tab");
        const focusOutline = await link.evaluate(element => {
          const style = getComputedStyle(element);
          return document.activeElement === element &&
            style.outlineStyle !== "none" && parseFloat(style.outlineWidth) >= 2;
        });
        assert.ok(focusOutline, "Keyboard users need a visible focus indicator.");
        assert.equal(geometry.overflow, false, "Bills empty state must not introduce horizontal scrolling.");
      }

      await page.screenshot({
        path: path.join(outputDir, "bills-empty-" + width + "-" + theme + (enlarged ? "-large-text" : "") + ".png"),
        fullPage: true
      });

      // Verify each real internal destination with the keyboard, without invoking sync.
      for (const [label, destination] of billsDestinations) {
        await page.goto("/app/bills", { waitUntil: "domcontentloaded" });
        await settle(".bills-empty-state");
        const link = page.locator(".bills-empty-state").getByRole("link", { name: label, exact: true });
        await link.focus();
        await Promise.all([
          page.waitForURL(url => url.pathname === destination, { timeout: 10000 }),
          page.keyboard.press("Enter")
        ]);
        await settle(".app-shell");
        assert.equal(await page.locator(".app-error-state").count(), 0);
      }
      assert.deepEqual(unsafeRequests, [], "Empty-state navigation must not mutate BFF resources.");
    } finally {
      page.off("request", recordMutation);
    }
  }
  await page.setViewportSize({ width: 390, height: 844 });
  await page.evaluate(() => localStorage.setItem("billwatch-theme", "dark"));

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

  // Account is created above in this disposable CI stack. Never run this
  // enrollment probe against a production host or a real user's account.
  const fixtureHost = new URL(baseUrl);
  if (!fixtureHost.hostname.endsWith(".localhost")) {
    throw new Error("MFA browser acceptance requires the isolated .localhost fixture.");
  }
  const apiOrigin = new URL(fixtureHost.origin);
  apiOrigin.hostname = "localhost";
  const apiPost = async (route, body, accessToken) => {
    const response = await context.request.post(apiOrigin.origin + route, {
      data: body,
      headers: accessToken ? { Authorization: "Bearer " + accessToken } : {}
    });
    if (!response.ok()) throw new Error("Disposable MFA fixture setup failed at " + route);
    return response;
  };
  const bootstrap = await (await apiPost("/api/auth/login", { email, password })).json();
  const setup = await (await apiPost("/api/account/security/two-factor/setup",
    { currentPassword: password }, bootstrap.accessToken)).json();
  function authenticatorCode(secret) {
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
  await apiPost("/api/account/security/two-factor/enable",
    { currentPassword: password, authenticatorCode: authenticatorCode(setup.sharedKey) },
    bootstrap.accessToken);
  await page.goto("/login", { waitUntil: "domcontentloaded" });
  await settle(".auth-form");
  await page.locator('input[name="email"]').fill(email);
  await page.locator('input[name="password"]').fill(password);
  await page.locator('input[name="rememberMe"]').check();
  await Promise.all([
    page.waitForURL(url => url.pathname === "/login" && url.searchParams.get("twoFactor") === "true"),
    page.locator('button[type="submit"]').click()
  ]);
  await settle('input[name="twoFactorCode"]');
  if (await page.locator('input[name="password"], input[name="email"]').count() !== 0)
    throw new Error("MFA continuation must not ask for the password or email again.");
  await page.screenshot({ path: path.join(outputDir, "password-once-verification-mobile.png"), fullPage: true });
  // An invalid code must preserve a bounded pending sign-in, not repeat the
  // password form. Never capture a real or fixture authenticator secret.
  await page.locator('input[name="twoFactorCode"]').fill("1234567");
  await Promise.all([
    page.waitForURL(url => url.searchParams.get("twoFactor") === "true" && url.searchParams.has("error")),
    page.locator('form[action="/auth/login"]').first().getByRole("button", { name: "Verify and sign in" }).click()
  ]);
  await settle('input[name="twoFactorCode"]');
  if (await page.locator('input[name="password"], input[name="email"]').count() !== 0)
    throw new Error("Invalid-code retry unexpectedly repeated the password step.");
  await page.locator('input[name="twoFactorCode"]').fill(authenticatorCode(setup.sharedKey));
  await Promise.all([
    page.waitForURL(url => url.pathname === "/app"),
    page.locator('form[action="/auth/login"]').first().getByRole("button", { name: "Verify and sign in" }).click()
  ]);
  await settle(".app-shell");

  await context.close();
} finally {
  await browser.close();
}
