# Offline AI evaluation runner

This command runs FullWorth's deterministic statement baseline and local-model
private-corpus evaluations. It is a standalone development tool; it is not
registered in the API, Web, or production runtime.

Keep the encrypted private corpus outside GitHub and outside the repository.
Each immediate child directory is one case and contains only
`statement.txt` and reviewer-approved `ground-truth.json`, as described in
[AI_SHADOW_CORPUS.md](../AI_SHADOW_CORPUS.md). Use authorized evaluation
documents and held-out cases. Do not place account numbers, credentials, or
unnecessary personal data in the corpus.

## Deterministic baseline

Run the deterministic parser baseline first:

```sh
dotnet run --project FullWorth.AiEvaluation/FullWorth.AiEvaluation.csproj --configuration Release -- baseline --corpus-root /absolute/path/to/private-corpus
```

## Start the guarded local runtime

The startup script verifies the approved model manifest, checks the exact GGUF
file size and SHA-256, launches the digest-pinned llama.cpp image, and runs the
authenticated structured-output smoke:

```sh
sh deploy/start-ai-evaluation.sh .env.ai
```

Export the already-validated local runtime key without printing it:

```sh
set +H
set -a
. ./.env.ai
set +a
```

Do not enable shell tracing while loading `.env.ai`.

## Evaluate one prompt

The default local evaluation uses the current prompt version:

```sh
dotnet run --project FullWorth.AiEvaluation/FullWorth.AiEvaluation.csproj --configuration Release -- local-ai --corpus-root /absolute/path/to/private-corpus --authorize-local-model-inference
```

A specific preserved prompt version may be selected explicitly:

```sh
dotnet run --project FullWorth.AiEvaluation/FullWorth.AiEvaluation.csproj --configuration Release -- local-ai --corpus-root /absolute/path/to/private-corpus --authorize-local-model-inference --prompt-version bill-statement-extraction-v1
```

Unsupported prompt versions fail closed. Statement text that exceeds the
configured local-AI input limit also fails closed before any inference request;
the extractor does not silently truncate the statement and then treat the
partial document as complete.

## Compare prompt v1 with v2

Use the comparison mode to run the preserved v1 baseline and current v2 prompt
against the exact same sorted case identifiers, model endpoint, deterministic
candidate validator, and ground-truth scorer:
For the paired inference runs, the runner loads and validates the selected cases
into one in-memory snapshot before the first inference, then reuses it for both
prompt versions. Editing or replacing corpus files during a run cannot silently
change the paired comparison.

```sh
dotnet run --project FullWorth.AiEvaluation/FullWorth.AiEvaluation.csproj --configuration Release -- compare-prompts --corpus-root /absolute/path/to/private-corpus --authorize-local-model-inference
```

The comparison reports overall precision, recall, ready-candidate rate,
provider-failure rate, raw count deltas, a fixed field-level breakdown for
TotalAmount, BillingPeriodStart, BillingPeriodEnd, StatementDate, DueDate,
CurrencyCode, and LineItems, plus anonymous provider-bucket comparisons. It
never reports a case, provider identity, statement, evidence excerpt, or model
response. Provider buckets use stable ordinals only.

A candidate qualifies only for **promotion review** when all of these are true:

- aggregate fact precision does not decrease;
- aggregate fact recall does not decrease;
- ready-candidate rate does not decrease;
- provider-failure rate does not increase;
- every fixed field has the same expected-fact population in both runs;
- for every fixed field, correct count does not decrease;
- for every fixed field, incorrect count does not increase;
- for every fixed field, missed count does not increase;
- for every fixed field, precision and recall do not decrease;
- every anonymous provider bucket has the same statement/attempt/expected-fact
  population in both runs;
- no anonymous provider bucket loses correct facts, gains incorrect/missed
  facts, loses precision/recall/readiness, or gains provider failures;
- within every anonymous provider bucket, each fixed field has the same
  expected-fact population and cannot lose correct facts, gain incorrect or
  missed facts, or lose precision/recall; and
- at least one aggregate metric strictly improves.

This means an overall improvement cannot hide a regression in a critical field
such as TotalAmount or DueDate, a regression concentrated in one provider, or
a DueDate regression for one provider offset by a different field within that
provider. The provider-field gate reports only the number of regressed
combinations; provider identities and case-level outputs are never emitted.

That flag does not promote a prompt automatically. It cannot enable runtime
shadow mode, production inference, alerts, or AI-derived persistence.

`compare-prompts` returns exit code `4` when the comparison completed but v2
does not qualify for promotion review. Coverage rejection returns exit code
`3`.

## Privacy and provenance

Local AI modes require the explicit authorization flag and runtime key before
opening the private corpus, and call only the loopback runtime. The runner
prints aggregate metrics and approved
model/runtime identifiers; it never prints case identifiers, provider keys,
statement text, ground truth, model output, evidence, secrets, or corpus paths.

The report explicitly states that runtime provenance is not independently
verified by the .NET runner. The guarded `deploy/start-ai-evaluation.sh` path
is what verifies the approved model file and digest-pinned runtime before
evaluation.

Local-model runs also report aggregate provider-attempt latency: minimum, mean,
p50, p95, and maximum milliseconds. Prompt comparison reports latency deltas,
but latency does not yet participate in promotion qualification because
FullWorth has not established an evidence-backed speed threshold. No per-case
timing samples are returned.

Provider-call failures are reported only as aggregate counts by coarse
vendor-neutral category (for example Timeout, Transport, HttpStatus,
InvalidStructuredOutput, IncompleteResponse, or OversizedResponse). Exception
messages, inner exceptions, response bodies, endpoints, provider identities,
and case identifiers are not written into the benchmark report.

The runner persists no records or result files. Keep or redirect aggregate
reports only to an approved private location.

The built-in coverage minimum is 100 cases across at least five providers,
with at least 10 cases for every provider. It must pass before local inference
starts. The wider shadow-readiness policy also requires false-alert evaluation,
which this extraction-only runner deliberately does not fabricate. Passing a
prompt comparison therefore does not by itself pass the full shadow-readiness
gate.
