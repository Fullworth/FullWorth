import { getBankTransactions } from "./bff.js";

let currentTransactionIndex = [];
let filterWorker = null;
let filterCleanup = null;
let nextWorkerRequestId = 1;
let filterState = {
    searchText: "",
    selectedAccountId: ""
};

const pendingWorkerRequests =
    new Map();

function getExecutionMode() {
    return window.FullWorthPerformance
        ?.getExecutionMode?.() ??
        "server";
}

function buildTransactionIndex(
    transactions) {

    return transactions.map(
        transaction => ({
            id:
                String(
                    transaction?.id ??
                    ""),

            accountId:
                String(
                    transaction?.bankAccountId ??
                    ""),

            searchText: [
                transaction?.name,
                transaction?.merchantName,
                transaction?.institutionName,
                transaction?.accountName,
                transaction?.categoryPrimary,
                transaction?.categoryDetailed
            ]
                .filter(value =>
                    typeof value === "string" &&
                    value.trim())
                .join("\n")
        }));
}

function rejectPendingWorkerRequests() {
    for (const pending
        of pendingWorkerRequests.values()) {

        window.clearTimeout(
            pending.timeoutId);

        pending.reject(
            new Error(
                "FullWorth local transaction processing stopped."));
    }

    pendingWorkerRequests.clear();
}

function disposeFilterWorker() {
    if (!filterWorker) {
        return;
    }

    filterWorker.terminate();
    filterWorker =
        null;

    rejectPendingWorkerRequests();
}

function ensureFilterWorker() {
    if (filterWorker) {
        return filterWorker;
    }

    const worker =
        new Worker(
            new URL(
                "./transaction-filter-worker.js",
                import.meta.url));

    worker.addEventListener(
        "message",
        event => {
            const requestId =
                Number(
                    event.data?.requestId ??
                    0);

            const pending =
                pendingWorkerRequests.get(
                    requestId);

            if (!pending) {
                return;
            }

            pendingWorkerRequests.delete(
                requestId);

            window.clearTimeout(
                pending.timeoutId);

            if (event.data?.ok !== true) {
                pending.reject(
                    new Error(
                        "FullWorth local transaction processing failed."));

                return;
            }

            pending.resolve(
                event.data);
        });

    worker.addEventListener(
        "error",
        () => {
            disposeFilterWorker();
        });

    filterWorker =
        worker;

    return filterWorker;
}

function sendWorkerRequest(
    type,
    payload = {}) {

    const worker =
        ensureFilterWorker();

    const requestId =
        nextWorkerRequestId++;

    return new Promise(
        (resolve, reject) => {
            const timeoutId =
                window.setTimeout(
                    () => {
                        pendingWorkerRequests.delete(
                            requestId);

                        reject(
                            new Error(
                                "FullWorth local transaction processing timed out."));
                    },
                    3000);

            pendingWorkerRequests.set(
                requestId,
                {
                    resolve,
                    reject,
                    timeoutId
                });

            worker.postMessage({
                requestId,
                type,
                ...payload
            });
        });
}

function updateResultLabel(
    element,
    count) {

    if (!element) {
        return;
    }

    const singular =
        element.dataset
            .singularLabel ??
        "1 transaction";

    const pluralTemplate =
        element.dataset
            .pluralTemplate ??
        "{count} transactions";

    element.textContent =
        count === 1
            ? singular
            : pluralTemplate.replace(
                "{count}",
                String(count));
}

async function applyLocalFilter(
    searchInput,
    accountSelect,
    resultCount,
    emptyState,
    rows) {

    filterState = {
        searchText:
            searchInput.value ??
            "",

        selectedAccountId:
            accountSelect.value ??
            ""
    };

    const result =
        await sendWorkerRequest(
            "filter",
            filterState);

    const visibleIds =
        new Set(
            Array.isArray(
                result?.ids)
                ? result.ids
                : []);

    let visibleCount =
        0;

    for (const row of rows) {
        const transactionId =
            row.dataset
                .fullworthTransactionId ??
            "";

        const isVisible =
            visibleIds.has(
                transactionId);

        row.hidden =
            !isVisible;

        if (isVisible) {
            visibleCount++;
        }
    }

    updateResultLabel(
        resultCount,
        visibleCount);

    if (emptyState) {
        emptyState.hidden =
            visibleCount !== 0;
    }
}

export async function getBankTransactionsStream(take) {
    const transactions = await getBankTransactions(take);
    if (!Array.isArray(transactions) || transactions.length > 500) {
        throw new Error("FullWorth received an invalid transaction response.");
    }

    currentTransactionIndex =
        getExecutionMode() === "local"
            ? buildTransactionIndex(
                transactions)
            : [];

    if (currentTransactionIndex.length === 0) {
        disposeFilterWorker();
    }

    // Stream the result so a full history never becomes one oversized
    // browser-to-server SignalR message. Keep financial data in memory only.
    const payload = new Blob([JSON.stringify(transactions)], {
        type: "application/json"
    });
    if (payload.size > 4 * 1024 * 1024) {
        throw new Error("FullWorth received an oversized transaction response.");
    }

    // IJSStreamReference interop wraps this Blob automatically.
    return payload;
}

export async function activateLocalTransactionFiltering(
    searchInputId,
    accountSelectId,
    resultCountId,
    emptyStateId) {

    if (getExecutionMode() !== "local" ||
        currentTransactionIndex.length === 0) {
        return false;
    }

    const searchInput =
        document.getElementById(
            searchInputId);

    const accountSelect =
        document.getElementById(
            accountSelectId);

    const resultCount =
        document.getElementById(
            resultCountId);

    const emptyState =
        document.getElementById(
            emptyStateId);

    const rows =
        Array.from(
            document.querySelectorAll(
                "[data-fullworth-transaction-id]"));

    if (!searchInput ||
        !accountSelect ||
        !resultCount ||
        rows.length === 0) {
        return false;
    }

    if (filterCleanup) {
        filterCleanup();
        filterCleanup =
            null;
    }

    try {
        await sendWorkerRequest(
            "initialize",
            {
                rows:
                    currentTransactionIndex
            });
    }
    catch {
        disposeFilterWorker();
        return false;
    }

    searchInput.value =
        filterState.searchText;

    accountSelect.value =
        filterState.selectedAccountId;

    let debounceTimer =
        null;

    const apply =
        async () => {
            try {
                await applyLocalFilter(
                    searchInput,
                    accountSelect,
                    resultCount,
                    emptyState,
                    rows);
            }
            catch {
                disposeFilterWorker();
            }
        };

    const onSearchInput =
        () => {
            if (debounceTimer !== null) {
                window.clearTimeout(
                    debounceTimer);
            }

            debounceTimer =
                window.setTimeout(
                    apply,
                    60);
        };

    const onAccountChange =
        () => {
            if (debounceTimer !== null) {
                window.clearTimeout(
                    debounceTimer);

                debounceTimer =
                    null;
            }

            void apply();
        };

    searchInput.addEventListener(
        "input",
        onSearchInput);

    accountSelect.addEventListener(
        "change",
        onAccountChange);

    filterCleanup =
        () => {
            searchInput.removeEventListener(
                "input",
                onSearchInput);

            accountSelect.removeEventListener(
                "change",
                onAccountChange);

            if (debounceTimer !== null) {
                window.clearTimeout(
                    debounceTimer);
            }
        };

    await apply();

    return true;
}

export function deactivateLocalTransactionFiltering() {
    if (filterCleanup) {
        filterCleanup();
        filterCleanup =
            null;
    }
}
