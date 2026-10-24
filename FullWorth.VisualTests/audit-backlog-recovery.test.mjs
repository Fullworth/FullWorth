import assert from "node:assert/strict";
import path from "node:path";

// Real components, synthetic intercepted responses; never run against production.
export async function verifyAuditBacklogRecovery(page, outputDir) {
    assert.match(new URL(page.url()).hostname, /\.localhost$/);
    const adminReads = [];
    let grants = 0, grantOperation;
    const admin = route => {
        const method = route.request().method();
        if (method !== "GET") {
            assert.ok(route.request().headers()["x-csrf-token"]);
            const body = route.request().postDataJSON();
            assert.equal(body.currentPassword, "Synthetic!Proof123");
            assert.equal(method, "POST");
            assert.match(route.request().url(), /\/entitlements$/);
            assert.match(body.operationId, /^[a-f0-9-]{36}$/i);
            grants++;
            if (grants === 1) { grantOperation = body.operationId; return route.abort("failed"); }
            assert.equal(body.operationId, grantOperation, "retry must retain the queued grant identity");
            return route.fulfill({ status: 201, json: { entitlementId: grantOperation } });
        }
        const url = new URL(route.request().url()), skip = Number(url.searchParams.get("skip"));
        if (url.pathname.endsWith("/users") && url.searchParams.get("take") === "1")
            return route.fulfill({ json: [] }); // AppLayout's independent staff-access probe.
        assert.equal(url.searchParams.get("take"), "50");
        adminReads.push([url.pathname, skip]);
        const users = url.pathname.endsWith("/users");
        const rows = Array.from({ length: skip === 0 ? 50 : 1 }, (_, i) => {
            const n = skip + i + 1;
            return { id: "b8240000-0000-0000-0000-" + String(n).padStart(12, "0"),
                email: "synthetic-" + n + "@example.test", roles: [], programs: ["BetaTester"], isActive: true,
                displayPrefix: "SYN" + n, purpose: "Complimentary", tier: "Standard", status: "Active",
                action: "SyntheticAudit" + n, subjectType: "Synthetic",
                createdAtUtc: "2026-10-01T00:00:00Z", items: [] };
        });
        return route.fulfill({ json: users ? rows : { skip, take: 50, totalCount: 51, items: rows } });
    };
    await page.route("**/bff/admin/**", admin);
    try {
        await page.goto("/app/admin", { waitUntil: "domcontentloaded" });
        const groups = page.getByRole("group", { name: "Page controls", exact: true });
        await groups.first().waitFor();
        assert.equal(await groups.count(), 3);
        const firstUser = page.locator(".admin-user-row").first();
        await firstUser.getByRole("button", { name: "Grant 30 days", exact: true }).click();
        await firstUser.getByLabel("Current password", { exact: true }).fill("Synthetic!Proof123");
        await firstUser.getByRole("button", { name: "Confirm", exact: true }).click();
        await firstUser.getByText("The grant could not be confirmed. Retry this confirmation with fresh credentials to recover the same grant, or cancel and review the account.", { exact: true }).waitFor();
        assert.equal(await firstUser.getByLabel("Current password", { exact: true }).inputValue(), "");
        await firstUser.getByLabel("Current password", { exact: true }).fill("Synthetic!Proof123");
        await Promise.all([page.waitForEvent("domcontentloaded"),
            firstUser.getByRole("button", { name: "Confirm", exact: true }).click()]);
        await groups.first().waitFor();
        assert.equal(grants, 2);
        for (let i = 0; i < 3; i++) {
            await groups.nth(i).getByRole("button", { name: "Next page", exact: true }).click();
            await groups.nth(i).getByText("Records 51–51", { exact: true }).waitFor();
            assert.equal(await groups.nth(i).getByRole("button", { name: "Next page", exact: true }).isDisabled(), true);
            await groups.nth(i).getByRole("button", { name: "Previous page", exact: true }).click();
            await groups.nth(i).getByText("Records 1–50", { exact: true }).waitFor();
        }
        assert.equal(adminReads.filter(([, skip]) => skip === 50).length, 3);
        for (const width of [320, 1440]) {
            await page.setViewportSize({ width, height: 1000 });
            await groups.first().evaluate(root => root.scrollIntoView({ block: "center" }));
            const geometry = await groups.first().evaluate(root => {
                const bounds = root.getBoundingClientRect();
                const next = root.querySelector("button:last-of-type");
                const target = next.getBoundingClientRect();
                const hit = document.elementFromPoint(target.x + target.width / 2, target.y + target.height / 2);
                return { left: bounds.left, right: bounds.right, viewport: innerWidth,
                    reachable: hit === next || next.contains(hit),
                    heights: [...root.querySelectorAll("button")].map(button => button.getBoundingClientRect().height) };
            });
            assert.ok(geometry.left >= 0 && geometry.right <= geometry.viewport + 1);
            assert.ok(geometry.heights.every(height => height >= 44), "pager controls retain usable target height");
            assert.equal(geometry.reachable, true, "enabled pager must not be covered by fixed navigation");
            await groups.first().screenshot({ path: path.join(outputDir, "audit-admin-pager-" + width + ".png") });
        }
        await page.setViewportSize({ width: 1440, height: 1000 });
        console.log("Audit admin recovery passed: stable grant retries with cleared credentials and all three bounded admin pagers.");
    } catch (error) {
        await page.screenshot({ path: path.join(outputDir, "audit-backlog-recovery-failure.png"), fullPage: true });
        throw error;
    } finally {
        await page.unroute("**/bff/admin/**", admin);
        await page.goto("/app", { waitUntil: "domcontentloaded" });
    }
}
