import assert from "node:assert/strict";
import path from "node:path";

// Synthetic records exercise real authenticated components. No financial writes.
export async function verifyUiPolish(page, outputDir) {
  const ids = ["aa111111-1111-4111-8111-111111111111", "aa222222-2222-4222-8222-222222222222",
    "aa333333-3333-4333-8333-333333333333"];
  const longProvider = "Synthetic Mountain & Valley Community Utilities — Household Services";
  const bills = [
    { id: ids[0], providerName: longProvider, category: "Utility", isActive: true, source: "Manual",
      currentAmount: 89.25, previousAverage: 79.25, currencyCode: "USD", hasTransactionEvidence: true, hasMonthlyCadenceEvidence: true },
    { id: ids[1], providerName: "Spotify", category: "Other", isActive: true, source: "Manual",
      currentAmount: 13.80, previousAverage: 13.80, currencyCode: "USD", hasTransactionEvidence: true, hasMonthlyCadenceEvidence: true },
    { id: ids[2], providerName: "Synthetic Regional Internet", category: "Internet", isActive: true, source: "Manual",
      currentAmount: 45, previousAverage: 50, currencyCode: "EUR", hasTransactionEvidence: true, hasMonthlyCadenceEvidence: false }
  ];
  const alerts = [{ id: "bb111111-1111-4111-8111-111111111111", billStreamId: ids[0],
    alertType: "BillIncrease", severity: "Warning", title: "Synthetic utility charge increased",
    message: "Your latest charge changed from $79.25 to $89.25. Open the bill to review transaction evidence; the reason is not yet confirmed.",
    isRead: false, isDismissed: false, createdAtUtc: "2026-10-01T12:00:00Z", updatedAtUtc: "2026-10-01T12:00:00Z" },
    { id: "bb222222-2222-4222-8222-222222222222", alertType: "ConnectionIssue", severity: "Info",
      title: "Synthetic bank connection update", message: "Review your saved bank connections for the current status.",
      isRead: true, isDismissed: false, createdAtUtc: "2026-09-30T12:00:00Z", updatedAtUtc: "2026-09-30T12:00:00Z" }];
  let fail = false, empty = false;
  const read = async route => {
    assert.equal(route.request().method(), "GET");
    const pathname = new URL(route.request().url()).pathname;
    if (fail && pathname !== "/bff/bank-connections") return route.fulfill({ status: 503, body: "{}" });
    const json = pathname === "/bff/bill-streams" ? (empty ? [] : bills) : pathname === "/bff/alerts" ? (empty ? [] : alerts) : empty ? [] :
      [{ id: "cc111111-1111-4111-8111-111111111111", institutionName: "Synthetic Community Bank",
        status: 0, lastSuccessfulSyncAtUtc: "2026-10-01T12:00:00Z", createdAtUtc: "2026-09-01T12:00:00Z" }];
    return route.fulfill({ json });
  };
  const patterns = [/\/bff\/bill-streams(?:\?.*)?$/, /\/bff\/alerts(?:\?.*)?$/, "**/bff/bank-connections"];
  const unsafe = [];
  const record = request => {
    if (new URL(request.url()).pathname.startsWith("/bff/") && !["GET", "HEAD", "OPTIONS"].includes(request.method()))
      unsafe.push(request.method() + " " + new URL(request.url()).pathname);
  };
  const setCulture = async language => page.context().addCookies([{ name: ".AspNetCore.Culture",
    value: encodeURIComponent("c=en-US|uic=" + language), url: new URL(page.url()).origin, secure: true, sameSite: "Lax" }]);
  const cases = [
    [390, "dark", false, "en-US"], [1440, "dark", false, "en-US"],
    [320, "dark", false, "en-US"], [390, "light", false, "en-US"],
    [390, "dark", true, "en-US"], [390, "dark", false, "es"]
  ];
  const routes = [["overview", "/app", ".fw-pulse"], ["bills", "/app/bills", ".fw-bills-row"],
    ["activity", "/app/activity", ".alert-card"]];
  const waitInset = async () => page.waitForFunction(() => {
    const shell = document.querySelector(".app-shell");
    const bar = shell?.querySelector(".app-sidebar");
    const nav = shell?.querySelector(".mobile-bottom-nav");
    if (!shell || !bar || !nav) return false;
    const mobile = getComputedStyle(nav).display !== "none";
    const actual = parseFloat(shell.style.getPropertyValue("--fw-navigation-inset"));
    const rect = bar.getBoundingClientRect();
    const expected = mobile ? Math.ceil(rect.height + Math.max(0, parseFloat(getComputedStyle(bar).bottom) || 0) + 20) : 0;
    return Number.isFinite(actual) && Math.abs(actual - expected) <= 1;
  });
  const verifyControls = async selector => {
    const controls = await page.locator(selector).all();
    assert.ok(controls.length > 0, "Geometry checks must inspect actual controls.");
    for (const control of controls) {
      if (!(await control.isVisible())) continue;
      await control.evaluate(element => element.scrollIntoView({ block: "center", inline: "nearest", behavior: "instant" }));
      const r = await control.evaluate(element => {
        const rect = element.getBoundingClientRect();
        const nav = document.querySelector(".app-sidebar");
        const isMobile = getComputedStyle(document.querySelector(".mobile-bottom-nav")).display !== "none";
        const hit = document.elementFromPoint(rect.left + rect.width / 2, rect.top + rect.height / 2);
        return { height: rect.height, left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom,
          boundary: isMobile ? nav.getBoundingClientRect().top : innerHeight,
          hit: element === hit || element.contains(hit), viewport: innerWidth };
      });
      assert.ok(r.height >= 44 && r.left >= 0 && r.right <= r.viewport + 1,
        "Polished controls must fit with 44px touch targets: " + JSON.stringify(r));
      assert.ok(r.top >= 0 && r.bottom <= r.boundary + 1 && r.hit,
        "A person scrolling to a control must be able to reach it above the tab bar: " + JSON.stringify(r));
    }
  };
  const logoPattern = /^https:\/\/www\.google\.com\/s2\/favicons/;
  let holdLogo = true, releaseLogo;
  const blockLogo = async route => {
    if (holdLogo) await new Promise(resolve => { releaseLogo = resolve; });
    await route.abort("failed");
  };
  await page.route(logoPattern, blockLogo);
  page.on("request", record);
  for (const pattern of patterns) await page.route(pattern, read);
  try {
    for (const [width, theme, enlarged, language] of cases) {
      await page.setViewportSize({ width, height: 1000 });
      await page.evaluate(value => localStorage.setItem("billwatch-theme", value), theme);
      await setCulture(language);
      for (const [label, url, ready] of routes) {
        await page.goto(url, { waitUntil: "domcontentloaded" });
        await page.locator(ready).first().waitFor();
        await page.locator('link[href="/app-polish.css"]').waitFor({ state: "attached" });
        await page.waitForFunction(() => Boolean(document.querySelector('link[href="/app-polish.css"]').sheet));
        const normalFont = await page.locator(".mobile-bottom-nav").evaluate(element => parseFloat(getComputedStyle(element).fontSize));
        if (enlarged) await page.evaluate(() => document.documentElement.style.fontSize = "200%");
        await waitInset();
        if (holdLogo && label === "overview") {
          const deadline = Date.now() + 10000;
          while (!releaseLogo && Date.now() < deadline) await new Promise(done => setTimeout(done, 20));
          assert.ok(releaseLogo, "Expected a held decorative-logo request.");
          const spotify = page.locator(".fw-bill-row").filter({ hasText: "Spotify" });
          await spotify.locator(".fw-bill-logo").getByText("S", { exact: true }).waitFor();
          assert.equal(await spotify.locator("img.provider-logo-image").isVisible(), false,
            "The fallback must remain visible while an image is pending.");
          holdLogo = false;
          releaseLogo();
        }
        await page.locator("img.provider-logo-image").waitFor({ state: "detached" });

        if (width === 390 && theme === "dark" && !enlarged && language === "en-US") {
          const amounts = page.locator(".fw-pulse-amount, .fw-bill-value strong, .fw-bills-current strong, .fw-bills-total strong, .alert-title-line strong");
          const financialBefore = await amounts.allTextContents();
          await page.locator('link[href="/app-polish.css"]').evaluate(element => element.sheet.disabled = true);
          await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
          await page.screenshot({ path: path.join(outputDir, "polish-before-" + label + ".png"), fullPage: true });
          await page.locator('link[href="/app-polish.css"]').evaluate(element => element.sheet.disabled = false);
          await waitInset();
          assert.deepEqual(await amounts.allTextContents(), financialBefore,
            "The presentation layer must not change financial content.");
        }
        const pageGeometry = await page.evaluate(() => {
          const viewport = document.documentElement.clientWidth;
          const scrollWidth = document.documentElement.scrollWidth;
          const offenders = [...document.querySelectorAll(".app-shell, .app-shell *")]
            .filter(element => {
              const r = element.getBoundingClientRect();
              const style = getComputedStyle(element);
              return style.display !== "none" && style.position !== "fixed" &&
                r.width > 0 && r.right > viewport + 1;
            })
            .slice(0, 12)
            .map(element => ({
              tag: element.tagName.toLowerCase(),
              classes: [...element.classList].slice(0, 4),
              right: Math.round(element.getBoundingClientRect().right)
            }));
          return { viewport, scrollWidth, offenders };
        });
        assert.ok(pageGeometry.scrollWidth <= pageGeometry.viewport + 1,
          "Polish must not introduce horizontal page scrolling: " + label + " " +
          width + " " + language + " large=" + enlarged + " " + JSON.stringify(pageGeometry));

        if (width <= 900) {
          const links = await page.locator(".mobile-bottom-link").all();
          assert.equal(links.length, 5);
          const rects = [];
          for (const link of links) {
            const r = await link.evaluate(element => {
              const rect = element.getBoundingClientRect(), text = element.lastElementChild.getBoundingClientRect();
              return { left: rect.left, right: rect.right, top: rect.top, bottom: rect.bottom, height: rect.height,
                textLeft: text.left, textRight: text.right, textHeight: text.height,
                lineHeight: parseFloat(getComputedStyle(element.lastElementChild).lineHeight), width: innerWidth };
            });
            assert.ok(r.height >= 44 && r.left >= 0 && r.right <= r.width + 1);
            assert.ok(r.textLeft >= r.left - 1 && r.textRight <= r.right + 1, "Tab labels must fit their own targets.");
            assert.ok(r.textHeight <= r.lineHeight + 1, "Short tab names must reflow as whole labels, not break into fragments.");
            for (const other of rects) assert.ok(r.right <= other.left + 1 || r.left >= other.right - 1 ||
              r.bottom <= other.top + 1 || r.top >= other.bottom - 1, "Tab targets must not overlap.");
            rects.push(r);
          }
          const rows = new Map();
          for (const r of rects) {
            const key = Math.round(r.top);
            if (!rows.has(key)) rows.set(key, []);
            rows.get(key).push(r);
          }
          if (rows.size > 1) {
            const lastRow = rows.get([...rows.keys()].sort((a, b) => a - b).at(-1));
            if (lastRow.length === 1) assert.ok(lastRow[0].right - lastRow[0].left >=
              Math.max(...rects.map(r => r.right)) - Math.min(...rects.map(r => r.left)) - 2,
              "A lone final tab should fill the row rather than leave an empty grid.");
          }
          if (enlarged) assert.ok(await page.locator(".mobile-bottom-nav").evaluate(element =>
            parseFloat(getComputedStyle(element).fontSize)) >= normalFont * 1.9,
            "Layout must reflow, not shrink away the user's enlarged text.");
        }

        if (label === "overview") {
          const content = await page.locator(".fw-pulse").innerText();
          assert.ok(content.includes("$103.05") && content.includes("EUR 45.00"), "Separate-currency totals must survive presentation polish.");
          const spotify = page.locator(".fw-bill-row").filter({ hasText: "Spotify" });
          await spotify.locator(".fw-bill-logo").getByText("S", { exact: true }).waitFor();
          assert.equal(await spotify.locator("img.provider-logo-image").count(), 0, "Failed logos must use the initial, not a broken image.");
          await verifyControls(".fw-pulse-actions a");
          const contained = await page.locator(".fw-pulse").evaluate(hero => {
            const bounds = hero.getBoundingClientRect();
            return { scrollTop: hero.scrollTop, overflow: getComputedStyle(hero).overflow,
              children: [...hero.querySelectorAll(".fw-pulse-state, .fw-pulse-label, .fw-pulse-amount, .fw-pulse-stat, .fw-sync-line")]
                .map(element => { const r = element.getBoundingClientRect();
                  return { text: element.textContent.trim().slice(0, 45), inside: r.top >= bounds.top - 1 &&
                    r.bottom <= bounds.bottom + 1 && r.left >= bounds.left - 1 && r.right <= bounds.right + 1 }; }) };
          });
          assert.equal(contained.scrollTop, 0, "Scrolling to an action must not scroll the hero internally.");
          assert.ok(contained.children.every(child => child.inside), "Hero evidence must remain inside the visible card: " + JSON.stringify(contained));
        } else if (label === "bills") {
          const provider = page.locator(".fw-bills-provider strong").filter({ hasText: longProvider });
          assert.equal(await provider.innerText(), longProvider);
          assert.equal(await provider.evaluate(element => getComputedStyle(element).whiteSpace), "normal");
          assert.ok((await page.locator(".fw-bills-total").innerText()).includes("$103.05"));
          const spotify = page.locator(".fw-bills-row").filter({ hasText: "Spotify" });
          await spotify.locator(".bill-category-sprite").waitFor();
          assert.equal(await spotify.locator("img.provider-logo-image").count(), 0, "Bill rows must retain their category fallback after logo failure.");
          // Bills now has separate primary and additional filter groups. Verify
          // both groups reflow correctly rather than assuming one CSS match.
          const filterLayouts = await page.locator(".fw-bills-filters").evaluateAll(elements =>
            elements.map(element => ({
              wrap: getComputedStyle(element).flexWrap,
              fits: element.scrollWidth <= element.clientWidth + 1
            })));
          assert.ok(filterLayouts.length >= 2, "Both bill filter groups should be present.");
          for (const layout of filterLayouts) {
            assert.equal(layout.wrap, "wrap", "Bill filters must wrap instead of hiding behind a horizontal strip.");
            assert.equal(layout.fits, true, "Every bill filter group must fit without horizontal scrolling.");
          }
          await verifyControls(".fw-bills-filters button");
          await page.locator(".fw-bills-filters button").nth(1).click();
          await page.waitForFunction(() => document.querySelectorAll(".fw-bills-row").length === 1);
          assert.ok((await page.locator(".fw-bills-row").innerText()).includes(longProvider));
          await page.locator(".fw-bills-filters button").first().click();
          await page.waitForFunction(() => document.querySelectorAll(".fw-bills-row").length === 3);
        } else {
          assert.equal(await page.locator(".alert-card").count(), 2);
          await verifyControls(".activity-filters button");
          await page.locator(".activity-filters button").nth(1).click();
          await page.waitForFunction(() => document.querySelectorAll(".alert-card").length === 1);
          await page.locator(".activity-filters button").first().click();
          await page.waitForFunction(() => document.querySelectorAll(".alert-card").length === 2);
        }
        await page.locator("img.provider-logo-image").waitFor({ state: "detached" });
        await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo({ top: 0, behavior: 'instant' }); });
        await page.mouse.move(width - 2, 2);
        const suffix = width + "-" + theme + (enlarged ? "-200pct" : "") + "-" + language;
        await page.screenshot({ path: path.join(outputDir, "polish-" + label + "-" + suffix + ".png"), fullPage: true });

        if (label === "activity" && width <= 900) {
          await page.locator(".mobile-menu-trigger").click();
          await page.locator("#billwatch-mobile-menu").waitFor();
          const rect = await page.locator("#billwatch-mobile-menu").evaluate(element => {
            const r = element.getBoundingClientRect(), bar = document.querySelector(".app-sidebar").getBoundingClientRect();
            return { top: r.top, bottom: r.bottom, left: r.left, right: r.right, boundary: bar.top, width: innerWidth };
          });
          assert.ok(rect.top >= 0 && rect.bottom <= rect.boundary && rect.left >= 0 && rect.right <= rect.width + 1,
            "More menu must fit above the actual text-dependent tab bar.");
          await page.screenshot({ path: path.join(outputDir, "polish-menu-" + suffix + ".png") });
          await page.keyboard.press("Escape");
          await page.locator("#billwatch-mobile-menu").waitFor({ state: "detached" });
        }
      }
    }

    empty = true;
    await setCulture("en-US");
    await page.evaluate(() => localStorage.setItem("billwatch-theme", "dark"));
    const emptyReady = [".onboarding-card", ".bills-empty-state", ".alerts-empty"];
    for (let index = 0; index < routes.length; index++) {
      const [label, url] = routes[index];
      await page.goto(url, { waitUntil: "domcontentloaded" });
      await page.locator(emptyReady[index]).waitFor();
      await waitInset();
      if (label === "overview") await verifyControls(".onboarding-primary-action");
      if (label === "bills") await verifyControls(".bills-empty-actions a");
      if (label === "activity") await verifyControls(".activity-refresh-button");
      assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
      await page.evaluate(() => window.scrollTo({ top: 0, behavior: 'instant' }));
      await page.screenshot({ path: path.join(outputDir, "polish-empty-" + label + ".png"), fullPage: true });
    }
    empty = false;
    fail = true;
    await setCulture("en-US");
    for (const [label, url] of routes) {
      await page.goto(url, { waitUntil: "domcontentloaded" });
      await page.locator(".app-error-state").waitFor();
      assert.ok((await page.locator(".app-error-state").innerText()).includes("Try again"));
      await verifyControls(".app-error-state button");
      await page.screenshot({ path: path.join(outputDir, "polish-error-" + label + ".png"), fullPage: true });
    }
    assert.deepEqual(unsafe, [], "Visual polish and navigation must never mutate financial resources.");
    console.log("UI polish: identical financial content, responsive/Spanish/light/dark/200% layouts, measured navigation, reachable controls, filters, menu and failure states passed.");
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "ui-polish-failure.png"), fullPage: true });
    throw error;
  } finally {
    for (const pattern of patterns) await page.unroute(pattern, read);
    holdLogo = false;
    releaseLogo?.();
    await page.unroute(logoPattern, blockLogo);
    page.off("request", record);
    await setCulture("en-US");
    await page.evaluate(() => { document.documentElement.style.fontSize = ""; localStorage.setItem("billwatch-theme", "dark"); });
    await page.setViewportSize({ width: 1440, height: 1100 });
  }
}
