# Offline AI evaluation runner

This command runs FullWorth's existing deterministic baseline or local-model
private-corpus evaluator. It is a standalone development tool; it is not
registered in the API, Web, or production runtime.

Keep the encrypted private corpus outside GitHub and outside the repository.
Each immediate child directory is one case and contains only
`statement.txt` and reviewer-approved `ground-truth.json`, as described in
[AI_SHADOW_CORPUS.md](../AI_SHADOW_CORPUS.md). Use authorized evaluation
documents and held-out cases. Do not place account numbers, credentials, or
unnecessary personal data in the corpus.

From the repository root, run the deterministic baseline first:

```sh
dotnet run --project FullWorth.AiEvaluation/FullWorth.AiEvaluation.csproj --configuration Release -- baseline --corpus-root /absolute/path/to/private-corpus
```

After reviewing the baseline, start the pinned local runtime. The startup
script verifies the exact model and runtime manifests and runs the
authenticated structured-output smoke:

```sh
sh deploy/start-ai-evaluation.sh .env.ai
```

Export the already-validated local runtime key without printing it, then
explicitly authorize local inference:

```sh
set +H
set -a
. ./.env.ai
set +a
dotnet run --project FullWorth.AiEvaluation/FullWorth.AiEvaluation.csproj --configuration Release -- local-ai --corpus-root /absolute/path/to/private-corpus --authorize-local-model-inference
```

Do not enable shell tracing while loading `.env.ai`. The local AI mode requires
the explicit authorization flag and calls only the loopback runtime. It prints
aggregate metrics and pinned model/runtime identifiers; it never prints case
identifiers, provider keys, statement text, ground truth, model output,
evidence, secrets, or corpus paths. It persists no records or result files.
Keep or redirect the aggregate report only to an approved private location.

The built-in coverage minimum is 100 cases across at least five providers,
with at least 10 cases for every provider. It must pass before local inference
starts. The default readiness policy also measures false alerts,
which this extraction-only runner does not fabricate; a successful extraction
benchmark therefore does not by itself pass the wider shadow-readiness gate.
This command cannot enable runtime shadow mode or AI-derived persistence.
