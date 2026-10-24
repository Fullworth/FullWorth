import assert from "node:assert/strict";
import path from "node:path";

// Click the real rendered links, rather than only checking their geometry.
// A root <base> sends fragment-only hrefs to / even when target IDs exist.
export async function verifyInPageNavigation(page, route, selector, expectedCount, outputDir, screenshotName) {
  await page.goto(route, { waitUntil: "domcontentloaded" });
  const links = page.locator(selector + " a");
  await links.first().waitFor({ state: "visible" });
  const targets = await links.evaluateAll(elements => elements.map(link => {
    const destination = new URL(link.getAttribute("href"), document.baseURI);
    return { pathname: destination.pathname, hash: destination.hash, origin: destination.origin };
  }));
  assert.equal(targets.length, expectedCount);
  const origin = new URL(page.url()).origin;
  for (let index = 0; index < targets.length; index++) {
    const target = targets[index];
    assert.equal(target.pathname, route, "Section links must stay on " + route);
    assert.equal(target.origin, origin);
    assert.ok(target.hash.length > 1);
    const id = decodeURIComponent(target.hash.slice(1));
    assert.equal(await page.locator('[id="' + id + '"]').count(), 1);
    await links.nth(index).click();
    await page.waitForURL(url => url.pathname === route && url.hash === target.hash);
    await page.waitForFunction(targetId => {
      const target = document.getElementById(targetId);
      if (!target) return false;
      const box = target.getBoundingClientRect();
      return box.top < innerHeight && box.bottom > 0;
    }, id);
    assert.equal(new URL(page.url()).pathname, route);
  }
  await page.screenshot({ path: path.join(outputDir, screenshotName + ".png"), fullPage: false });
}
