## BFF statement upload body bound — 2026-09-29

PR #548 adds `RequestSizeLimit` and matching `RequestFormLimits` metadata of 16 MiB to the authenticated Web/BFF statement-upload endpoint. This enforces the total request limit at the server before form parsing, including requests without `Content-Length`, while preserving the existing 15 MiB individual-file limit and API-side request/form limits. A regression test verifies both route metadata values.

Exact PR #548 head `eb2626aa5dab2f2b50de1a641162a5d87cf05f5d` passed FullWorth CI #1293 (run `36642741581`, including backend/tests, visual acceptance, and isolated backup/recovery) and dependency security #393 (run `36642741583`). It was squash-merged into `development` as `1a95e4dc2c590a1660b39ee3ccda4f5b3e9fc0cc`. No production deployment occurred. The broader API/BFF endpoint input-bounds inventory remains open in security issue #291.

## BFF statement upload body bound — 2026-09-29

PR #548 adds `RequestSizeLimit` and matching `RequestFormLimits` metadata of 16 MiB to the authenticated Web/BFF statement-upload endpoint. This enforces the total request limit at the server before form parsing, including requests without `Content-Length`, while preserving the existing 15 MiB individual-file limit and API-side request/form limits. A regression test verifies both route metadata values.

Exact PR #548 head `eb2626aa5dab2f2b50de1a641162a5d87cf05f5d` passed FullWorth CI #1293 (run `36642741581`, including backend/tests, visual acceptance, and isolated backup/recovery) and dependency security #393 (run `36642741583`). It was squash-merged into `development` as `1a95e4dc2c590a1660b39ee3ccda4f5b3e9fc0cc`. No production deployment occurred. The broader API/BFF endpoint input-bounds inventory remains open in security issue #291.

