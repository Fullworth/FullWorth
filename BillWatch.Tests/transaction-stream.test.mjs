import test from "node:test";
import assert from "node:assert/strict";
import { getBankTransactionsStream } from "../BillWatch.Web/wwwroot/js/transaction-stream.js";

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
    let payload;
    globalThis.fetch = async (url, options) => {
        assert.equal(url, "/bff/bank-transactions?take=500");
        assert.equal(options.credentials, "same-origin");
        assert.equal(options.cache, "no-store");
        assert.equal(options.headers.Authorization, undefined);
        return Response.json(rows);
    };
    globalThis.DotNet = {
        createJSStreamReference(blob) {
            payload = blob;
            return { __jsStreamReferenceLength: blob.size, __jsObjectId: 1 };
        }
    };
    const reference = await getBankTransactionsStream(500);
    assert.ok(Buffer.byteLength(JSON.stringify(reference)) < 32768);
    assert.deepEqual(JSON.parse(await payload.text()), rows);
});

test("empty history streams successfully", async () => {
    globalThis.fetch = async () => Response.json([]);
    globalThis.DotNet = { createJSStreamReference: blob => blob };
    assert.equal(await (await getBankTransactionsStream(500)).text(), "[]");
});

test("invalid, excessive and oversized responses fail without creating a stream", async () => {
    globalThis.DotNet = { createJSStreamReference() { assert.fail("Unexpected stream"); } };
    for (const response of [null, {}, Array(501).fill({}), [{ name: "x".repeat(4 * 1024 * 1024) }]]) {
        globalThis.fetch = async () => Response.json(response);
        await assert.rejects(getBankTransactionsStream(500), /transaction response/);
    }
});

test("expired authentication redirects to login without streaming", async () => {
    let destination;
    globalThis.window = { location: { assign: path => { destination = path; } } };
    globalThis.fetch = async () => new Response(null, { status: 401 });
    globalThis.DotNet = { createJSStreamReference() { assert.fail("Unexpected stream"); } };
    await assert.rejects(getBankTransactionsStream(500), /session expired/);
    assert.equal(destination, "/login");
});

test("failed BFF requests remain errors", async () => {
    globalThis.fetch = async () => new Response(null, { status: 503 });
    await assert.rejects(getBankTransactionsStream(500), /503/);
});
