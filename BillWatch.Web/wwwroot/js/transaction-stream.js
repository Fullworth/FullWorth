import { getBankTransactions } from "./bff.js";

export async function getBankTransactionsStream(take) {
    const transactions = await getBankTransactions(take);
    if (!Array.isArray(transactions) || transactions.length > 500) {
        throw new Error("BillWatch received an invalid transaction response.");
    }

    // Stream the result so a full history never becomes one oversized
    // browser-to-server SignalR message. Keep financial data in memory only.
    const payload = new Blob([JSON.stringify(transactions)], {
        type: "application/json"
    });
    if (payload.size > 4 * 1024 * 1024) {
        throw new Error("BillWatch received an oversized transaction response.");
    }

    return DotNet.createJSStreamReference(payload);
}
