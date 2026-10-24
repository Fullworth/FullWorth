import assert from "node:assert/strict";
import path from "node:path";

// Runs inside the real authenticated disposable CI stack. Every financial
// response below is synthetic; no production account or provider is contacted.
export async function verifyBillsWorkspace(page, outputDir) {
  const id = n => String(n).repeat(8) + "-" + String(n).repeat(4) + "-" +
    String(n).repeat(4) + "-" + String(n).repeat(4) + "-" + String(n).repeat(12);
  const bill = (n, providerName, currentAmount, previousAverage, extra = {}) => ({
    id: id(n), providerName, category: "Other", isActive: true, source: "Manual",
    currentAmount, previousAverage, currencyCode: "USD", hasTransactionEvidence: true,
    hasMonthlyCadenceEvidence: true, ...extra
  });
  const bills = [
    bill(1, "Synthetic Café utility", 20, 10, { currencyCode: "EUR" }),
    bill(2, "Synthetic Dollar service", 10, 8),
    bill(3, "Synthetic Steady", 12, 12),
    bill(4, "Synthetic New", 7, 0),
    bill(5, "Synthetic Unavailable", 0, 10, { hasTransactionEvidence: false }),
    bill(6, "Synthetic Unknown currency", 999, 0, { currencyCode: null }),
    bill(7, "Synthetic Decrease", 5, 10),
    bill(8, "Synthetic Saved history", 50, 40, { isActive: false }),
    bill(9, "Synthetic Review candidate", 40, 30, { isActive: false, source: "AutomaticDiscovery" })
  ];
  let listMode = "ok";
  let detailMode = "ok";
  let diagnosticsFail = false;
  let postMode = "ok";
  let posts = 0;
  const confirmed = new Set();
  const pattern = /\/bff\/bill-streams(?:\/[^?]*)?(?:\?.*)?$/;
  const handler = async route => {
    const request = route.request();
    const pathname = new URL(request.url()).pathname;
    if (request.method() === "POST") {
      posts++;
      if (postMode === "lost") return route.abort("failed");
      const matched = bills.find(item => pathname === "/bff/bill-streams/" + item.id + "/confirm");
      assert.ok(matched, "Only an explicit synthetic confirmation may mutate.");
      confirmed.add(matched.id);
      detailMode = "fail-once";
      return route.fulfill({ status: 204 });
    }
    assert.equal(request.method(), "GET");
    if (pathname === "/bff/bill-streams") {
      if (listMode === "fail") return route.fulfill({ status: 503, body: "{}" });
      return route.fulfill({ json: listMode === "empty" ? [] : bills });
    }
    // The bill-detail upload panel can now read the most recent saved upload.
    // These synthetic bills have none; do not treat the status GET as a bill GET.
    if (bills.some(item =>
      pathname === "/bff/bill-streams/" + item.id + "/statement-uploads/latest"))
      return route.fulfill({ status: 204 });
    if (detailMode === "fail-once") {
      detailMode = "ok";
      return route.fulfill({ status: 503, body: "{}" });
    }
    const item = bills.find(value => pathname === "/bff/bill-streams/" + value.id);
    assert.ok(item);
    return route.fulfill({ json: { ...item, ...(confirmed.has(item.id) ?
      { isActive: true, source: "Manual" } : {}), statements: [], changes: [] } });
  };
  const diagnostics = async route => {
    assert.equal(route.request().method(), "GET");
    if (diagnosticsFail) return route.fulfill({ status: 503, body: "{}" });
    return route.fulfill({ json: {
      importedTransactions: 15, eligiblePostedTransactions: 10,
      firstEligiblePostedDate: "2026-01-01", lastEligiblePostedDate: "2026-03-01",
      detectedMonthlyCandidates: 3, acceptedCandidates: 2
    } });
  };
  await page.route(pattern, handler);
  await page.route("**/bff/bill-discovery/diagnostics", diagnostics);
  const waitRows = async count => page.waitForFunction(expected =>
    document.querySelectorAll(".fw-bills-row").length === expected, count);
  const reset = async () => {
    await page.goto("/app/bills", { waitUntil: "domcontentloaded" });
    await page.locator("#bill-search").waitFor();
    await waitRows(7);
  };
  try {
    for (const [width, theme, enlarged] of [
      [1440, "dark", false], [320, "dark", false], [390, "light", false],
      [390, "dark", true]
    ]) {
      await page.setViewportSize({ width, height: 1100 });
      await page.evaluate(value => localStorage.setItem("billwatch-theme", value), theme);
      await reset();
      assert.equal(await page.locator("#bill-sort").inputValue(), "Attention",
        "Bill ledger should start with actionable comparison changes and evidence gaps.");
      assert.ok((await page.locator(".fw-bills-row").first().innerText()).includes("Café utility"),
        "The supported EUR increase should lead its currency group without ranking currencies by value.");
      if (enlarged) await page.evaluate(() => document.documentElement.style.fontSize = "200%");
      const total = page.locator(".fw-bills-total > strong");
      assert.equal((await total.innerText()).trim(), "EUR 20.00 · $34.00");
      await page.locator("#bill-search").fill(" CAFE ");
      await waitRows(1);
      assert.ok((await page.locator(".fw-bills-row").innerText()).includes("Café"));
      assert.equal((await total.innerText()).trim(), "EUR 20.00 · $34.00");
      await page.locator("#bill-search").fill("");
      await waitRows(7);
      await page.locator("#bill-currency").selectOption("USD");
      await waitRows(5);
      await page.locator("#bill-sort").selectOption("LatestCharge");
      await page.waitForFunction(() =>
        document.querySelector(".fw-bills-row")?.textContent.includes("Synthetic Steady"));
      await page.locator(".bill-workspace-tools button").click();
      await waitRows(7);
      assert.equal(await page.locator("#bill-sort").inputValue(), "Attention",
        "Clear filters returns the ledger to the attention-first home view.");
      await page.locator(".bill-extra-filters button").filter({ hasText: "No change" }).click();
      await waitRows(1);
      assert.ok((await page.locator(".fw-bills-row").innerText()).includes("Synthetic Steady"));
      await page.locator(".bill-extra-filters button").filter({ hasText: "Amount unavailable" }).click();
      await waitRows(2);
      assert.ok(!(await page.locator(".fw-bills-list").innerText()).includes("Synthetic Decrease"));
      await page.locator(".bill-workspace-tools button").click();
      await waitRows(7);
      await page.locator(".bill-inactive-history summary").click();
      assert.ok((await page.locator(".bill-inactive-history").innerText()).includes("Synthetic Saved history"));
      await page.locator("#bill-search").fill("Review candidate");
      await waitRows(0);
      assert.ok((await page.locator(".bill-section-count").innerText()).includes("1 of 1"));
      assert.equal((await total.innerText()).trim(), "EUR 20.00 · $34.00");
      await page.locator(".bill-workspace-tools button").click();
      await waitRows(7);
      for (const selector of ["#bill-search", "#bill-currency", "#bill-sort"]) {
        const control = page.locator(selector);
        await control.evaluate(element => element.scrollIntoView({ block: "center", behavior: "instant" }));
        await control.focus();
        const geometry = await control.evaluate(element => {
          const rect = element.getBoundingClientRect();
          return { height: rect.height, left: rect.left, right: rect.right, width: innerWidth };
        });
        assert.ok(geometry.height >= 44 && geometry.left >= 0 && geometry.right <= geometry.width + 1,
          "Workspace controls must be usable at narrow widths and enlarged text: " + selector);
      }
      for (const filter of await page.locator(".fw-bills-filters .bill-filter").all()) {
        const geometry = await filter.evaluate(element => {
          const rect = element.getBoundingClientRect();
          return { height: rect.height, left: rect.left, right: rect.right, width: innerWidth };
        });
        assert.ok(geometry.height >= 44 && geometry.left >= 0 && geometry.right <= geometry.width + 1,
          "All evidence filters must wrap into view with 44px touch targets, not hide in a horizontal strip.");
      }
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false,
        "Bills workspace must not introduce horizontal scrolling.");
      await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
      await page.screenshot({ path: path.join(outputDir,
        "bills-workspace-" + width + "-" + theme + (enlarged ? "-large-text" : "") + ".png"), fullPage: true });
    }

    await page.setViewportSize({ width: 390, height: 844 });
    await reset();
    await page.locator("#bill-search").fill("Café");
    await waitRows(1);
    listMode = "fail";
    await page.getByRole("button", { name: "Refresh bills", exact: true }).click();
    await page.locator(".bill-read-warning").waitFor();
    await waitRows(1);
    assert.equal(await page.locator("#bill-search").inputValue(), "Café");
    assert.equal((await page.locator(".fw-bills-total > strong").innerText()).trim(), "EUR 20.00 · $34.00");
    await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
    await page.screenshot({ path: path.join(outputDir, "bills-refresh-preserved.png"), fullPage: true });
    listMode = "ok";
    await page.getByRole("button", { name: "Refresh bills", exact: true }).click();
    await page.locator(".bill-read-warning").waitFor({ state: "detached" });
    await page.locator(".bill-workspace-tools button").click();
    await waitRows(7);
    await page.getByRole("button", { name: "Check discovery details", exact: true }).click();
    await page.locator(".discovery-summary").waitFor();
    assert.ok((await page.locator(".discovery-summary").innerText()).includes("15"));
    diagnosticsFail = true;
    await page.getByRole("button", { name: "Refresh discovery details", exact: true }).click();
    await page.locator("#discovery-details [role=alert]").waitFor();
    assert.ok((await page.locator(".discovery-summary").innerText()).includes("15"),
      "A failed diagnostics refresh preserves the last counts with a stale warning.");
    assert.ok((await page.locator("#discovery-details").innerText()).includes("may be out of date"));
    // Re-open performs a new read. A failed first read must never manufacture zero counts.
    await page.getByRole("button", { name: "Hide discovery details", exact: true }).click();
    await page.getByRole("button", { name: "Check discovery details", exact: true }).click();
    await page.locator("#discovery-details [role=alert]").waitFor();
    assert.equal(await page.locator(".discovery-summary").count(), 0);
    diagnosticsFail = false;
    await page.locator("#discovery-details").getByRole("button", { name: "Try again", exact: true }).click();
    await page.locator(".discovery-summary").waitFor();
    assert.equal(posts, 0, "Filtering and refresh must never POST, sync, or reactivate bills.");
    listMode = "empty";
    await page.getByRole("button", { name: "Refresh bills", exact: true }).click();
    await page.locator(".bills-empty-state").waitFor();
    assert.equal(await page.locator(".fw-bills-row").count(), 0);
    listMode = "ok";

    await page.goto("/app/bills/" + id(9), { waitUntil: "domcontentloaded" });
    const keep = page.getByRole("button", { name: "Keep monitoring this bill", exact: true });
    await keep.waitFor();
    await keep.click();
    await page.locator(".bill-detail-warning").waitFor();
    assert.ok((await page.locator(".bill-monitoring-choice").innerText()).includes("Your monitoring choice was saved."));
    assert.equal(await keep.count(), 0);
    assert.equal(posts, 1);
    await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
    await page.screenshot({ path: path.join(outputDir, "bill-detail-saved-read-failed.png"), fullPage: true });
    await page.getByRole("button", { name: "Refresh bill", exact: true }).click();
    await page.locator(".bill-detail-warning").waitFor({ state: "detached" });
    assert.equal(posts, 1, "Retry after a confirmed write must only repeat the read.");

    await page.goto("/app/bills/" + id(8), { waitUntil: "domcontentloaded" });
    await keep.waitFor();
    postMode = "lost";
    await keep.click();
    await page.getByText("We could not confirm whether your choice was saved. Refresh this bill before trying again.",
      { exact: true }).waitFor();
    assert.equal(await keep.isDisabled(), true);
    assert.equal(posts, 2);
    await page.getByRole("button", { name: "Refresh bill", exact: true }).click();
    await page.waitForFunction(() => [...document.querySelectorAll("button")].some(button =>
      button.textContent.trim() === "Keep monitoring this bill" && !button.disabled));
    assert.equal(posts, 2, "An uncertain response must not automatically replay the POST.");
    console.log("Bills workspace: search, currency sorting, evidence filters, history, read recovery, confirmation, and responsive controls passed.");
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "bills-workspace-failure.png"), fullPage: true });
    throw error;
  } finally {
    await page.unroute(pattern, handler);
    await page.unroute("**/bff/bill-discovery/diagnostics", diagnostics);
    await page.evaluate(() => document.documentElement.style.fontSize = "");
  }
}
