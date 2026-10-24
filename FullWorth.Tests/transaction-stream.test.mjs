import "./login-redirect.test.mjs";
import "./plaid-completion-recovery.test.mjs";
import test from "node:test";
import assert from "node:assert/strict";
import { getBankTransactionsStream } from "../FullWorth.Web/wwwroot/js/transaction-stream.js";

test("500 transactions cross the interop boundary as a stream, preserving Unicode and amounts", async () => {
    const rows = Array.from({ length: 500 }, (_, index) => ({
        id: index,
        merchantName: index === 499 ? "Black Hills Energy" : "Synthetic café utility",
        name: "Synthetic transaction",
        institutionName: "Test institution",
        accountName: "Test checking",
        amount: 104.99,
        postedDate: "2026-09-18",
        isPending: false
    }));
    assert.ok(Buffer.byteLength(JSON.stringify(rows)) > 32768);

    globalThis.fetch = async (url, options) => {
        assert.equal(url, "/bff/bank-transactions?take=500");
        assert.equal(options.credentials, "same-origin");
        assert.equal(options.cache, "no-store");
        assert.equal(options.headers.Authorization, undefined);
        return Response.json(rows);
    };
    const payload = await getBankTransactionsStream(500);
    assert.ok(payload instanceof Blob);
    assert.ok(payload.size > 32768);
    assert.deepEqual(JSON.parse(await payload.text()), rows);
});

test("empty history streams successfully", async () => {
    globalThis.fetch = async () => Response.json([]);
    assert.equal(await (await getBankTransactionsStream(500)).text(), "[]");
});

test("invalid, excessive and oversized responses fail without creating a stream", async () => {
    for (const response of [null, {}, Array(501).fill({}), [{ name: "x".repeat(4 * 1024 * 1024) }]]) {
        globalThis.fetch = async () => Response.json(response);
        await assert.rejects(getBankTransactionsStream(500), /transaction response/);
    }
});

test("expired authentication redirects to login without streaming", async () => {
    let destination;
    globalThis.window = { location: { assign: path => { destination = path; } } };
    globalThis.fetch = async () => new Response(null, { status: 401 });
    await assert.rejects(getBankTransactionsStream(500), /session expired/);
    assert.equal(destination, "/login");
});

test("failed BFF requests remain errors", async () => {
    globalThis.fetch = async () => new Response(null, { status: 503 });
    await assert.rejects(getBankTransactionsStream(500), /503/);
});

import { activateLocalTransactionFiltering, deactivateLocalTransactionFiltering }
    from "../FullWorth.Web/wwwroot/js/transaction-stream.js";

function localFixture(workerSupported = true) {
    class Element extends EventTarget {
        value = "";
        hidden = false;
        textContent = "";
        dataset = {};
    }
    const elements = Object.fromEntries(["search", "account", "count", "empty"].map(id => [id, new Element()]));
    elements.count.dataset = { singularLabel: "1 transaction", pluralTemplate: "{count} transactions" };
    const rows = ["one", "two"].map(id => {
        const row = new Element();
        row.dataset.fullworthTransactionId = id;
        return row;
    });
    const data = [
        { id: "one", bankAccountId: "account-a", merchantName: "Café Utility" },
        { id: "two", bankAccountId: "account-b", merchantName: "Telephone" }
    ];
    const workers = [];
    class TestWorker {
        listeners = {};
        deferred = false;
        requests = [];
        terminated = false;
        constructor() { workers.push(this); }
        addEventListener(name, listener) { this.listeners[name] = listener; }
        terminate() { this.terminated = true; }
        postMessage(message) {
            if (message.type === "initialize") {
                queueMicrotask(() => this.respond(message, []));
            } else if (this.deferred) {
                this.requests.push(message);
            } else {
                queueMicrotask(() => this.respond(message, ["one", "two"]));
            }
        }
        respond(message, ids) {
            this.listeners.message({ data: { requestId: message.requestId, ok: true, ids } });
        }
    }
    globalThis.window = { setTimeout, clearTimeout };
    globalThis.document = {
        getElementById: id => elements[id],
        querySelectorAll: () => rows
    };
    globalThis.FullWorthPerformance = { getExecutionMode: () => "local" };
    globalThis.Worker = workerSupported ? TestWorker : class {
        constructor() { throw new Error("Workers blocked"); }
    };
    globalThis.fetch = async () => Response.json(data);
    return { elements, rows, workers, data };
}

const flush = () => new Promise(resolve => setImmediate(resolve));
const activate = () => activateLocalTransactionFiltering("search", "account", "count", "empty");

test("local filters keep working without a Worker and combine account and Unicode search", async () => {
    const f = localFixture(false);
    try {
        await getBankTransactionsStream(500);
        assert.equal(await activate(), true);
        f.elements.search.value = " CAFÉ ";
        f.elements.account.value = "ACCOUNT-A";
        f.elements.account.dispatchEvent(new Event("change"));
        await flush();
        assert.deepEqual(f.rows.map(row => row.hidden), [false, true]);
        assert.equal(f.elements.count.textContent, "1 transaction");
        f.elements.account.value = "account-b";
        f.elements.account.dispatchEvent(new Event("change"));
        await flush();
        assert.deepEqual(f.rows.map(row => row.hidden), [true, true]);
        assert.equal(f.elements.empty.hidden, false);
        assert.equal(f.elements.count.textContent, "0 transactions");
    } finally { deactivateLocalTransactionFiltering(); }
});

test("older worker replies cannot replace newer account results", async () => {
    const f = localFixture();
    try {
        await getBankTransactionsStream(500);
        await activate();
        const worker = f.workers[0];
        worker.deferred = true;
        f.elements.account.value = "account-a";
        f.elements.account.dispatchEvent(new Event("change"));
        f.elements.account.value = "account-b";
        f.elements.account.dispatchEvent(new Event("change"));
        worker.respond(worker.requests[1], ["two"]);
        await flush();
        worker.respond(worker.requests[0], ["one"]);
        await flush();
        assert.deepEqual(f.rows.map(row => row.hidden), [true, false]);
    } finally { deactivateLocalTransactionFiltering(); }
});

test("worker crash falls back without leaving stale rows and disposal terminates private worker", async () => {
    const f = localFixture();
    await getBankTransactionsStream(500);
    await activate();
    const worker = f.workers[0];
    worker.deferred = true;
    f.elements.search.value = "Telephone";
    f.elements.account.dispatchEvent(new Event("change"));
    worker.listeners.error();
    await flush();
    assert.deepEqual(f.rows.map(row => row.hidden), [true, false]);
    assert.equal(worker.terminated, true);
    deactivateLocalTransactionFiltering();
    assert.equal(await activate(), false);
});

test("page disposal invalidates pending replies and resets the next account's filters", async () => {
    const f = localFixture();
    await getBankTransactionsStream(500);
    await activate();
    const worker = f.workers[0];
    worker.deferred = true;
    f.elements.search.value = "private previous search";
    f.elements.account.dispatchEvent(new Event("change"));
    deactivateLocalTransactionFiltering();
    worker.respond(worker.requests[0], ["one"]);
    await flush();
    assert.deepEqual(f.rows.map(row => row.hidden), [false, false]);
    await getBankTransactionsStream(500);
    await activate();
    assert.equal(f.elements.search.value, "");
    assert.equal(f.elements.account.value, "");
    deactivateLocalTransactionFiltering();
});

test("failed oversized refresh preserves the working transaction index", async () => {
    const f = localFixture(false);
    try {
        await getBankTransactionsStream(500);
        await activate();
        globalThis.fetch = async () => Response.json([{ name: "x".repeat(4 * 1024 * 1024) }]);
        await assert.rejects(getBankTransactionsStream(500), /oversized/);
        f.elements.search.value = "Telephone";
        f.elements.account.dispatchEvent(new Event("change"));
        await flush();
        assert.deepEqual(f.rows.map(row => row.hidden), [true, false]);
    } finally { deactivateLocalTransactionFiltering(); }
});

test("a load completing after page disposal cannot resurrect private transaction index", async () => {
    const f = localFixture();
    let resolve;
    globalThis.fetch = () => new Promise(done => { resolve = done; });
    const loading = getBankTransactionsStream(500);
    deactivateLocalTransactionFiltering();
    resolve(Response.json(f.data));
    await assert.rejects(loading, /superseded/);
    assert.equal(await activate(), false);
});
