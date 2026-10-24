// Bounded client waiting; server ownership and provider limits remain authoritative.
export const completionPollIntervalMs = 35000;

function pause(ms, signal) {
    return new Promise((resolve, reject) => {
        if (signal?.aborted) return reject(new DOMException("Aborted", "AbortError"));
        const abort = () => { clearTimeout(timer); reject(new DOMException("Aborted", "AbortError")); };
        const timer = setTimeout(() => { signal?.removeEventListener("abort", abort); resolve(); }, ms);
        signal?.addEventListener("abort", abort, { once: true });
    });
}

// Session creation plus at most 18 reads fit the existing 20/10-minute budget.
// Other operations can still consume that budget: honor server throttling too.
export async function pollPlaidCompletion({
    readStatus, isCurrent, signal, isPopupClosed = () => false, now = Date.now, sleep = pause,
    timeoutMs = 10 * 60 * 1000
}) {
    const deadline = now() + timeoutMs;
    let waitMs = 2000;
    let popupClosedPendingChecks = 0;
    while (isCurrent() && !signal?.aborted && now() < deadline) {
        await sleep(Math.min(waitMs, deadline - now()), signal);
        if (!isCurrent() || signal?.aborted) return null;
        if (now() >= deadline) return { status: "TimedOut" };
        try {
            const result = await readStatus(signal);
            if (!isCurrent() || signal?.aborted) return null;
            if (typeof result?.status !== "string") {
                throw new Error("FullWorth received an invalid Link status.");
            }
            const state = result.status.toLowerCase();
            if (["completed", "exited", "expired", "failed"].includes(state)) return result;
            if (state !== "pending") {
                throw new Error("FullWorth received an invalid Link status.");
            }
            popupClosedPendingChecks = isPopupClosed() ? popupClosedPendingChecks + 1 : 0;
            if (popupClosedPendingChecks >= 2) return { status: "PopupClosed" };
            waitMs = completionPollIntervalMs;
        } catch (error) {
            if (!isCurrent() || signal?.aborted) return null;
            if (error?.status !== 429) throw error;
            const retry = Number(error.retryAfterMs);
            waitMs = Math.max(completionPollIntervalMs,
                Number.isFinite(retry) && retry > 0 ? retry : 60000);
        }
    }
    return isCurrent() && !signal?.aborted ? { status: "TimedOut" } : null;
}

// A completed Link session is not proof of a usable connected bank.
// The BFF must supply a currently active owner-scoped bank with an ID.
export function isActiveLinkResult(result) {
    const bankId = result?.connection?.id;
    return result?.status === "Completed" &&
        result.connection?.status === "Active" &&
        typeof bankId === "string" &&
        /^[0-9a-f]{8}-(?:[0-9a-f]{4}-){3}[0-9a-f]{12}$/i.test(bankId);
}
