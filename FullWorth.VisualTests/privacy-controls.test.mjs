import { verifyAccountPageRecovery } from "./account-page-recovery.test.mjs";
import assert from "node:assert/strict";
import path from "node:path";

// Only disposable CI accounts and synthetic export/delete responses are used.
export async function verifyPrivacyControls(page, outputDir) {
  const proof = "Synthetic!Proof123";
  let exportMode = "wrong-proof", deleteMode = "uncertain";
  let exports = 0, deletes = 0, downloads = 0;
  let releaseExport;
  const recordDownload = () => { downloads++; };
  const exportRoute = async route => {
    assert.equal(route.request().method(), "POST");
    assert.ok(route.request().headers()["x-csrf-token"]);
    exports++;
    if (exportMode === "hold") await new Promise(resolve => { releaseExport = resolve; });
    if (exportMode === "html") return route.fulfill({ status: 200, contentType: "text/html", body: "<html>not an export</html>" });
    if (exportMode === "ok") return route.fulfill({ json: { synthetic: "CI export—not real financial data" } });
    try {
      return await route.fulfill({ status: 401, contentType: "application/problem+json",
        body: JSON.stringify({ title: "Current password is incorrect." }) });
    } catch (error) {
      if (exportMode !== "hold") throw error; // Navigation/abort is expected only in the disposal case.
    }
  };
  const deleteRoute = async route => {
    assert.equal(route.request().method(), "DELETE");
    assert.ok(route.request().headers()["x-csrf-token"]);
    deletes++;
    if (deleteMode === "uncertain") return route.abort("failed");
    return route.fulfill({ status: 409, contentType: "application/json",
      body: JSON.stringify({ message: "Staff accounts cannot be self-deleted." }) });
  };
  await page.route("**/bff/account/export", exportRoute);
  await page.route(/\/bff\/account$/, deleteRoute);
  page.on("download", recordDownload);
  const open = async () => {
    await page.goto("/app/account/privacy", { waitUntil: "domcontentloaded" });
    await page.getByRole("button", { name: "Download data", exact: true }).waitFor();
    await page.waitForTimeout(600);
  };
  const exportProof = async () => {
    await page.getByRole("button", { name: "Download data", exact: true }).click();
    await page.locator(".export-confirmation input[type=password]").fill(proof);
    await page.locator(".export-confirmation input[inputmode=numeric]").fill("123456");
  };
  const deleteProof = async () => {
    await page.getByRole("button", { name: "Delete account permanently", exact: true }).click();
    await page.locator(".delete-confirmation input[type=password]").fill(proof);
    await page.locator(".delete-confirmation input[inputmode=numeric]").fill("123456");
    await page.locator(".delete-confirmation input[autocomplete=off]").fill("DELETE");
  };
  const emptyProof = async selector => {
    for (const input of await page.locator(selector + " input").all())
      assert.equal(await input.inputValue(), "", "Proof must be cleared after the request.");
  };
  const waitForHeldExport = async () => {
    const deadline = Date.now() + 10000;
    while (!releaseExport && Date.now() < deadline) await new Promise(done => setTimeout(done, 20));
    assert.ok(releaseExport, "Export did not reach the held response within 10 seconds.");
  };
  try {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.evaluate(() => localStorage.setItem("billwatch-theme", "dark"));
    await open();
    await exportProof();
    exportMode = "hold";
    await page.getByRole("button", { name: "Download securely", exact: true }).click();
    await page.waitForFunction(() => document.querySelector(".export-confirmation input[type=password]")?.disabled);
    assert.equal(await page.getByRole("button", { name: "Delete account permanently", exact: true }).isDisabled(), true);
    assert.equal(await page.locator(".export-confirmation").getByRole("button", { name: "Cancel", exact: true }).isDisabled(), true);
    await waitForHeldExport();
    exportMode = "wrong-proof";
    releaseExport();
    await page.locator(".export-confirmation [role=alert]").waitFor();
    assert.equal((await page.locator(".export-confirmation .privacy-message").innerText()).trim(), "Your current password is incorrect.");
    await emptyProof(".export-confirmation");
    assert.equal(exports, 1);
    assert.equal(await page.getByRole("button", { name: "Download securely", exact: true }).isDisabled(), true);

    await page.getByRole("button", { name: "Delete account permanently", exact: true }).click();
    await page.locator(".delete-confirmation").waitFor();
    await page.locator(".export-confirmation").waitFor({ state: "detached" });
    assert.equal(await page.locator(".export-confirmation").count(), 0);
    await page.locator(".delete-confirmation input[type=password]").fill(proof);
    await page.locator(".delete-confirmation input[inputmode=numeric]").fill("123456");
    await page.locator(".delete-confirmation input[autocomplete=off]").fill("DELETE ");
    assert.equal(await page.getByRole("button", { name: "Delete permanently", exact: true }).isDisabled(), true);
    assert.equal(deletes, 0);
    await page.locator(".delete-confirmation input[autocomplete=off]").fill("DELETE");
    await page.getByRole("button", { name: "Delete permanently", exact: true }).click();
    await page.locator(".delete-confirmation [role=alert]").waitFor();
    assert.ok((await page.locator(".delete-confirmation .privacy-message").innerText()).includes("may or may not have been deleted"));
    await emptyProof(".delete-confirmation");
    assert.equal(new URL(page.url()).pathname, "/app/account/privacy");
    assert.equal(deletes, 1, "Lost responses must not cause automatic DELETE retries.");
    await page.screenshot({ path: path.join(outputDir, "privacy-uncertain-deletion-mobile.png"), fullPage: true });

    deleteMode = "staff";
    await page.locator(".delete-confirmation input[type=password]").fill(proof);
    await page.locator(".delete-confirmation input[autocomplete=off]").fill("DELETE");
    await page.getByRole("button", { name: "Delete permanently", exact: true }).click();
    await page.getByText("Staff roles must be removed through the authorized admin workflow before this account can be deleted.",
      { exact: true }).waitFor();
    await emptyProof(".delete-confirmation");
    assert.equal(deletes, 2);

    await page.getByRole("button", { name: "Download data", exact: true }).click();
    await page.locator(".export-confirmation").waitFor();
    await page.locator(".delete-confirmation").waitFor({ state: "detached" });
    assert.equal(await page.locator(".delete-confirmation").count(), 0);
    await page.locator(".export-confirmation input[type=password]").fill(proof);
    exportMode = "html";
    await page.getByRole("button", { name: "Download securely", exact: true }).click();
    await page.getByText("FullWorth could not prepare your data export.", { exact: true }).waitFor();
    await emptyProof(".export-confirmation");
    assert.equal(downloads, 0);
    exportMode = "ok";
    await page.locator(".export-confirmation input[type=password]").fill(proof);
    const downloaded = page.waitForEvent("download");
    await page.getByRole("button", { name: "Download securely", exact: true }).click();
    const download = await downloaded;
    assert.equal(download.suggestedFilename(), "fullworth-data-export.json");
    await page.getByText("Download requested. Check Files or Downloads to confirm the JSON copy was saved. Keep it private.",
      { exact: true }).waitFor();
    assert.equal(downloads, 1);
    assert.equal(await page.locator(".export-confirmation").count(), 0);

    for (const [width, theme, enlarged] of [
      [1440, "dark", false], [320, "dark", false], [390, "light", false], [390, "dark", true]
    ]) {
      await page.setViewportSize({ width, height: 1100 });
      await page.evaluate(value => localStorage.setItem("billwatch-theme", value), theme);
      await open();
      await page.getByRole("button", { name: "Download data", exact: true }).click();
      if (enlarged) await page.evaluate(() => document.documentElement.style.fontSize = "200%");
      for (const selector of [".export-confirmation", ".delete-confirmation"]) {
        if (selector === ".delete-confirmation") {
          await page.getByRole("button", { name: "Delete account permanently", exact: true }).click();
        }
        await page.locator(selector).waitFor();
        const controls = await page.locator(selector + " input, " + selector + " button").all();
        assert.ok(controls.length >= 4, "The real confirmation form must render before checking its controls.");
        for (const control of controls) {
          const rect = await control.evaluate(element => {
            const box = element.getBoundingClientRect();
            return { height: box.height, left: box.left, right: box.right, width: innerWidth };
          });
          assert.ok(rect.height >= 44 && rect.left >= 0 && rect.right <= rect.width + 1,
            "Privacy controls require reachable touch targets without horizontal clipping.");
        }
        assert.equal(await page.evaluate(() => document.documentElement.scrollWidth > innerWidth + 1), false);
        await page.evaluate(() => window.scrollTo(0, 0));
        await page.screenshot({ path: path.join(outputDir, "privacy-" +
          (selector === ".export-confirmation" ? "export" : "delete") + "-" + width + "-" + theme +
          (enlarged ? "-large-text" : "") + ".png"), fullPage: true });
      }
    }

    await page.setViewportSize({ width: 390, height: 844 });
    await open();
    await exportProof();
    exportMode = "hold";
    releaseExport = null;
    await page.getByRole("button", { name: "Download securely", exact: true }).click();
    await waitForHeldExport();
    const before = downloads;
    await page.getByRole("link", { name: "Account", exact: true }).first().click();
    await page.waitForURL(url => url.pathname === "/app/account");
    releaseExport();
    await page.waitForTimeout(500);
    assert.equal(downloads, before, "Leaving privacy must not trigger a late export download.");
    assert.equal(deletes, 2);
    console.log("Privacy controls: exclusive operations, proof clearing, safe failure copy, real synthetic download, disposal and responsive forms passed.");
  } catch (error) {
    await page.screenshot({ path: path.join(outputDir, "privacy-controls-failure.png"), fullPage: true });
    throw error;
  } finally {
    releaseExport?.();
    await page.unroute("**/bff/account/export", exportRoute);
    await page.unroute(/\/bff\/account$/, deleteRoute);
    page.off("download", recordDownload);
    await page.evaluate(() => document.documentElement.style.fontSize = "");
  }
  await verifyAccountPageRecovery(page, outputDir);
}
