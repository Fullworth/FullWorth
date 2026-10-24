import { getBankTransactions } from "./bff.js";

// Financial data stays in memory and is erased on page disposal.
let currentTransactionIndex = [];
let filterWorker = null;
let filterCleanup = null;
let bindingVersion = 0;
let loadVersion = 0;
let nextWorkerRequestId = 1;
let filterState = { searchText: "", selectedAccountId: "" };
const pendingWorkerRequests = new Map();

function getExecutionMode() {
    try {
        return globalThis.FullWorthPerformance?.getExecutionMode?.() ?? "server";
    } catch {
        // A crashed optional device profiler cannot block a real transaction read.
        return "server";
    }
}

function normalize(value) {
    return String(value ?? "").trim().toLocaleLowerCase();
}

function buildTransactionIndex(transactions) {
    return transactions.map(transaction => ({
        id: String(transaction?.id ?? ""),
        accountId: normalize(transaction?.bankAccountId),
        searchText: normalize([
            transaction?.name, transaction?.merchantName,
            transaction?.institutionName, transaction?.accountName,
            transaction?.categoryPrimary, transaction?.categoryDetailed
        ].filter(value => typeof value === "string" && value.trim()).join("\n"))
    }));
}

function disposeFilterWorker() {
    const worker = filterWorker;
    filterWorker = null;
    worker?.terminate();
    for (const pending of pendingWorkerRequests.values()) {
        window.clearTimeout(pending.timeoutId);
        pending.reject(new Error("FullWorth local transaction processing stopped."));
    }
    pendingWorkerRequests.clear();
}

function ensureFilterWorker() {
    if (filterWorker) return filterWorker;
    const worker = new Worker(new URL("./transaction-filter-worker.js", import.meta.url));
    worker.addEventListener("message", event => {
        if (worker !== filterWorker) return;
        const pending = pendingWorkerRequests.get(Number(event.data?.requestId ?? 0));
        if (!pending) return;
        pendingWorkerRequests.delete(event.data.requestId);
        window.clearTimeout(pending.timeoutId);
        if (event.data?.ok !== true) {
            pending.reject(new Error("FullWorth local transaction processing failed."));
        } else {
            pending.resolve(event.data);
        }
    });
    worker.addEventListener("error", () => {
        if (worker === filterWorker) disposeFilterWorker();
    });
    filterWorker = worker;
    return worker;
}

function sendWorkerRequest(type, payload = {}) {
    const worker = ensureFilterWorker();
    const requestId = nextWorkerRequestId++;
    return new Promise((resolve, reject) => {
        const timeoutId = window.setTimeout(() => {
            pendingWorkerRequests.delete(requestId);
            reject(new Error("FullWorth local transaction processing timed out."));
        }, 3000);
        pendingWorkerRequests.set(requestId, { resolve, reject, timeoutId });
        try {
            worker.postMessage({ requestId, type, ...payload });
        } catch (error) {
            pendingWorkerRequests.delete(requestId);
            window.clearTimeout(timeoutId);
            reject(error);
        }
    });
}

function updateResultLabel(element, count) {
    const singular = element.dataset.singularLabel ?? "1 transaction";
    const plural = element.dataset.pluralTemplate ?? "{count} transactions";
    element.textContent = count === 1 ? singular : plural.replace("{count}", String(count));
}

export async function getBankTransactionsStream(take) {
    const version = ++loadVersion;
    const transactions = await getBankTransactions(take);
    if (version !== loadVersion) throw new Error("FullWorth transaction load was superseded.");
    if (!Array.isArray(transactions) || transactions.length > 500) {
        throw new Error("FullWorth received an invalid transaction response.");
    }
    // Stream instead of sending financial history as an oversized SignalR message.
    const payload = new Blob([JSON.stringify(transactions)], { type: "application/json" });
    if (payload.size > 4 * 1024 * 1024) {
        throw new Error("FullWorth received an oversized transaction response.");
    }
    // Never replace the working index with an invalid or failed refresh.
    currentTransactionIndex = getExecutionMode() === "local" ? buildTransactionIndex(transactions) : [];
    return payload;
}

export async function activateLocalTransactionFiltering(searchInputId, accountSelectId, resultCountId, emptyStateId) {
    filterCleanup?.();
    filterCleanup = null;
    const version = ++bindingVersion;
    if (getExecutionMode() !== "local" || currentTransactionIndex.length === 0) return false;

    const searchInput = document.getElementById(searchInputId);
    const accountSelect = document.getElementById(accountSelectId);
    const resultCount = document.getElementById(resultCountId);
    const emptyState = document.getElementById(emptyStateId);
    const rows = Array.from(document.querySelectorAll("[data-fullworth-transaction-id]"));
    if (!searchInput || !accountSelect || !resultCount || rows.length === 0) return false;

    const index = currentTransactionIndex;
    let workerAvailable = true;
    try {
        await sendWorkerRequest("initialize", { rows: index });
    } catch {
        if (version !== bindingVersion) return false;
        disposeFilterWorker();
        workerAvailable = false;
    }
    if (version !== bindingVersion || document.getElementById(searchInputId) !== searchInput) return false;

    searchInput.value = filterState.searchText;
    accountSelect.value = filterState.selectedAccountId;
    // An account can disappear after a refresh. Do not retain an invisible filter.
    if (!accountSelect.value) filterState.selectedAccountId = "";

    let sequence = 0;
    let debounceTimer = null;
    const isCurrent = requestSequence => version === bindingVersion &&
        requestSequence === sequence && document.getElementById(searchInputId) === searchInput;

    const apply = async () => {
        const requestSequence = ++sequence;
        const state = { searchText: searchInput.value ?? "", selectedAccountId: accountSelect.value ?? "" };
        filterState = state;
        let ids;
        try {
            if (!workerAvailable) throw new Error("Use bounded in-memory fallback.");
            ids = (await sendWorkerRequest("filter", state))?.ids;
            if (!Array.isArray(ids)) throw new Error("Invalid worker result.");
        } catch {
            if (!isCurrent(requestSequence)) return;
            disposeFilterWorker();
            workerAvailable = false;
            // Keep controls working even if Web Workers are unavailable or crash.
            const search = normalize(state.searchText);
            const account = normalize(state.selectedAccountId);
            ids = index.filter(row => (!account || row.accountId === account) &&
                (!search || row.searchText.includes(search))).map(row => row.id);
        }
        if (!isCurrent(requestSequence)) return;
        const visibleIds = new Set(ids);
        let count = 0;
        for (const row of rows) {
            row.hidden = !visibleIds.has(row.dataset.fullworthTransactionId ?? "");
            if (!row.hidden) count++;
        }
        updateResultLabel(resultCount, count);
        if (emptyState) emptyState.hidden = count !== 0;
    };
    const onSearchInput = () => {
        // Invalidate outstanding results immediately, before the debounce elapses.
        sequence++;
        if (debounceTimer !== null) window.clearTimeout(debounceTimer);
        debounceTimer = window.setTimeout(() => { debounceTimer = null; void apply(); }, 60);
    };
    const onAccountChange = () => {
        sequence++;
        if (debounceTimer !== null) window.clearTimeout(debounceTimer);
        debounceTimer = null;
        void apply();
    };
    searchInput.addEventListener("input", onSearchInput);
    accountSelect.addEventListener("change", onAccountChange);
    filterCleanup = () => {
        sequence++;
        searchInput.removeEventListener("input", onSearchInput);
        accountSelect.removeEventListener("change", onAccountChange);
        if (debounceTimer !== null) window.clearTimeout(debounceTimer);
    };
    await apply();
    return version === bindingVersion && document.getElementById(searchInputId) === searchInput;
}

export function deactivateLocalTransactionFiltering() {
    bindingVersion++;
    loadVersion++;
    filterCleanup?.();
    filterCleanup = null;
    disposeFilterWorker();
    currentTransactionIndex = [];
    filterState = { searchText: "", selectedAccountId: "" };
}
