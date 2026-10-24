import { verifyAuditBacklogRecovery } from "./audit-backlog-recovery.test.mjs";
import assert from "node:assert/strict";
import path from "node:path";

// Real authenticated components; all preference responses here are synthetic CI fixtures.
export async function verifyExperienceSetup(page, outputDir) {
  const defaults = () => ({ timestampDisplayMode: "Local12Hour", experienceSetupComplete: true,
    preferredUiLanguage: "en-US", themePreference: "Dark", textSizePreference: "Standard",
    highContrastEnabled: false, reduceMotionEnabled: false, experienceFocus: ["BillChanges", "AccountOverview"] });
  let stored = defaults(), getFails = true, saveMode = "ok", puts = 0, release;
  const unsafe = [];
  const record = request => {
    const url = new URL(request.url());
    if (url.pathname.startsWith("/bff/") && !["GET", "HEAD", "OPTIONS"].includes(request.method()))
      unsafe.push(request.method() + " " + url.pathname);
  };
  const readRoute = async route => {
    assert.equal(route.request().method(), "GET");
    if (getFails) return route.fulfill({ status: 503, body: "{}" });
    return route.fulfill({ json: stored });
  };
  const saveRoute = async route => {
    assert.equal(route.request().method(), "PUT");
    assert.ok(route.request().headers()["x-csrf-token"]);
    puts++;
    const body = route.request().postDataJSON();
    if (saveMode === "hold-lost") {
      stored = { ...stored, ...body, experienceSetupComplete: true };
      await new Promise(resolve => { release = resolve; });
      return route.abort("failed");
    }
    if (saveMode === "mismatch") return route.fulfill({ json: stored });
    stored = { ...stored, ...body, experienceSetupComplete: true };
    return route.fulfill({ json: stored });
  };
  const culture = async language => page.context().addCookies([{
    name: ".AspNetCore.Culture", value: encodeURIComponent("c=en-US|uic=" + language),
    url: new URL(page.url()).origin, secure: true, sameSite: "Lax"
  }]);
  const open = async () => {
    await page.goto("/app/setup?returnTo=settings", { waitUntil: "domcontentloaded" });
    await page.locator(".experience-setup-page").waitFor();
  };
  const step = async (label, heading) => {
    await page.locator(".setup-progress").getByRole("button", { name: label, exact: true }).click();
    await page.getByRole("heading", { name: heading, exact: true }).waitFor();
    await page.waitForFunction(() => document.activeElement?.id === "setup-step-heading");
  };
  const waitHeld = async () => {
    const deadline = Date.now() + 10000;
    while (!release && Date.now() < deadline) await new Promise(done => setTimeout(done, 20));
    assert.ok(release, "Save never reached the held response.");
  };
  await page.route("**/bff/account/preferences", readRoute);
  await page.route("**/bff/account/preferences/experience", saveRoute);
  page.on("request", record);
  try {
    await page.setViewportSize({ width: 390, height: 844 });
    await culture("en-US");
    await open();
    await page.getByRole("button", { name: "Retry loading setup", exact: true }).waitFor();
    assert.equal(await page.locator(".setup-fields").count(), 0,
      "Read failure must not expose editable defaults or a save action.");
    assert.equal(puts, 0);
    getFails = false;
    await page.getByRole("button", { name: "Retry loading setup", exact: true }).click();
    await page.locator(".setup-fields").waitFor();
    assert.equal(await page.getByLabel("Theme", { exact: true }).inputValue(), "Dark");

    await page.getByLabel("Theme", { exact: true }).selectOption("Light");
    await step("Readability", "Make FullWorth comfortable to read.");
    await page.getByLabel("Text size", { exact: true }).selectOption("Large");
    await page.getByLabel("Use stronger text and border contrast", { exact: true }).check();
    await step("Your priorities", "Tell FullWorth what matters most.");
    await page.getByRole("checkbox", { name: /Statement explanations/ }).check();
    await step("Review & save", "Review before you save.");
    assert.ok((await page.locator(".setup-review").innerText()).includes("Light"));
    assert.ok((await page.locator(".setup-review").innerText()).includes("Statement explanations"));
    assert.ok((await page.locator(".setup-review").innerText()).includes("They do not connect a bank"));
    assert.equal(puts, 0, "Stepping through and reviewing preferences must never save them.");

    saveMode = "hold-lost";
    await page.getByRole("button", { name: "Save setup", exact: true }).click();
    await waitHeld();
    await page.locator(".setup-fields[disabled]").waitFor();
    assert.equal(await page.locator(".setup-fields").evaluate(element => element.disabled), true);
    for (const button of await page.locator(".setup-fields button").all())
      assert.equal(await button.isDisabled(), true, "Busy and uncertain drafts must disable every editable action.");
    for (const button of await page.locator(".setup-progress button").all())
      assert.equal(await button.isDisabled(), true);
    release();
    await page.getByRole("button", { name: "Check saved setup", exact: true }).waitFor();
    assert.equal(puts, 1);
    assert.equal(await page.locator(".settings-toast[role=alert]").evaluate(element =>
      getComputedStyle(element).position), "static", "Recovery feedback must stay beside the draft, not cover the mobile header.");
    await page.locator(".setup-fields[disabled]").waitFor();
    assert.equal(await page.locator(".setup-fields").evaluate(element => element.disabled), true);
    for (const button of await page.locator(".setup-fields button").all())
      assert.equal(await button.isDisabled(), true, "Busy and uncertain drafts must disable every editable action.");
    getFails = true;
    await page.getByRole("button", { name: "Check saved setup", exact: true }).click();
    await page.getByText("Saved setup could not be checked. Your edits are kept. Retry the read when the connection is available.",
      { exact: true }).waitFor();
    assert.ok((await page.locator(".setup-review").innerText()).includes("Light"));
    assert.equal(puts, 1);
    await page.evaluate(() => window.scrollTo(0, 0));
    await page.screenshot({ path: path.join(outputDir, "setup-uncertain-save.png"), fullPage: true });
    getFails = false;
    await page.getByRole("button", { name: "Check saved setup", exact: true }).click();
    await page.waitForURL(url => url.pathname === "/app/account/settings");
    assert.equal(puts, 1, "A confirmed recovery read must not repeat PUT.");

    stored = defaults();
    saveMode = "mismatch";
    await open();
    await page.getByLabel("Theme", { exact: true }).selectOption("Light");
    await step("Review & save", "Review before you save.");
    await page.getByRole("button", { name: "Save setup", exact: true }).click();
    await page.getByRole("button", { name: "Check saved setup", exact: true }).waitFor();
    await page.getByRole("button", { name: "Check saved setup", exact: true }).click();
    await page.getByText("Saved preferences currently differ from your edits. Your edits are kept; review them before saving again. No save was retried.",
      { exact: true }).waitFor();
    await step("Language & theme", "Start with the way you prefer to use FullWorth.");
    assert.equal(await page.getByLabel("Theme", { exact: true }).inputValue(), "Light");
    assert.equal(puts, 2);
    await step("Review & save", "Review before you save.");
    saveMode = "ok";
    await page.getByRole("button", { name: "Save setup", exact: true }).click();
    await page.waitForURL(url => url.pathname === "/app/account/settings");
    assert.equal(puts, 3);

    stored = defaults();
    await open();
    await step("Review & save", "Review before you save.");
    await page.evaluate(() => Object.defineProperty(window, "localStorage", {
      configurable: true, get() { throw new DOMException("Synthetic blocked storage", "SecurityError"); }
    }));
    await page.getByRole("button", { name: "Save setup", exact: true }).click();
    await page.getByRole("button", { name: "Continue to FullWorth", exact: true }).waitFor();
    await page.getByText("Your setup was saved. Some browser preferences could not be applied or remembered. Continue to reload FullWorth.",
      { exact: true }).waitFor();
    assert.equal(puts, 4);
    assert.equal(await page.getByRole("button", { name: "Check saved setup", exact: true }).count(), 0);
    await page.screenshot({ path: path.join(outputDir, "setup-saved-storage-blocked.png"), fullPage: true });
    await page.getByRole("button", { name: "Continue to FullWorth", exact: true }).click();
    await page.waitForURL(url => url.pathname === "/app/account/settings");
    assert.equal(puts, 4);

    for (const [width, theme, enlarged, language] of [
      [1440, "dark", false, "en-US"], [320, "dark", false, "en-US"],
      [390, "light", false, "en-US"], [390, "dark", true, "en-US"], [390, "dark", false, "es"]
    ]) {
      stored = { ...defaults(), preferredUiLanguage: language };
      await page.setViewportSize({ width, height: 1100 });
      await page.evaluate(value => {
        localStorage.setItem("billwatch-theme", value);
        localStorage.setItem("fullworth.experience-theme-initialized", "1");
      }, theme);
      await culture(language);
      await open();
      const spanish = language === "es";
      await page.locator(".setup-progress").waitFor();
      if (enlarged) await page.evaluate(() => document.documentElement.style.fontSize = "200%");
      const labels = spanish ? ["Legibilidad", "Revisar y guardar"] : ["Readability", "Review & save"];
      const headings = spanish ? ["Haz que FullWorth sea cómodo de leer.", "Revisa antes de guardar."] :
        ["Make FullWorth comfortable to read.", "Review before you save."];
      for (let index = 0; index < 2; index++) {
        await step(labels[index], headings[index]);
        const controls = await page.locator(".setup-progress button, .setup-fields button, .setup-fields select, .setup-fields label.remember-option").all();
        assert.ok(controls.length >= 6, "Check actual rendered setup controls.");
        for (const control of controls) {
          const geometry = await control.evaluate(element => {
            const rect = element.getBoundingClientRect();
            return { height: rect.height, left: rect.left, right: rect.right, viewport: innerWidth };
          });
          assert.ok(geometry.height >= 44 && geometry.left >= 0 && geometry.right <= geometry.viewport + 1,
            "Setup controls must fit with reachable 44px targets.");
        }
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
        if (width <= 390) {
          assert.equal(await page.locator(".setup-progress ol").evaluate(element =>
            getComputedStyle(element).gridTemplateColumns.trim().split(/\s+/).length), 1,
            "Narrow or enlarged setup navigation must stack rather than split step names into tiny tiles.");
        }
        for (const action of await page.locator(".setup-fields .settings-actions button").all()) {
          await action.evaluate(element => element.scrollIntoView({ block: "center", behavior: "instant" }));
          const bounds = await action.evaluate(element => {
            const rect = element.getBoundingClientRect();
            const nav = document.querySelector(".mobile-bottom-nav");
            const navRect = nav?.getBoundingClientRect();
            return { top: rect.top, bottom: rect.bottom,
              navTop: nav && getComputedStyle(nav).display !== "none" ? navRect.top : innerHeight };
          });
          assert.ok(bounds.top >= 0 && bounds.bottom <= bounds.navTop,
            "Setup actions must remain reachable above the fixed mobile navigation.");
        }

        await page.evaluate(() => { document.activeElement?.blur(); window.scrollTo(0, 0); });
        await page.screenshot({ path: path.join(outputDir, "setup-" + (index ? "review" : "readability") +
          "-" + width + "-" + theme + (enlarged ? "-large-text" : "") + "-" + language + ".png"), fullPage: true });
      }
    }
    assert.equal(puts, 4, "Responsive and localized reviews must not save.");
    assert.ok(unsafe.every(value => value === "PUT /bff/account/preferences/experience"),
      "Personal setup must not mutate bank, transaction or other financial resources.");
    console.log("Experience setup: guarded loading, four-step review, exact saves, read-only recovery, preserved edits, storage denial and responsive/localized controls passed.");
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "experience-setup-failure.png"), fullPage: true });
    throw error;
  } finally {
    release?.();
    await page.unroute("**/bff/account/preferences", readRoute);
    await page.unroute("**/bff/account/preferences/experience", saveRoute);
    page.off("request", record);
    await culture("en-US");
    await page.evaluate(() => document.documentElement.style.fontSize = "");
  }
  await verifyAuditBacklogRecovery(page, outputDir);
}
