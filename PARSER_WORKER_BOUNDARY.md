# Document parser worker boundary

## Status

This is the security design contract for the next Issue #291 implementation step. The current API still runs PDF parsing and native OCR in-process. This document does not claim process isolation until the worker is implemented, deployed, and verified by CI.

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
