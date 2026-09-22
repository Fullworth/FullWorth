let transactionIndex = [];

function normalize(value) {
    return String(value ?? "")
        .trim()
        .toLocaleLowerCase();
}

function initialize(rows) {
    if (!Array.isArray(rows) ||
        rows.length > 500) {
        throw new Error(
            "FullWorth received an invalid local transaction index.");
    }

    transactionIndex =
        rows.map(row => ({
            id:
                String(row?.id ?? ""),

            accountId:
                normalize(
                    row?.accountId),

            searchText:
                normalize(
                    row?.searchText)
        }));
}

function filterTransactions(
    searchText,
    selectedAccountId) {

    const search =
        normalize(
            searchText);

    const accountId =
        normalize(
            selectedAccountId);

    return transactionIndex
        .filter(row =>
            (!accountId ||
             row.accountId === accountId) &&
            (!search ||
             row.searchText.includes(search)))
        .map(row =>
            row.id);
}

self.addEventListener(
    "message",
    event => {
        const requestId =
            event.data?.requestId;

        try {
            switch (event.data?.type) {
                case "initialize":
                    initialize(
                        event.data.rows);

                    self.postMessage({
                        requestId,
                        ok:
                            true,
                        type:
                            "initialized"
                    });
                    break;

                case "filter":
                    self.postMessage({
                        requestId,
                        ok:
                            true,
                        type:
                            "filtered",
                        ids:
                            filterTransactions(
                                event.data.searchText,
                                event.data.selectedAccountId)
                    });
                    break;

                default:
                    throw new Error(
                        "Unsupported FullWorth worker request.");
            }
        }
        catch {
            self.postMessage({
                requestId,
                ok:
                    false
            });
        }
    });
