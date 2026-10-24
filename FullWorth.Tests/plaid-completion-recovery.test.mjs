import test from "node:test";
import assert from "node:assert/strict";
import { pollPlaidCompletion, isActiveLinkResult } from "../FullWorth.Web/wwwroot/js/plaid-completion.js";

test("normal multi-minute authorization fits the unchanged provider budget", async () => {
    let time = 0, permits = 1;
    const result = await pollPlaidCompletion({
        now: () => time, sleep: async ms => { time += ms; }, isCurrent: () => true,
        readStatus: async () => {
            assert.ok(++permits <= 20, "creation and polls must fit the production budget");
            return { status: time < 180000 ? "Pending" : "Completed" };
        }
    });
    assert.equal(result.status, "Completed");
    assert.ok(time >= 180000);
});
test("ten-minute pending authorization is bounded to 18 reads", async () => {
    let time = 0, reads = 0;
    const result = await pollPlaidCompletion({
        now: () => time, sleep: async ms => { time += ms; }, isCurrent: () => true,
        readStatus: async () => { reads++; return { status: "Pending" }; }
    });
    assert.equal(result.status, "TimedOut");
    assert.equal(reads, 18);
});
test("429 honors Retry-After without another request or false failure", async () => {
    let time = 0, reads = 0;
    const result = await pollPlaidCompletion({
        now: () => time, sleep: async ms => { time += ms; }, isCurrent: () => true,
        readStatus: async () => {
            reads++;
            if (reads === 1) throw Object.assign(new Error("throttled"), { status: 429, retryAfterMs: 120000 });
            assert.ok(time >= 122000);
            return { status: "Completed" };
        }
    });
    assert.equal(result.status, "Completed");
    assert.equal(reads, 2);
});
test("navigation during delay or a held response suppresses old results", async () => {
    for (const duringRead of [false, true]) {
        let time = 0, current = true, reads = 0;
        const result = await pollPlaidCompletion({
            now: () => time, isCurrent: () => current,
            sleep: async ms => { time += ms; if (!duringRead) current = false; },
            readStatus: async () => { reads++; current = false; return { status: "Completed" }; }
        });
        assert.equal(result, null);
        assert.equal(reads, duringRead ? 1 : 0);
    }
});
test("terminal provider states are preserved, real errors remain errors", async () => {
    for (const status of ["Exited", "Expired", "Failed"]) {
        let time = 0;
        const result = await pollPlaidCompletion({ now: () => time, sleep: async ms => { time += ms; },
            isCurrent: () => true, readStatus: async () => ({ status }) });
        assert.equal(result.status, status);
    }
    await assert.rejects(pollPlaidCompletion({ isCurrent: () => true, sleep: async () => {},
        readStatus: async () => { throw new Error("network"); } }), /network/);
});
test("abort cancels the real pending timer", async () => {
    const flow = new AbortController();
    const waiting = pollPlaidCompletion({ signal: flow.signal, isCurrent: () => true,
        readStatus: async () => assert.fail("aborted flow must not read") });
    flow.abort();
    await assert.rejects(waiting, error => error.name === "AbortError");
});

test("closing a popup permits a final authoritative completion check", async () => {
    let time = 0, reads = 0;
    const result = await pollPlaidCompletion({ now: () => time, sleep: async ms => { time += ms; },
        isCurrent: () => true, isPopupClosed: () => true,
        readStatus: async () => ({ status: ++reads === 2 ? "Completed" : "Pending" }) });
    assert.equal(result.status, "Completed");
    reads = 0;
    const closed = await pollPlaidCompletion({ now: () => time, sleep: async ms => { time += ms; },
        isCurrent: () => true, isPopupClosed: () => true,
        readStatus: async () => { reads++; return { status: "Pending" }; } });
    assert.equal(closed.status, "PopupClosed");
    assert.equal(reads, 2);
});

test("unknown or malformed Link responses never count as terminal authorization", async () => {
    const invalid = [
        null, undefined, {}, { status: null }, { status: 200 },
        { status: "" }, { status: "Unknown" }, { status: "Success" },
        { status: "Connected" }, { status: {} },
        { status: " Completed " }, { status: "pending?" }, { status: [] }
    ];
    for (const response of invalid) {
        let time = 0, reads = 0;
        await assert.rejects(pollPlaidCompletion({
            now: () => time, sleep: async ms => { time += ms; },
            isCurrent: () => true,
            readStatus: async () => { reads++; return response; }
        }), /invalid Link status/);
        assert.equal(reads, 1, "malformed data must not trigger more provider reads");
    }
});

test("completed polling requires active bank connection evidence before success", () => {
    const active = { status: "Completed", connection: {
        id: "d933d7d3-934e-4abe-9436-1f97f1729ec7",
        institutionName: "Synthetic Bank", status: "Active"
    }};
    assert.equal(isActiveLinkResult(active), true);
    for (const rejected of [
        null, { status: "Completed" }, { status: "Completed", connection: null },
        { status: "Completed", connection: { id: "", status: "Active" } },
        { status: "Completed", connection: { id: " ", status: "Active" } },
        { status: "Completed", connection: { id: "not-a-bank-id", status: "Active" } },
        { status: "Completed", connection: { id: active.connection.id, status: "Disconnected" } },
        { status: "Completed", connection: { id: active.connection.id, status: "RequiresAttention" } },
        { status: "Failed", connection: active.connection }
    ]) {
        assert.equal(isActiveLinkResult(rejected), false);
    }
});
