import { chromium } from "playwright";
import { mkdir } from "node:fs/promises";
import path from "node:path";

const baseUrl = process.env.FULLWORTH_VISUAL_BASE_URL ?? "https://web.localhost";
const runId = process.env.GITHUB_RUN_ID ?? String(Date.now());
const outputDir = path.resolve("FullWorth.VisualTests", "artifacts");
const email = `ci-visual-${runId}@billwatch.local`;
const password = "BillWatch!Visual123";

await mkdir(outputDir, { recursive: true });

const browser = await chromium.launch({ headless: true });

try {
  const context = await browser.newContext({
    baseURL: baseUrl,
    ignoreHTTPSErrors: true,
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

  await page.goto("/app", { waitUntil: "domcontentloaded" });
  await settle(".mobile-bottom-nav");
  await page.locator(".mobile-menu-trigger").click();
  await page.locator("#billwatch-mobile-menu").waitFor({ state: "visible", timeout: 10000 });
  await page.waitForTimeout(250);
  await page.screenshot({
    path: path.join(outputDir, "mobile-menu-dark.png"),
    fullPage: true
  });

  await context.close();
} finally {
  await browser.close();
}
