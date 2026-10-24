import assert from "node:assert/strict";
import path from "node:path";
import { verifyInPageNavigation } from "./in-page-navigation.test.mjs";
import { verifyNavigationRecovery } from "./navigation-recovery.test.mjs";

// Real authenticated components; all planning/bill reads and writes below are intercepted synthetic fixtures.
export async function verifyPlanningWorkspace(page, outputDir) {
  const id = "d8d88252-55c1-444c-9496-e0b63a941a61";
  const secondId = "e8d88252-55c1-444c-9496-e0b63a941a61";
  const provider = "Synthetic Mountain & Valley Community Utilities — Household Services";
  const bills = [
    { id, providerName: provider, isActive: true, currentAmount: 89.25, currencyCode: "USD" },
    { id: secondId, providerName: "Synthetic Regional Internet", isActive: true, currentAmount: 45, currencyCode: "EUR" },
    { id: "f8d88252-55c1-444c-9496-e0b63a941a61", providerName: "Inactive synthetic bill", isActive: false, currentAmount: 999, currencyCode: "USD" }
  ];
  let schedule = { frequency: "Biweekly", anchorPayDate: "2026-10-16", secondaryDayOfMonth: null, defaultPaychecksAhead: 2 };
  let preferences = [{ billStreamId: id, paychecksAheadOverride: 3 }];
  const plans = [
    { id, paycheckPostedDate: "2026-10-16", paycheckAmount: 100, currencyCode: "USD",
      recommendedSetAside: 130, paycheckRemainingAfterPlan: -30, shortfall: 30,
      items: [{ providerName: provider, billDueDate: "2026-10-26", plannedAmount: 130, currencyCode: "USD" }] },
    { id: secondId, paycheckPostedDate: "2026-10-15", paycheckAmount: 300, currencyCode: "EUR",
      recommendedSetAside: 45, paycheckRemainingAfterPlan: 255, shortfall: 0,
      items: [{ providerName: "Synthetic Regional Internet", billDueDate: "2026-10-29", plannedAmount: 45, currencyCode: "EUR" }] }
  ];
  const changes = [{ changeId: id, providerName: provider, billDueDate: "2026-10-26", currencyCode: "USD",
    previousAmount: 79.25, currentAmount: 89.25, amountDifference: 10, annualizedImpact: 120,
    description: "Synthetic statement-supported service price change.", recalculationStatus: "Ready", remainingAmountToPlan: 20 }];
  const failures = new Set();
  let empty = false, lostSave = false, held = null, releaseWrite;
  const writes = [], reads = [];
  const pattern = /\/bff\/(?:planning\/.*|bill-streams(?:\?.*)?)$/;
  const handler = async route => {
    const request = route.request(), url = new URL(request.url()), endpoint = url.pathname;
    if (request.method() !== "GET") {
      assert.ok(endpoint.startsWith("/bff/planning/"), "Never mutate actual financial fixture sources.");
      assert.ok(["PUT", "DELETE"].includes(request.method()));
      writes.push({ method: request.method(), endpoint, data: request.postDataJSON() });
      if (held === endpoint) await new Promise(resolve => { releaseWrite = resolve; });
      if (endpoint === "/bff/planning/pay-schedule") schedule = request.postDataJSON();
      else {
        const billStreamId = endpoint.split("/").pop();
        preferences = preferences.filter(p => p.billStreamId !== billStreamId);
        if (request.method() === "PUT") preferences.push({ billStreamId, ...request.postDataJSON() });
      }
      if (lostSave) return route.fulfill({ status: 503, json: {} }); // synthetic commit with a lost success response
      return route.fulfill({ json: endpoint.endsWith("pay-schedule") ? schedule : {} });
    }
    reads.push(endpoint);
    if (failures.has(endpoint)) return route.fulfill({ status: 503, json: {} });
    const json = endpoint.endsWith("/pay-schedule") ? (empty ? null : schedule) :
      endpoint.endsWith("/bill-funding-preferences") ? (empty ? [] : preferences) :
      endpoint.endsWith("/upcoming-bill-changes") ? (empty ? [] : changes) :
      endpoint.endsWith("/recent") ? (empty ? [] : plans) : empty ? [] : bills;
    if (endpoint.endsWith("/pay-schedule") && empty) return route.fulfill({ status: 404, json: {} });
    return route.fulfill({ json });
  };
  await page.route(pattern, handler);
  const panel = name => page.locator('section[aria-labelledby="planning-' + name + '-title"]');
  const loaded = async () => {
    await page.locator(".planning-workspace").waitFor();
    await page.waitForFunction(() => !document.querySelector(".planning-section-loading"));
  };
  const goto = async () => { await page.goto("/app/planning", { waitUntil: "domcontentloaded" }); await loaded(); };
  const photo = async name => {
    await page.mouse.move(0, 0);
    await page.evaluate(() => window.scrollTo({ top: 0, behavior: "instant" }));
    await page.screenshot({ path: path.join(outputDir, name + ".png"), fullPage: true });
  };
  const checkLayout = async () => {
    assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
      "Planning must not horizontally scroll.");
    for (const target of await page.locator(".planning-page button:enabled, .planning-page select:enabled, .planning-page input:enabled, .planning-page summary, .planning-page a").all()) {
      if (!(await target.isVisible())) continue;
      await target.evaluate(e => e.scrollIntoView({ block: "center", behavior: "instant" }));
      const r = await target.evaluate(e => {
        const box = e.getBoundingClientRect(), hit = document.elementFromPoint(box.left + box.width / 2, box.top + box.height / 2);
        return { height: box.height, left: box.left, right: box.right, width: innerWidth, hit: e === hit || e.contains(hit) };
      });
      assert.ok(r.height >= 44 && r.left >= 0 && r.right <= r.width + 1 && r.hit,
        "Planning control must be readable and reachable: " + JSON.stringify(r));
    }
  };
  try {
    await verifyNavigationRecovery(page, outputDir);
    await page.setViewportSize({ width: 1440, height: 1100 });
    await verifyInPageNavigation(page, "/terms", ".legal-toc", 15, outputDir, "terms-section-navigation");
    await verifyInPageNavigation(page, "/privacy", ".legal-toc", 11, outputDir, "privacy-section-navigation");
    for (const [width, theme, large, language] of [[390,"dark",false,"en-US"],[1440,"dark",false,"en-US"],[320,"dark",false,"en-US"],[390,"light",false,"en-US"],[390,"dark",true,"en-US"],[390,"dark",false,"es"]]) {
      await page.setViewportSize({ width, height: 1000 });
      await page.evaluate(t => localStorage.setItem("billwatch-theme", t), theme);
      await page.context().addCookies([{ name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=" + language), url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);
      await goto();
      if (width === 390 && theme === "dark" && !large && language === "en-US") {
        await verifyInPageNavigation(page, "/app/planning", ".planning-jump-links", 4, outputDir, "planning-section-navigation");
        await goto();
      }
      if (large) await page.evaluate(() => document.documentElement.style.fontSize = "200%");
      assert.equal(await page.locator(".planning-bill-row").count(), 2, "Inactive bills must stay excluded.");
      assert.equal(await panel("recent").locator(".planning-shortfall-note").count(), 1);
      assert.ok((await panel("recent").textContent()).includes("-30.00 USD"));
      assert.ok((await panel("recent").textContent()).includes("255.00 EUR"));
      assert.ok((await panel("changes").textContent()).includes("+10.00 USD"));
      assert.ok((await panel("changes").textContent()).includes("+120.00 USD"));
      assert.ok((await page.locator(".planning-guidance").textContent()).includes(language === "es" ? "no un saldo bancario" : "not a bank balance"));
      for (const input of await page.locator(".planning-fields input, .planning-fields select").all())
        assert.ok(await input.evaluate(e => parseFloat(getComputedStyle(e).fontSize)) >= (large ? 32 : 16));
      await checkLayout();
      await photo("planning-" + width + "-" + theme + (large ? "-200pct" : "") + "-" + language);
      if (width === 390 && theme === "dark" && !large && language === "en-US")
        await page.screenshot({ path: path.join(outputDir, "planning-phone-preview.png"), fullPage: false });
    }
    await page.context().addCookies([{ name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=en-US"), url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);
    await page.setViewportSize({ width: 390, height: 1000 });
    await goto();
    const schedulePanel = panel("schedule"), billPanel = panel("bills");
    const ahead = schedulePanel.locator('input[type="number"]').last();
    await ahead.fill("4"); await ahead.blur();
    await page.waitForFunction(() => document.querySelector('section[aria-labelledby="planning-schedule-title"] .planning-save-state')?.textContent.includes("Unsaved"));
    const financialBefore = await panel("recent").textContent();

    // A failed history read must not hide the editor or erase edits; retry only that endpoint.
    failures.add("/bff/planning/payday-plans/recent");
    await goto();
    await ahead.fill("4"); await ahead.blur();
    assert.equal(await panel("recent").locator(".planning-error").count(), 1);
    assert.equal(await panel("recent").locator(".planning-empty").count(), 0, "Failure is not an empty history.");
    await photo("planning-partial-failure");
    failures.clear();
    const readsBefore = reads.length;
    await panel("recent").getByRole("button", { name: "Try again", exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll('section[aria-labelledby="planning-recent-title"] .planning-change-row').length === 2);
    assert.deepEqual(reads.slice(readsBefore), ["/bff/planning/payday-plans/recent"]);
    assert.equal(await ahead.inputValue(), "4");

    // Save is locked while pending; a lost response cannot trigger an automatic second PUT.
    held = "/bff/planning/pay-schedule"; lostSave = true;
    const countBefore = writes.length;
    await schedulePanel.getByRole("button", { name: "Save schedule", exact: true }).click();
    await page.waitForFunction(() => document.querySelector(".planning-fields").disabled);
    const deadline = Date.now() + 10000;
    while (!releaseWrite && Date.now() < deadline) await new Promise(resolve => setTimeout(resolve, 20));
    assert.ok(releaseWrite, "Expected held synthetic save.");
    assert.equal(writes.length, countBefore + 1);
    assert.equal(writes.at(-1).data.defaultPaychecksAhead, 4);
    assert.equal(await schedulePanel.getByRole("button", { name: "Discard edits", exact: true }).isDisabled(), true);
    releaseWrite(); held = null; releaseWrite = null;
    await schedulePanel.getByRole("button", { name: "Check saved schedule", exact: true }).waitFor();
    assert.equal(await ahead.inputValue(), "4");
    await photo("planning-uncertain-save");
    lostSave = false;
    await schedulePanel.getByRole("button", { name: "Check saved schedule", exact: true }).click();
    await page.waitForFunction(() => !document.querySelector(".planning-fields").disabled);
    assert.equal(await schedulePanel.getByRole("button", { name: "Save schedule", exact: true }).isDisabled(), true);
    assert.equal(writes.length, countBefore + 1);
    assert.equal(await panel("recent").textContent(), financialBefore, "Editing preferences must not rewrite frozen paycheck evidence.");

    const search = billPanel.locator('input[type="search"]');
    await search.fill("No matching synthetic provider");
    await billPanel.getByRole("button", { name: "Clear search", exact: true }).waitFor();
    assert.equal(await billPanel.locator(".planning-bill-row").count(), 0);
    await billPanel.getByRole("button", { name: "Clear search", exact: true }).click();
    await page.waitForFunction(() => document.querySelectorAll(".planning-bill-row").length === 2);
    const row = page.locator('[data-bill-id="' + id + '"]');
    await row.locator("select").selectOption("5");
    await page.waitForFunction(id => !document.querySelector('[data-bill-id="' + id + '"] button').disabled, id);
    await row.getByRole("button", { name: "Discard edits", exact: true }).click();
    await page.waitForFunction(id => document.querySelector('[data-bill-id="' + id + '"] select').value === "3", id);
    await row.locator("select").selectOption("");
    await page.waitForFunction(id => !document.querySelector('[data-bill-id="' + id + '"] button').disabled, id);
    await row.getByRole("button", { name: "Save", exact: true }).click();
    await billPanel.getByText("Bill planning preference saved.", { exact: true }).waitFor();
    assert.equal(writes.at(-1).method, "DELETE");
    assert.equal(writes.at(-1).endpoint, "/bff/planning/bill-funding-preferences/" + id);
    assert.equal(writes.length, countBefore + 2, "Only two explicitly simulated planning writes are allowed.");

    // All four failure panels must be honest and independently retryable.
    for (const [name, endpoint] of [["schedule","/bff/planning/pay-schedule"],["bills","/bff/planning/bill-funding-preferences"],["changes","/bff/planning/upcoming-bill-changes"]]) {
      failures.add(endpoint); await goto();
      assert.equal(await panel(name).locator(".planning-error").count(), 1);
      assert.equal(await panel(name).locator(".planning-empty").count(), 0);
      assert.equal(await page.locator(".planning-card").count(), 4);
      await photo("planning-failed-" + name);
      failures.clear();
    }
    empty = true; await goto();
    assert.equal(await page.locator(".planning-empty").count(), 3);
    assert.equal(await schedulePanel.locator(".planning-save-state").textContent(), "Not configured");
    await checkLayout();
    await photo("planning-empty");
    console.log("Planning workspace suite passed: independent reads, safe drafts, uncertain writes, currency-preserving history, mobile/theme/text/localization.");
  } finally {
    if (releaseWrite) releaseWrite();
    await page.unroute(pattern, handler);
    await page.context().addCookies([{ name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=en-US"), url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);
    await page.evaluate(() => document.documentElement.style.removeProperty("font-size"));
  }
}
