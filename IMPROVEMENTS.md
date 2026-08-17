# BlogWriter — MAF Health & Improvement Report
_Generated 2026-08-17_

## 1. Tooling status

| Item | Value |
|---|---|
| maf-doctor tool version (installed) | `1.14.0` |
| maf-doctor latest available | `1.14.0` |
| Status | **Up to date** — no update needed |
| Workspace init | Current, no re-run of `maf-doctor init` required |

## 2. MAF health grade: **B**

Full scan (`MafDoctor --full`) results:

| Metric | Count |
|---|---:|
| Anti-pattern errors | 0 |
| Anti-pattern warnings | 0 |
| Anti-pattern info notes | 0 |
| Silent-starvation risks (fan-out) | 0 |
| Prompt-lint errors (injection, etc.) | 0 |
| Prompt-lint warnings | 0 |
| `[MessageHandler]` methods inspected | 4 |
| Agent call sites flagged "no `MaxOutputTokens` cap" | 4 (all false positives — see below) |

**The only findings are 4 heuristic `COST-001` notes** (one per agent: `AuthorAgent.cs:75`, `BloggerAgent.cs:126`, `ResearcherAgent.cs:88`, `ReviewerAgent.cs:77`) claiming the `RunAsync` call has no output-token cap. I verified this against the actual code: **it's a false positive.** Every agent already:
- caps `MaxOutputTokens` on the agent's own `ChatOptions` at construction, and
- re-applies `_maxOutputTokens` on a per-call `ChatClientAgentRunOptions` on every `RunAsync`.

The scanner is a text/name heuristic and doesn't trace the `options: runOptions` variable back to its construction, so it can't see the cap is actually there. No action needed — this repo has no real, unmitigated cost-runaway risk.

There is a genuinely good, second layer of cost protection on top of that: `TokenCapChatClient` enforces a **hard cumulative 10,000-token budget for the whole process**, across every model round-trip (including tool-invocation turns), and fails the run loudly (`TokenCapExceededException`) rather than silently overspending.

## 3. What's good — in plain language

- **Clean separation of concerns.** Each pipeline stage (Blogger, Researcher, Author, Reviewer) has its own interface, agent implementation, and workflow executor. The workflow graph (`BlogWorkflow.cs`) is a thin orchestration layer over that — easy to read, easy to test in isolation.
- **The workflow is provably terminating.** The reviewer→author loop is gated by `ResearchState.MaxRevisions` (a hard cap of 4), so there's no way for the graph to spin forever even if the model keeps asking for revisions.
- **Cost is controlled at two independent layers**: per-call `MaxOutputTokens` and a process-wide cumulative token budget (`TokenCapChatClient`). This is a genuinely strong pattern — most sample MAF apps only do one or the other.
- **Structured decision-making done right.** `BloggerAgent` uses `RunAsync<BloggerDecision>` to get a typed result straight from the model instead of hand-rolling JSON extraction/fenced-code-block stripping — this is the current MAF idiom and avoids a whole class of brittle parsing bugs.
- **Deterministic routing before LLM routing.** `BloggerAgent.InvokeAsync` short-circuits with plain C# rules (no research → researcher, has draft + approved → END, etc.) and only falls back to an LLM call when the state is genuinely ambiguous. This saves tokens and makes the common paths 100% predictable.
- **Secrets hygiene.** API keys come from `dotnet user-secrets` locally and environment variables in CI/prod, never hardcoded, with a clear fail-fast message (`GetRequired`) if a key is missing.
- **Observability is wired in, not bolted on.** Every agent and the workflow itself emits `ActivitySource` spans, and the `IChatClient` pipeline emits GenAI spans via `UseOpenTelemetry`. Swapping the console `ActivityListener` for a real `TracerProvider` is a one-line change.
- **Token-cap failure handling is correct.** A budget breach is deliberately *not* caught and swallowed — it's re-thrown through the workflow's `ExecutorFailedEvent` handling and all the way to `Program.cs`, so the process exits cleanly instead of continuing to spend money after the budget is blown.

## 4. What's wrong / weak — in plain language

None of these are MAF anti-patterns (the scanner is clean); they're general .NET/production-readiness gaps that matter if this app grows beyond a demo/CLI.

1. **`Console.WriteLine` instead of `ILogger` almost everywhere.** Every agent takes an `ILogger<T>` in its constructor but only uses it once (`"...initialized."`). All the actually interesting events — errors, decisions, review outcomes, revision counts — go to `Console.WriteLine`, which means they're unstructured, can't be filtered/routed by log level, and won't show up if this ever runs somewhere without a console (e.g. a hosted service or Azure Function).
2. **Broad `catch (Exception e)` blocks that swallow the real error.** `AuthorAgent`, `ResearcherAgent`, `ReviewerAgent`, and `BloggerAgent` all catch every exception from `RunAsync` and replace it with a generic fallback string ("Error generating draft...", "Research completed on: ..."). This means a genuine auth failure, rate-limit, network timeout, or bad-request error looks identical to the workflow as "the model didn't have much to say" — nothing distinguishes a transient/retryable failure from a permanent one, and the real exception is only ever printed to the console, never logged with the `ILogger` that's already injected.
3. **No `CancellationToken` anywhere in the agent/workflow APIs.** `InvokeAsync`, `*NodeAsync`, `RunAsync` (on `IBlogWorkflow`) — none of them accept or forward a `CancellationToken`. There's no way to cancel an in-flight run (Ctrl+C, a timeout, a hosting shutdown signal); the process can only be stopped by the token-cap exception or letting it run to completion.
4. **No resilience policy around outbound calls.** Neither the OpenAI chat client pipeline nor the raw `tavilyHttpClient` has retry/backoff/timeout policies. A single transient network blip on a Tavily call or a chat completion call falls straight into the generic `catch` and produces a silent, low-quality fallback rather than retrying once or twice.
5. **`tavilyHttpClient` is a manually constructed, unbounded-lifetime `HttpClient`.** It's fine for a short-lived console run, but it's not using `IHttpClientFactory`/`AddHttpClient`, has no request timeout configured (defaults to 100s), and would be a socket-exhaustion risk if this code were ever lifted into a long-running service.
6. **Duplicated "is approved?" string check.** `review.ToUpperInvariant().Contains("APPROVED")` is duplicated across `BloggerAgent` and `ReviewerAgent`. It's a magic string with no single source of truth — a typo in one place (e.g. "Approved" vs the exact literal used elsewhere) silently breaks the loop-exit condition.
7. **Model name and token budget are hardcoded in `Program.cs`** (`"gpt-4o-mini"`, `MaxOutputTokens = 4096`, cumulative cap `10000`). Fine for a demo; brittle if you want to swap models/budgets without recompiling.
8. **No automated tests.** There's no test project in the repo. The workflow's termination guarantee, the Blogger's deterministic routing rules, and the token-cap logic are all excellent candidates for fast, no-LLM-required unit tests (they're pure C# logic), but none exist today.
9. **Minor nit:** `TokenCapChatClient.Track` throws `InvalidOperationException` for a non-positive `maxTotalTokens` *inside the per-response hot path* rather than validating it once in the constructor — it should fail fast at construction instead of on the first token update.

## 5. Recommended action plan (priority order)

| # | Action | Effort | Why first |
|---|---|---|---|
| 1 | Replace `Console.WriteLine` calls in agents/workflow with `_logger.LogInformation/LogWarning/LogError`, including the caught exception object (`_logger.LogError(e, ...)`) instead of `e.Message` only. | Small | Everything downstream (diagnostics, prod-readiness) depends on this; `ILogger` is already injected everywhere. |
| 2 | In each agent's `catch (Exception e)` block, log the real exception via `ILogger` before returning the fallback string, and consider narrowing the catch (e.g. distinguish `ClientResultException`/HTTP errors from unexpected bugs). | Small–Medium | Currently a real outage looks identical to "model had nothing to say." |
| 3 | Thread a `CancellationToken` through `IBlogWorkflow.RunAsync` → node methods → `RunAsync` calls, and pass `Console.CancelKeyPress`/a timeout token from `Program.cs`. | Medium | Lets the app be stopped cleanly and bounds worst-case run time independent of the token cap. |
| 4 | Add a small resilience layer (e.g. `Microsoft.Extensions.Http.Resilience` for `tavilyHttpClient`, or a `.Use(...)` retry middleware on the `IChatClient` pipeline) for transient failures, with a sane per-call timeout. | Medium | Reduces "silent low-quality fallback" outcomes caused by one-off network blips. |
| 5 | Centralize the "approved" check as a single helper/constant (e.g. `ResearchState.IsApproved(string reviewNotes)` or a `const string ApprovedMarker = "APPROVED"`) used by both `BloggerAgent` and `ReviewerAgent`. | Small | Removes a duplicated magic string that both loop-exit paths depend on. |
| 6 | Move `modelName`, per-call `MaxOutputTokens`, and the cumulative token cap into configuration (`IConfiguration`/env vars) instead of literals in `Program.cs`. | Small | Lets you tune cost/model without recompiling. |
| 7 | Add a test project covering: `ResearchState.NeedsRevision` boundary conditions, `BloggerAgent`'s deterministic routing rules, and `TokenCapChatClient`'s cap-exceeded behavior. All three are pure logic — no live model calls required. | Medium | Cheapest tests to write, and they protect the two correctness guarantees (termination, budget) that matter most. |
| 8 | Fix the `TokenCapChatClient` constructor to validate `maxTotalTokens > 0` eagerly (throw in the constructor, not in `Track`). | Trivial | Fail-fast instead of failing on the first real response. |

Nothing above is required to keep the MAF grade at **B** — the scanner is already clean. These are general production-hardening items for when this moves beyond a demo CLI.
