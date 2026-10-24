import { verifyPlanningWorkspace } from "./planning-workspace.test.mjs";
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

await (await import("./privacy-requests.test.mjs")).verifyPrivacyRequestModules();

await (await import("./experience-preferences.test.mjs")).verifyExperiencePreferenceModule();
await (await import("./navigation-viewport.test.mjs")).verifyNavigationViewportModule();

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

    if (route.startsWith("/app") && await page.evaluate(() => window.innerWidth <= 900)) {
      const geometry = await page.evaluate(() => {
        const viewport = document.documentElement.clientWidth;
        const documentWidth = document.documentElement.scrollWidth;
        const bodyWidth = document.body.scrollWidth;

        // Report only element types/classes, never financial text or bank IDs.
        const candidates = [...document.querySelectorAll(".app-shell *")]
          .filter(element => {
            const rect = element.getBoundingClientRect();
            const styles = getComputedStyle(element);
            return styles.display !== "none" && styles.position !== "fixed" &&
              rect.width > 0 && (rect.right > viewport + 2 || rect.left < -2);
          })
          .slice(0, 8)
          .map(element => ({
            element: element.tagName.toLowerCase(),
            classes: [...element.classList].slice(0, 3)
          }));

        return { viewport, documentWidth, bodyWidth, candidates };
      });

      assert.ok(geometry.documentWidth <= geometry.viewport + 1 &&
        geometry.bodyWidth <= geometry.viewport + 1,
        "Authenticated " + route + " has unwanted sideways scrolling at " +
        geometry.viewport + "px: " + JSON.stringify(geometry));
    }

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

  // The decorative glow must not make release notes pan sideways on iPhone.
  // Keep the real dialog open while exercising narrow viewport layouts.
  for (const viewport of [
    { width: 320, height: 640 },
    { width: 390, height: 844 },
    { width: 430, height: 932 }
  ]) {
    await page.setViewportSize(viewport);
    const layout = await whatsNewDialog.evaluate(dialog => {
      const rect = dialog.getBoundingClientRect();
      return {
        left: rect.left,
        right: rect.right,
        viewportWidth: window.innerWidth,
        scrollWidth: dialog.scrollWidth,
        clientWidth: dialog.clientWidth,
        canScrollVertically: dialog.scrollHeight > dialog.clientHeight,
        overflowX: getComputedStyle(dialog).overflowX,
        overflowY: getComputedStyle(dialog).overflowY
      };
    });

    assert.ok(layout.left >= -1 && layout.right <= layout.viewportWidth + 1,
      `What's New must fit inside the ${viewport.width}px viewport: ${JSON.stringify(layout)}`);
    assert.ok(layout.scrollWidth <= layout.clientWidth + 1,
      `What's New must have no horizontal overflow at ${viewport.width}px: ${JSON.stringify(layout)}`);
    assert.equal(layout.overflowX, "hidden",
      "What's New must disable sideways scrolling.");
    assert.equal(layout.overflowY, "auto",
      "What's New must preserve vertical scrolling.");
    assert.equal(layout.canScrollVertically, true,
      `What's New must remain vertically scrollable at ${viewport.width}px.`);

    if (viewport.width === 390) {
      await page.screenshot({
        path: path.join(outputDir, "whats-new-mobile.png"),
        fullPage: true
      });
    }
  }
  await page.setViewportSize({ width: 1440, height: 1100 });

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

  await (await import("./experience-setup.test.mjs")).verifyExperienceSetup(page, outputDir);
  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");

  await (await import("./ui-polish.test.mjs")).verifyUiPolish(page, outputDir);
  await page.goto("/app", { waitUntil: "domcontentloaded" });
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
    ["planning-mobile-dark", "/app/planning"],
    ["account-mobile-dark", "/app/account"],
    ["transactions-mobile-dark", "/app/account/transactions"],
    ["settings-mobile-dark", "/app/account/settings"],
    ["privacy-mobile-dark", "/app/account/privacy"],
    ["subscription-mobile-dark", "/app/subscription"],
    ["profile-mobile-dark", "/app/profile"]
  ];

  // The 390px baseline remains unchanged; 320px and 430px guard against
  // narrow phone overflow on every authenticated route, not just Bills.
  for (const width of [390, 320, 430]) {
    await page.setViewportSize({ width, height: 844 });
    for (const [name, route] of mobileRoutes) {
      const selector = route === "/" ? ".landing-page" : ".app-shell";
      await capture(width === 390 ? name : name + "-" + width, route, selector);
    }
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
      const detailOrder = await page.evaluate(() => {
        const story = document.querySelector(".fw-detail-change-story");
        const upload = document.querySelector(".statement-upload-panel");
        const choice = document.querySelector(".bill-monitoring-choice");
        const history = document.querySelector(".fw-detail-history-grid");
        if (!story || !upload || !choice || !history) return null;
        const prior = (a, b) => Boolean(a.compareDocumentPosition(b) & Node.DOCUMENT_POSITION_FOLLOWING);
        return prior(story, choice) && prior(story, upload) &&
          prior(choice, history) && prior(upload, history);
      });
      assert.equal(detailOrder, true,
        "Bill evidence story must appear before upload and monitoring actions, then historical records.");
      const detailText = await page.locator(".app-page").innerText();
      assert.ok(detailText.includes("EUR 20.00"));
      assert.ok(detailText.includes("EUR 25.00"));
      assert.ok(detailText.includes("+EUR 60.00"));
      assert.ok(!detailText.includes("$20.00") && !detailText.includes("$25.00"));
      await page.screenshot({ path: path.join(outputDir, "bill-detail-currencies-" + width + ".png"), fullPage: true });

      if (width === 320) {
        // Reproduce the real upload-status markup and its Blazor CSS scope without
        // sending any financial document or issuing a POST.
        await page.locator(".statement-upload-panel").evaluate(panel => {
          const scope = [...panel.attributes].find(attribute => /^b-[a-z0-9]+$/.test(attribute.name))?.name;
          assertScope(scope);
          function assertScope(value) {
            if (!value) throw new Error("Statement panel CSS isolation attribute not found.");
          }
          const make = (tag, className, text) => {
            const element = document.createElement(tag);
            element.setAttribute(scope, "");
            if (className) element.className = className;
            if (text) element.textContent = text;
            return element;
          };
          const status = make("div", "statement-upload-status working");
          const dot = make("span", "statement-status-dot");
          const body = make("div");
          body.append(
            make("strong", "", "Uploading securely"),
            make("span", "", "Sending the " + "A".repeat(240) + ".pdf statement to FullWorth.")
          );
          status.append(dot, body);
          panel.append(status);
        });

        for (const [mobileWidth, largeText] of [[320, false], [390, false], [430, false], [390, true]]) {
          await page.setViewportSize({ width: mobileWidth, height: 950 });
          await page.evaluate(large => {
            document.documentElement.style.fontSize = large ? "200%" : "";
          }, largeText);
          const layout = await page.locator(".statement-upload-status").evaluate(status => {
            const rect = status.getBoundingClientRect();
            const panel = document.querySelector(".statement-upload-panel").getBoundingClientRect();
            return {
              left: rect.left, right: rect.right, width: window.innerWidth,
              panelLeft: panel.left, panelRight: panel.right,
              fits: status.scrollWidth <= status.clientWidth + 1,
              wraps: getComputedStyle(status.querySelector("span:last-child")).overflowWrap
            };
          });
          assert.ok(layout.left >= layout.panelLeft - 1 && layout.right <= layout.panelRight + 1 &&
            layout.right <= layout.width + 1 && layout.fits && layout.wraps === "anywhere",
            "Long statement filename must fit within mobile upload status: " + JSON.stringify(layout));
          assert.ok(await page.evaluate(() => document.documentElement.scrollWidth <= innerWidth + 1),
            "Long filename must not cause sideways page scrolling.");
        }
        await page.evaluate(() => document.documentElement.style.fontSize = "");
        await page.setViewportSize({ width: 320, height: 1100 });
      }
    }
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "currency-acceptance-failure.png"), fullPage: true });
    throw error;
  } finally {
    await page.unroute(currencyBillRoutePattern, currencyRoute);
    await page.unroute("**/bff/bank-connections", currencyConnectionRoute);
  }

  // MV-01: real authenticated Overview with controlled BFF snapshots, not fixture markup.
  const overviewBillPattern = /\/bff\/bill-streams(?:\?.*)?$/;
  let overviewBills = [];
  let overviewFailure = false;
  let overviewConnections = [{ id: "88888888-8888-8888-8888-888888888888",
    institutionName: "Synthetic bank", status: 0,
    lastSuccessfulSyncAtUtc: "2026-02-01T00:00:00Z" }];
  const overviewWrites = [];
  const observeOverviewWrites = request => {
    if (/\/bff\/(bill-streams|bank-connections)/.test(new URL(request.url()).pathname) &&
        request.method() !== "GET") overviewWrites.push(request.method());
  };
  const overviewBillRoute = async route => {
    await route.fulfill({ status: overviewFailure ? 503 : 200,
      contentType: "application/json",
      body: overviewFailure ? '{"error":"Controlled failure"}' : JSON.stringify(overviewBills) });
  };
  const overviewBankRoute = async route => {
    await route.fulfill({ status: 200, contentType: "application/json",
      body: JSON.stringify(overviewConnections) });
  };
  const exampleBill = (id, amount, previous, extra = {}) => ({
    id, providerName: "Synthetic bill", category: "Other", isActive: true,
    currentAmount: amount, previousAverage: previous, currencyCode: "USD",
    hasTransactionEvidence: true, hasMonthlyCadenceEvidence: true, ...extra
  });
  const overviewCases = [
    ["no-history", [
      exampleBill("11111111-1111-1111-1111-111111111111", 29, 0),
      exampleBill("22222222-2222-2222-2222-222222222222", 13.8, 0)
    ], "Comparison history needed", "$42.80"],
    ["compared-zero", [exampleBill("11111111-1111-1111-1111-111111111111", 29, 29)],
      "No increases in compared bills", "$29.00"],
    ["partial", [
      exampleBill("11111111-1111-1111-1111-111111111111", 29, 29),
      exampleBill("22222222-2222-2222-2222-222222222222", 13.8, 0)
    ], "Only some bills can be compared", "$42.80"],
    ["unknown-currency", [
      exampleBill("11111111-1111-1111-1111-111111111111", 999, 10, {currencyCode: null})
    ], "Bill amounts need review", "Amount unavailable"],
    ["nonmonthly-increase", [
      exampleBill("11111111-1111-1111-1111-111111111111", 29, 20,
        {hasMonthlyCadenceEvidence: false})
    ], "Bills need review", "$29.00"]
  ];
  page.on("request", observeOverviewWrites);
  await page.route(overviewBillPattern, overviewBillRoute);
  await page.route("**/bff/bank-connections", overviewBankRoute);
  try {
    for (const [width, theme] of [[320, "dark"], [1440, "light"]]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.evaluate(value => localStorage.setItem("billwatch-theme", value), theme);
      for (const [name, bills, heading, amount] of overviewCases) {
        overviewBills = bills;
        await page.goto("/app", {waitUntil: "domcontentloaded"});
        await settle(".fw-pulse-amount");
        assert.equal((await page.locator(".fw-pulse-amount").innerText()).trim(), amount);
        assert.equal(await page.locator(".fw-signal-card h2").innerText(), heading);
        assert.ok((await page.locator(".fw-pulse").innerText()).includes(
          "This is not total spending or a monthly budget"));
        assert.ok(!(await page.locator(".app-page").innerText()).includes("Everything looks steady"));
        if (name === "compared-zero") {
          assert.ok((await page.locator(".fw-pulse-rail").innerText()).includes("$0.00"));
          assert.ok((await page.locator(".fw-pulse-rail").innerText()).includes("No increase to project"));
        }
        if (name === "nonmonthly-increase") {
          assert.ok((await page.locator(".fw-pulse-rail").innerText()).includes("Monthly timing not established"));
        }
        assert.ok(!(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)),
          "Overview coverage and money states must fit the viewport");
        await page.screenshot({path: path.join(outputDir, "overview-" + name + "-" + width + ".png"),
          fullPage: true});
      }
    }

    overviewBills = overviewCases[0][1];
    await page.setViewportSize({width: 390, height: 844});
    await page.goto("/app", {waitUntil: "domcontentloaded"});
    await settle(".fw-pulse-amount");
    overviewFailure = true;
    await page.getByRole("button", {name: "Check status", exact: true}).click();
    await page.getByRole("status").filter({hasText: "Previously loaded records"}).waitFor();
    assert.equal((await page.locator(".fw-pulse-amount").innerText()).trim(), "$42.80");
    overviewFailure = false;
    await page.getByRole("button", {name: "Check status", exact: true}).click();
    await page.getByRole("status").filter({hasText: "Previously loaded records"}).waitFor({state: "hidden"});

    overviewConnections = [];
    await page.goto("/app", {waitUntil: "domcontentloaded"});
    await settle(".fw-pulse-amount");
    assert.ok((await page.locator(".app-page").innerText()).includes("These are saved bill records."));
    assert.equal((await page.locator(".fw-pulse-amount").innerText()).trim(), "$42.80");
    await page.evaluate(() => document.documentElement.style.fontSize = "200%");
    const noticeLayout = await page.getByRole("status").filter({hasText: "These are saved bill records."})
      .evaluate(notice => {
        const text = notice.querySelector("p").getBoundingClientRect();
        const link = notice.querySelector("a").getBoundingClientRect();
        return {textBottom: text.bottom, linkTop: link.top, linkWidth: link.width, linkHeight: link.height};
      });
    assert.ok(noticeLayout.linkTop >= noticeLayout.textBottom,
      "At phone width, the connection action must stack below notice text, not squeeze beside it");
    assert.ok(noticeLayout.linkWidth >= 100 && noticeLayout.linkHeight >= 44,
      "The connection link must remain readable and touch-sized with enlarged text");
    assert.ok(!(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1)),
      "Overview must support enlarged text without horizontal overflow");
    await page.screenshot({path: path.join(outputDir, "overview-saved-records-enlarged.png"), fullPage: true});

    overviewFailure = true;
    await page.goto("/app", {waitUntil: "domcontentloaded"});
    await settle(".app-error-state");
    assert.equal(await page.locator(".fw-pulse-amount").count(), 0);
    overviewFailure = false;
    await page.getByRole("button", {name: "Try again", exact: true}).click();
    await settle(".fw-pulse-amount");
    assert.deepEqual(overviewWrites, [], "Overview status checks and evidence links must not mutate data");
  } catch (error) {
    await page.screenshot({path: path.join(outputDir, "overview-trust-failure.png"), fullPage: true});
    throw error;
  } finally {
    page.off("request", observeOverviewWrites);
    await page.unroute(overviewBillPattern, overviewBillRoute);
    await page.unroute("**/bff/bank-connections", overviewBankRoute);
  }

  // Exercise the real authenticated Bills page, not duplicated fixture markup.
  // This account and every bank/transaction record are disposable CI data.
  await (await import("./bills-workspace.test.mjs")).verifyBillsWorkspace(page, outputDir);

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

  await (await import("./privacy-controls.test.mjs")).verifyPrivacyControls(page, outputDir);

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


  // Synthetic BFF evidence only; no bank connection or production data is mutated.
  const transactionEvidence = [
    { id: "aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", bankAccountId: "11111111-1111-1111-1111-111111111111",
      name: "Euro utility", merchantName: "Synthetic Euro Utility", institutionName: "Synthetic Bank",
      accountName: "Euro account", amount: 20, isoCurrencyCode: "EUR", postedDate: "2026-10-10", isPending: false },
    { id: "bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb", bankAccountId: "22222222-2222-2222-2222-222222222222",
      name: "Dollar purchase", merchantName: "Synthetic Dollar Purchase", institutionName: "Synthetic Bank",
      accountName: "Dollar account", amount: 10, isoCurrencyCode: "USD", postedDate: "2026-10-11", isPending: false },
    { id: "cccccccc-cccc-cccc-cccc-cccccccccccc", bankAccountId: "22222222-2222-2222-2222-222222222222",
      name: "Unknown purchase", merchantName: "Synthetic Unknown Purchase", institutionName: "Synthetic Bank",
      accountName: "Dollar account", amount: 42, isoCurrencyCode: null, postedDate: "2026-10-12", isPending: false },
    { id: "dddddddd-dddd-dddd-dddd-dddddddddddd", bankAccountId: "22222222-2222-2222-2222-222222222222",
      name: "Pending purchase", merchantName: "Synthetic Pending Purchase", institutionName: "Synthetic Bank",
      accountName: "Dollar account", amount: 999, isoCurrencyCode: "USD", postedDate: "2026-10-13", isPending: true },
    { id: "eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee", bankAccountId: "11111111-1111-1111-1111-111111111111",
      name: "Refund", merchantName: "Synthetic Refund", institutionName: "Synthetic Bank",
      accountName: "Euro account", amount: -5, isoCurrencyCode: "EUR", postedDate: "2026-10-14", isPending: false }
  ];
  const ledgerRoute = "**/bff/bank-transactions?*";
  await page.route(ledgerRoute, route => route.fulfill({ json: transactionEvidence }));
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/app/account/transactions", { waitUntil: "domcontentloaded" });
    await settle(".transaction-row");
    assert.equal(await page.locator(".transaction-row").count(), 5);
    const summary = await page.locator(".fw-transactions-total").innerText();
    assert.ok(summary.includes("EUR 20.00 · $10.00"), summary);
    assert.ok(summary.includes("1 posted outflow records"), summary);
    assert.ok(summary.includes("This is not proof of complete bank coverage."), summary);
    if (width === 390) assert.equal(await page.locator(".transaction-mobile-date:visible").count(), 5);
    assert.ok((await page.locator(".transaction-row").nth(2).innerText()).includes("-42.00 (Currency unavailable)"));
    assert.ok((await page.locator(".transaction-row").nth(4).innerText()).includes("+EUR 5.00"));
    await page.getByRole("searchbox", { name: "Search merchant or transaction" }).fill("Refund");
    await page.waitForFunction(() =>
      [...document.querySelectorAll(".transaction-row")].filter(row => !row.hidden).length === 1);
    const visible = await page.locator(".transaction-row:visible").innerText();
    assert.ok(visible.includes("Synthetic Refund"), visible);
    assert.ok(!visible.includes(".ToString"), visible);
    if (width === 390) {
      assert.ok((await page.locator(".transaction-row:visible .transaction-mobile-date").innerText()).includes("14 Oct 2026"), visible);
    } else {
      assert.ok((await page.locator(".transaction-row:visible .transaction-date").innerText()).includes("Oct 14"), visible);
    }
    assert.equal(await page.locator(".fw-transactions-total").innerText(), summary);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(outputDir, `transaction-evidence-${width}.png`), fullPage: true });
  }
  await page.getByRole("searchbox", { name: "Search merchant or transaction" }).fill("");
  await page.route("**/bff/bill-monitoring/refresh", route => route.fulfill({ status: 503, json: {} }));
  await page.getByRole("button", { name: "Sync now", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "Sync failed." }).waitFor();
  assert.equal(await page.locator(".transaction-row").count(), 5);
  assert.equal(await page.locator(".app-error-state").count(), 0);
  await page.unroute("**/bff/bill-monitoring/refresh");
  await page.unroute(ledgerRoute);
  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");


  // Disposable CI user + synthetic responses: never mutate real bank data.
  const activityFixtures = [
    { id: "11111111-aaaa-aaaa-aaaa-111111111111", alertType: "ConnectionIssue", severity: "Warning",
      title: "Synthetic connection repair", message: "Reconnect the café account to restore monitoring.",
      isRead: false, isDismissed: false, createdAtUtc: "2026-10-17T10:00:00Z", updatedAtUtc: "2026-10-17T10:00:00Z" },
    { id: "22222222-aaaa-aaaa-aaaa-222222222222", billStreamId: "11111111-1111-1111-1111-111111111111",
      alertType: "BillIncrease", severity: "Critical", title: "Synthetic utility change", message: "Posted amount increased.",
      isRead: false, isDismissed: false, createdAtUtc: "2026-10-16T10:00:00Z", updatedAtUtc: "2026-10-16T10:00:00Z" },
    { id: "33333333-aaaa-aaaa-aaaa-333333333333", alertType: "NewBill", severity: "Info",
      title: "Synthetic recurring bill", message: "A monthly payment was discovered.",
      isRead: true, isDismissed: false, createdAtUtc: "2026-10-15T10:00:00Z", updatedAtUtc: "2026-10-15T10:00:00Z" },
    { id: "44444444-aaaa-aaaa-aaaa-444444444444", alertType: "ConnectionIssue", severity: "Critical",
      title: "Synthetic dismissed item", message: "Not active.", isRead: false, isDismissed: true,
      createdAtUtc: "2026-10-14T10:00:00Z", updatedAtUtc: "2026-10-14T10:00:00Z" }
  ];
  let activityResponse = "good";
  let mutationStatus = 503;
  let alertMutationRequests = 0;
  await page.route("**/bff/alerts?*", route => route.fulfill(activityResponse === "failed"
    ? { status: 503, json: {} } : { json: activityResponse === "null" ? null : activityFixtures }));
  const mutationRoute = /\/bff\/alerts\/[^/]+\/(read|dismiss)$/;
  await page.route(mutationRoute, route => {
    alertMutationRequests++;
    return route.fulfill(mutationStatus === 204 ? { status: 204 } : { status: 503, json: {} });
  });
  async function refreshActivity() {
    await Promise.all([
      page.waitForResponse(response => response.url().includes("/bff/alerts?")),
      page.getByRole("button", { name: "Refresh", exact: true }).click()
    ]);
    await page.waitForFunction(() => {
      const button = document.querySelector(".activity-refresh-button");
      return button && !button.disabled;
    });
  }
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/app/activity", { waitUntil: "domcontentloaded" });
    await settle(".alert-card");
    assert.equal(await page.locator(".alert-card").count(), 3);
    const actionStyle = await page.locator(".alert-secondary-action").first().evaluate(element => {
      const style = getComputedStyle(element);
      return { height: parseFloat(style.minHeight), radius: parseFloat(style.borderRadius) };
    });
    assert.ok(actionStyle.height >= 44 && actionStyle.radius >= 8, JSON.stringify(actionStyle));
    assert.ok((await page.locator(".activity-scope-note").first().innerText()).includes("not your entire history"));
    const search = page.getByRole("searchbox", { name: "Search activity", exact: true });
    await search.fill("CAFÉ");
    await page.waitForFunction(() => document.querySelectorAll(".alert-card").length === 1);
    assert.ok((await page.locator(".alert-card").innerText()).includes("Synthetic connection repair"));
    await page.locator(".activity-filters").getByRole("button", { name: /^Unread/ }).click();
    assert.equal(await page.locator(".alert-card").count(), 1);
    await search.fill("no matching synthetic alert");
    await page.locator(".alerts-filter-empty").waitFor();
    await page.getByRole("button", { name: "Clear filters", exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll(".alert-card").length === 3);
    activityResponse = "failed";
    await refreshActivity();
    await page.getByRole("alert").filter({ hasText: "Previously loaded alerts are still shown" }).waitFor();
    assert.equal(await page.locator(".alert-card").count(), 3);
    assert.equal(await page.locator(".app-error-state").count(), 0);
    activityResponse = "null";
    await refreshActivity();
    await page.getByRole("alert").filter({ hasText: "Previously loaded alerts are still shown" }).waitFor();
    assert.equal(await page.locator(".alert-card").count(), 3);
    activityResponse = "good";
    await refreshActivity();
    await page.waitForFunction(() => !document.querySelector(".activity-action-error"));
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(outputDir, `activity-resilience-${width}.png`), fullPage: true });
  }
  const connectionAlert = page.locator(".alert-card").filter({ hasText: "Synthetic connection repair" });
  await Promise.all([
    page.waitForResponse(response => response.url().endsWith("/read") && response.request().method() === "POST"),
    connectionAlert.getByRole("button", { name: "Mark read", exact: true }).click()
  ]);
  await page.waitForFunction(() => [...document.querySelectorAll(".alert-secondary-action")].every(button => !button.disabled));
  await page.getByRole("alert").filter({ hasText: "could not update that alert" }).waitFor();
  assert.equal(await connectionAlert.locator(".unread-dot").count(), 1);
  await Promise.all([
    page.waitForResponse(response => response.url().endsWith("/dismiss") && response.request().method() === "POST"),
    connectionAlert.getByRole("button", { name: "Dismiss", exact: true }).click()
  ]);
  await page.waitForFunction(() => [...document.querySelectorAll(".alert-secondary-action")].every(button => !button.disabled));
  await page.getByRole("alert").filter({ hasText: "could not update that alert" }).waitFor();
  assert.equal(await page.locator(".alert-card").count(), 3);
  const requestsBeforeReview = alertMutationRequests;
  await connectionAlert.getByRole("link", { name: "Review", exact: true }).click();
  await page.waitForURL(url => url.pathname === "/app/account");
  assert.equal(alertMutationRequests, requestsBeforeReview, "Opening evidence must not depend on an alert write.");
  await page.goto("/app/activity", { waitUntil: "domcontentloaded" });
  await settle(".alert-card");
  mutationStatus = 204;
  await page.locator(".activity-filters").getByRole("button", { name: /^Unread/ }).click();
  await page.locator(".alert-card").filter({ hasText: "Synthetic connection repair" })
    .getByRole("button", { name: "Mark read", exact: true }).click();
  await page.waitForFunction(() => document.querySelectorAll(".alert-card").length === 1);
  assert.ok((await page.locator(".alert-card").innerText()).includes("Synthetic utility change"));
  await page.getByRole("button", { name: "Clear filters", exact: true }).click();
  await page.locator(".alert-card").filter({ hasText: "Synthetic recurring bill" })
    .getByRole("button", { name: "Dismiss", exact: true }).click();
  await page.waitForFunction(() => document.querySelectorAll(".alert-card").length === 2);
  activityResponse = "failed";
  await page.goto("/app/activity", { waitUntil: "domcontentloaded" });
  await settle(".app-error-state");
  assert.equal(await page.locator(".alert-card").count(), 0);
  assert.equal(await page.locator(".alerts-empty").count(), 0);
  activityResponse = "good";
  await page.getByRole("button", { name: "Try again", exact: true }).click();
  await page.locator(".alert-card").first().waitFor();
  await page.unroute("**/bff/alerts?*");
  await page.unroute(mutationRoute);
  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".app-shell");


  // Synthetic account snapshots only. No provider connection is opened or modified.
  const accountConnectionId = "11111111-bbbb-bbbb-bbbb-111111111111";
  const accountRecordId = "22222222-bbbb-bbbb-bbbb-222222222222";
  const syntheticConnections = [
    { id: accountConnectionId, institutionName: "Synthetic first-sync bank", status: 0,
      createdAtUtc: "2026-10-17T10:00:00Z", lastSuccessfulSyncAtUtc: null },
    { id: "33333333-bbbb-bbbb-bbbb-333333333333", institutionName: "Synthetic ended bank", status: 2,
      createdAtUtc: "2026-10-16T10:00:00Z", lastSuccessfulSyncAtUtc: "2026-10-16T11:00:00Z" }
  ];
  const syntheticAccounts = [{ id: accountRecordId, bankConnectionId: accountConnectionId,
    institutionName: "Synthetic first-sync bank", name: "Synthetic checking", officialName: "Synthetic checking",
    mask: "1234", accountType: "Depository", accountSubtype: "checking", isActive: true }];
  let snapshotMode = "good";
  let syntheticArchived = false;
  let archiveStatus = 503;
  let archiveWrites = 0;
  let syntheticDisconnected = false;
  let disconnectStatus = 503;
  let syntheticDeletes = 0;
  const connectionsSnapshot = () => [
    ...syntheticConnections.map((item, index) => ({
      ...item, isArchived: index === 1 && syntheticArchived, status: snapshotMode === "ended" || syntheticDisconnected ? 2 :
        snapshotMode === "unknown" && index === 0 ? 99 : item.status,
      institutionName: snapshotMode === "partial" && index === 0 ? "Must not replace the old snapshot" : item.institutionName
    })),
    ...(snapshotMode === "mixed" ? [{
      id: "44444444-bbbb-bbbb-bbbb-444444444444", institutionName: "Synthetic previously synced bank",
      status: 0, createdAtUtc: "2026-10-16T10:00:00Z",
      lastSuccessfulSyncAtUtc: "2026-10-18T10:00:00Z"
    }] : [])
  ];
  await page.route("**/bff/bank-connections", route => {
    if (snapshotMode === "failed") return route.fulfill({ status: 503, json: {} });
    return route.fulfill({ json: snapshotMode === "empty" ? [] : connectionsSnapshot() });
  });
  await page.route("**/bff/bank-accounts", route => {
    if (snapshotMode === "failed" || snapshotMode === "partial") return route.fulfill({ status: 503, json: {} });
    return route.fulfill({ json: snapshotMode === "invalid" ? null :
      syntheticDisconnected || snapshotMode === "empty" || snapshotMode === "ended" ? [] : syntheticAccounts });
  });
  await page.route(`**/bff/bank-connections/${accountConnectionId}`, route => {
    assert.equal(route.request().method(), "DELETE");
    syntheticDeletes++;
    if (disconnectStatus === 204) {
      syntheticDisconnected = true;
      snapshotMode = "failed";
      return route.fulfill({ status: 204 });
    }
    return route.fulfill({ status: 503, json: {} });
  });
  async function refreshAccount() {
    await Promise.all([
      page.waitForResponse(response => response.url().endsWith("/bff/bank-accounts")),
      page.getByRole("button", { name: "Refresh account", exact: true }).click()
    ]);
    await page.waitForFunction(() => {
      const button = document.querySelector(".account-refresh-button");
      return button && !button.disabled;
    });
  }
  for (const width of [390, 1440]) {
    snapshotMode = "good";
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/app/account", { waitUntil: "domcontentloaded" });
    await settle(".connection-row");
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Awaiting first sync"));
    assert.equal(await page.locator(".financial-account-row").count(), 1);
    snapshotMode = "mixed";
    await refreshAccount();
    const mixedCoverage = await page.locator(".fw-account-stats").innerText();
    assert.ok(mixedCoverage.includes("Some banks have not synced"));
    assert.ok(mixedCoverage.includes("1 of 2 active banks report a successful sync"));
    assert.ok(!mixedCoverage.includes("Awaiting first sync") && !mixedCoverage.includes("Reported connected"),
      "A previously synchronized bank must not send Account back to initial-scan messaging.");
    assert.equal(await page.locator(".connection-row").count(), 3);
    assert.equal(await page.locator(".financial-account-row").count(), 1,
      "A bank status snapshot must not fabricate accounts or change financial ownership.");
    snapshotMode = "good";
    await refreshAccount();
    snapshotMode = "partial";
    await refreshAccount();
    await page.getByRole("alert").filter({ hasText: "Previously loaded data is still shown" }).waitFor();
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Previously loaded state"));
    assert.ok(!(await page.locator(".connection-list").innerText()).includes("Must not replace"));
    assert.equal(await page.locator(".financial-account-row").count(), 1);
    snapshotMode = "invalid";
    await refreshAccount();
    assert.equal(await page.locator(".connection-row").count(), 2);
    snapshotMode = "unknown";
    await refreshAccount();
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Unknown connection state"));
    snapshotMode = "ended";
    await refreshAccount();
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Connections ended"));
    assert.ok(!(await page.locator(".fw-account-stats").innerText()).includes("healthy"));
    snapshotMode = "empty";
    await refreshAccount();
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("No banks connected"));
    snapshotMode = "good";
    await refreshAccount();
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Awaiting first sync"));
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(outputDir, `account-connection-state-${width}.png`), fullPage: true });
  }
  const endedConnectionId = syntheticConnections[1].id;
  const archivePattern = `**/bff/bank-connections/${endedConnectionId}/{archive,restore}`;
  const archiveHandler = route => {
    assert.equal(route.request().method(), "POST");
    archiveWrites++;
    if (archiveStatus !== 204) return route.fulfill({ status: archiveStatus, json: {} });
    syntheticArchived = route.request().url().endsWith("/archive");
    return route.fulfill({ status: 204 });
  };
  await page.route(archivePattern, archiveHandler);
  const endedRow = page.locator(".connection-row").filter({ hasText: "Synthetic ended bank" });
  await endedRow.getByRole("button", { name: "Archive connection", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "The connection could not be updated" }).waitFor();
  assert.equal(await endedRow.count(), 1, "A failed archive must leave the connection visible.");
  assert.equal(archiveWrites, 1);
  archiveStatus = 204;
  await endedRow.getByRole("button", { name: "Archive connection", exact: true }).click();
  await page.getByRole("status").filter({ hasText: "Connection archived." }).waitFor();
  assert.equal(await endedRow.count(), 0);
  assert.equal(await page.locator(".financial-account-row").count(), 1);
  for (const width of [390, 1440]) {
    await page.setViewportSize({ width, height: 1000 });
    await page.goto("/app/account", { waitUntil: "domcontentloaded" });
    await settle(".connection-row");
    assert.equal(await endedRow.count(), 0, "A fresh page must respect the server archive preference.");
    const showArchived = page.getByRole("button", { name: "Show archived (1)", exact: true });
    await showArchived.click();
    await endedRow.getByRole("button", { name: "Restore connection", exact: true }).waitFor();
    assert.ok((await endedRow.innerText()).includes("Archived"));
    assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Awaiting first sync"));
    const restore = endedRow.getByRole("button", { name: "Restore connection", exact: true });
    await restore.evaluate(element => element.scrollIntoView({ block: "center", behavior: "instant" }));
    const bounds = await restore.boundingBox();
    assert.ok(bounds && bounds.height >= 44);
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
    await page.screenshot({ path: path.join(outputDir, `account-archive-${width}.png`), fullPage: true });
    await page.getByRole("button", { name: "Hide archived", exact: true }).click();
    await endedRow.waitFor({ state: "detached" });
    assert.equal(await endedRow.count(), 0);
  }
  await page.getByRole("button", { name: "Show archived (1)", exact: true }).click();
  await endedRow.getByRole("button", { name: "Restore connection", exact: true }).click();
  await page.getByRole("status").filter({ hasText: "Connection restored to the list." }).waitFor();
  assert.ok((await endedRow.innerText()).includes("Connection ended"));
  assert.equal(await endedRow.getByRole("button", { name: "Disconnect", exact: true }).count(), 0);
  assert.equal(await page.getByRole("button", { name: "Hide archived", exact: true }).count(), 0);
  assert.equal(archiveWrites, 3);
  assert.equal(syntheticDeletes, 0, "Archive and restore must never invoke provider disconnection.");
  await page.unroute(archivePattern, archiveHandler);
  const firstSyncRow = page.locator(".connection-row").filter({ hasText: "Synthetic first-sync bank" });
  await firstSyncRow.getByRole("button", { name: "Disconnect", exact: true }).click();
  await page.locator(".fw-confirm-dialog").getByRole("button", { name: "Keep connected", exact: true }).click();
  assert.equal(syntheticDeletes, 0);
  await firstSyncRow.getByRole("button", { name: "Disconnect", exact: true }).click();
  await page.locator(".fw-confirm-dialog").getByRole("button", { name: "Disconnect bank", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "could not disconnect" }).waitFor();
  assert.equal(syntheticDeletes, 1);
  assert.equal(await page.locator(".financial-account-row").count(), 1);
  disconnectStatus = 204;
  await firstSyncRow.getByRole("button", { name: "Disconnect", exact: true }).click();
  await page.locator(".fw-confirm-dialog").getByRole("button", { name: "Disconnect bank", exact: true }).click();
  await page.getByRole("alert").filter({ hasText: "Previously loaded data is still shown" }).waitFor();
  assert.equal(syntheticDeletes, 2);
  assert.equal(await page.locator(".financial-account-row").count(), 0);
  assert.equal(await page.getByRole("button", { name: "Disconnect", exact: true }).count(), 0);
  assert.ok((await page.locator(".account-action-message.success").innerText()).includes("was disconnected"));
  snapshotMode = "good";
  await refreshAccount();
  assert.equal(syntheticDeletes, 2, "Retry refresh must not repeat a disconnect write.");
  assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Connections ended"));
  snapshotMode = "failed";
  await page.goto("/app/account", { waitUntil: "domcontentloaded" });
  await settle(".app-error-state");
  assert.ok((await page.locator(".fw-account-stats").innerText()).includes("Connection state unavailable"));
  assert.ok(!(await page.locator(".fw-account-stats").innerText()).includes("healthy"));
  snapshotMode = "good";
  await page.getByRole("button", { name: "Try again", exact: true }).click();
  await page.locator(".connection-row").first().waitFor();
  await page.unroute("**/bff/bank-connections");
  await page.unroute("**/bff/bank-accounts");
  await page.unroute(`**/bff/bank-connections/${accountConnectionId}`);

  // Isolate optional button-wiring failure from authenticated data reads.
  const toolsContext = await browser.newContext({ baseURL: baseUrl, ignoreHTTPSErrors: true, serviceWorkers: "block" });
  await toolsContext.addCookies(await context.cookies());
  const toolsPage = await toolsContext.newPage();
  await toolsPage.route("**/bff/bank-connections", route => route.fulfill({ json: syntheticConnections }));
  await toolsPage.route("**/bff/bank-accounts", route => route.fulfill({ json: syntheticAccounts }));
  await toolsPage.addInitScript(() => {
    const getElementById = Document.prototype.getElementById;
    Document.prototype.getElementById = function(id) {
      if (id === "connect-bank-button" && !globalThis.__ciToolsRecovered) {
        globalThis.__ciToolFailures = (globalThis.__ciToolFailures ?? 0) + 1;
        throw new Error("Synthetic connection-tool DOM lookup failure");
      }
      return getElementById.call(this, id);
    };
  });
  await toolsPage.goto("/app/account", { waitUntil: "domcontentloaded" });
  await toolsPage.locator(".app-shell").waitFor();
  // A fresh isolated browser context has no acknowledged release notes.
  // Wait for the asynchronous What's New fetch/hydration, then acknowledge
  // the actual modal before interacting with underlying account controls.
  // An early isVisible() check races hydration and allows the modal to open
  // during the later retry click, incorrectly intercepting it.
  const freshReleaseDialog = toolsPage.locator(".whats-new-dialog");
  await freshReleaseDialog.waitFor({ state: "visible", timeout: 15000 });
  assert.ok(await freshReleaseDialog.locator(".whats-new-item").count() > 0,
    "A fresh browser context must show substantive release notes.");
  await freshReleaseDialog.getByRole("button", { name: "Explore FullWorth" }).click();
  await freshReleaseDialog.waitFor({ state: "detached", timeout: 10000 });
  try {
    await toolsPage.getByRole("alert").filter({ hasText: "Connection tools are unavailable" }).waitFor();
  } catch (error) {
    await toolsPage.screenshot({ path: path.join(outputDir, "account-tool-initialization-failure.png"), fullPage: true });
    console.error("Synthetic tool fixture state", {
      injectedFailures: await toolsPage.evaluate(() => globalThis.__ciToolFailures ?? 0),
      path: new URL(toolsPage.url()).pathname,
      connectionRows: await toolsPage.locator(".connection-row").count(),
      initialError: await toolsPage.locator(".app-error-state").count(),
      welcomeVisible: await toolsPage.locator(".whats-new-dialog").isVisible()
    });
    throw error;
  }
  assert.ok((await toolsPage.evaluate(() => globalThis.__ciToolFailures ?? 0)) > 0);
  assert.equal(await toolsPage.locator(".connection-row").count(), 2);
  assert.equal(await toolsPage.locator(".financial-account-row").count(), 1);
  assert.equal(await toolsPage.locator(".app-error-state").count(), 0);
  await toolsPage.evaluate(() => { globalThis.__ciToolsRecovered = true; });
  await toolsPage.getByRole("button", { name: "Retry connection tools", exact: true }).click();
  await toolsPage.waitForFunction(() => {
    const button = document.getElementById("connect-bank-button");
    return button && !button.disabled;
  });
  assert.equal(await toolsPage.getByRole("alert").filter({ hasText: "Connection tools are unavailable" }).count(), 0);
  await toolsContext.close();
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

  await verifyPlanningWorkspace(page, outputDir);

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
