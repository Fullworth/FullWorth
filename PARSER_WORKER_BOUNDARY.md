# Document parser worker boundary

## Status

This document records the security design contract and the partial implementation delivered for Issue #291. It does not claim full statement-processing isolation or production acceptance.

## Current implementation and residual gaps

PR #585 adds a dedicated parser-worker container for PDF text-layer extraction. The API sends a bounded PDF body over the internal-only `parser_worker` Docker network. The worker accepts one request at a time, starts a fresh parser child with a sanitized environment, kills the process tree after a 20-second deadline, validates the protocol response, and returns no more than the configured response bound. Parser exceptions and stderr are not returned to the API caller or logged.

The production Compose configuration gives the worker a read-only root filesystem, dedicated non-root user, no Linux capabilities, no published ports or mounted volumes, an internal-only network, a 64-process ceiling, and finite CPU, memory, and swap ceilings. Worker readiness fails closed unless the cgroup v2 CPU, memory, and swap limits are visible. Exact-head CI must still verify image build, stack readiness, and the full test suite before this change is merged. No production deployment is implied.

Scope is limited to PDF text-layer extraction. Scanned-document OCR still runs inside the API process after the worker reports `needs_ocr`; therefore the API remains able to open the original statement for that path. The worker child shares the worker container's cgroup rather than receiving a distinct per-document cgroup. The API-to-worker transport relies on Docker network membership and has no application-layer authentication; the worker network must remain attached only to the API and parser-worker services. These limits do not meet the full-document isolation contract below.

## Trust boundary

The API process must remain the owner of authentication, authorization, database writes, statement storage, and structured extraction. A dedicated parser worker must be the only process allowed to open an uploaded PDF or image for document text extraction.

The worker must receive a one-shot request over a local, authenticated IPC channel and return a bounded response. It must not receive database credentials, user session material, Plaid credentials, or unrestricted storage access.

The preferred production shape is a short-lived worker process per document, launched by the API-side supervisor with an explicit executable path and a private per-request working directory. The worker reads only the already-authorized input file and writes only the bounded response. The supervisor must kill the entire process tree when a deadline or resource ceiling is exceeded.

## Request and response contract

Request fields:

- protocol version
- authorized input path or descriptor
- media type and extension
- maximum parser input bytes
- maximum pages
- maximum declared image pixels per image
- maximum cumulative OCR pixels
- maximum extracted characters per page
- absolute deadline

Response fields:

- protocol version
- outcome: text, needs_ocr, or rejected
- page count
- bounded extracted text, when present
- stable rejection code, never a parser stack trace

The response must be framed with a length prefix and reject frames larger than the configured response limit before allocation. Invalid protocol versions, missing fields, extra fields, duplicate fields, and trailing bytes are fatal worker errors.

## Required ceilings

The supervisor must enforce these independently of parser cooperation:

1. wall-clock deadline, followed by process-tree termination;
2. address-space or cgroup memory ceiling, followed by process termination;
3. CPU quota or equivalent host-enforced CPU ceiling;
4. maximum input bytes before the worker starts parsing;
5. maximum output bytes before the API accepts the response.

The worker must also enforce the existing semantic document limits: page count, image dimensions, per-image bytes, cumulative OCR pixels, and per-page text characters. These semantic limits are defense in depth; they are not substitutes for process-level ceilings.

## Failure handling

The API must treat timeout, memory kill, CPU kill, abnormal exit, protocol violation, and malformed parser output as the same non-retryable document-processing failure class unless an operator explicitly enables a bounded retry policy. It must mark the upload failed without persisting raw document text or parser diagnostics.

The API must never reuse a worker after a timeout, memory kill, protocol violation, or abnormal exit. Temporary input and output paths must be removed by the supervisor in a finally block, including when the API is shutting down.

## Verification gates

Before enabling the worker for production traffic, CI must cover:

- valid text PDF and image happy paths;
- malformed, truncated, corrupt, compressed, and decompression-bomb fixtures;
- oversized declared streams and invalid filters;
- protocol truncation, oversized frames, duplicate fields, and trailing bytes;
- deadline expiry with a worker that never exits;
- worker abnormal exit and process-tree cleanup;
- memory and CPU ceiling enforcement on the supported Linux container runtime;
- no raw document text or secrets in API, worker, or container logs.

The production container must run the worker with a read-only root filesystem, no network access, a dedicated non-root identity, dropped capabilities, a private temporary directory, and only the minimum input/output mounts. The exact enforcement mechanism must be tested on the supported deployment host; configuration alone is not evidence of containment.

## Explicit current limitation

Until the worker and its supervisor are implemented and enabled, FullWorth does not provide separate-process CPU/RAM isolation, a hard peak-memory bound for a permitted 50M-pixel image, parser/native-library sandboxing, or decompression-expansion containment. Existing PDF/OCR limits reduce exposure but must not be described as hard process or host resource isolation.
