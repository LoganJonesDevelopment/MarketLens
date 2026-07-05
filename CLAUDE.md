# MarketLens

A locally-owned market research instrument. Public-source ingestion (SEC EDGAR, direct wire RSS, company IR feeds, FRED/BLS/BEA, Federal Reserve, FTC/DOJ/SEC enforcement, Federal Register, CourtListener, BIS, industry analysts, tech press, Reddit, Finnhub as low-trust supplement) plus on-host audio transcription. Dense embeddings, deduplicating clusters, a typed event schema with arithmetically-computed importance, and a research workspace that accumulates beliefs as falsifiable theses backed by stance-classified evidence. The Qwen LLM sees a cluster — never a single headline — and is reserved for the three jobs it's actually good at: cluster-level structured extraction, stance classification against a thesis, and thesis-plan generation.

## Operating premise

The point of this system is that **research output is computed from a corpus the pipeline already curated, not generated from a model's parametric memory**. Every step that *can* be arithmetic *is* arithmetic; the LLM is reserved for narrowly-scoped, constrained-JSON jobs where it has a real edge. Importance, conviction, source weight, novelty — all derived. Sentiment and stance — model judgments, but bounded by a thesis statement and grounded in cluster context. There is no general-purpose "ask the LLM about the market" surface and there should not be one. If you find yourself wanting to invent a fresh take in chat, you are working against the design.

When given a research assignment in this repo, the right behavior is to **read what's already been ingested, classified, and bound to a thesis** — and only then say what's missing. Do not call out to the open web. Do not ask the user to paste articles. Do not summarize from your training data and pass it off as evidence. The corpus is the point.

## Architecture — the five-stage cascade

1. **Source ingestion.** `INewsSource` implementations: `EdgarSource` (per-CIK polling across the full filing surface, with chunked 8-K exhibit extraction), `RssSource` (configurable, used for wires, IR feeds, Fed, BLS/BEA, regulators, analysts, tech press), `FredSource` (macro series), `FinnhubSource` (low-trust breadth), `CourtListenerSource` (litigation), `FederalRegisterSource` (BIS export controls and rulemakings), `RedditSource` (sentiment proxy across investing/hardware/ML subs). Each writes `articles` rows. Long-form filings additionally produce `article_chunks` for passage-level matching.
2. **Embedding + cluster assignment.** Every new article gets a 1024-dim `bge-large-en-v1.5` embedding from the embeddings sidecar, then `ClusterAssigner` queries pgvector for nearest-neighbor cluster within a 7-day rolling window at 0.85 cosine; attaches or creates.
3. **Triage classification.** `TriageClient` calls the DeBERTa-zeroshot sidecar against the typed `EventTypes` vocabulary on the cluster's representative text. Below threshold the cluster carries no `TriageEventType` and never reaches the LLM.
4. **Cluster-level event extraction.** `EventExtractionService` drains clusters with a confident triage label. `OllamaEventExtractor` calls `qwen3:latest` once per cluster with constrained JSON, no chain-of-thought, structured slot extraction, sentiment, summary, magnitude. The event type is *given*, not asked.
5. **Importance computation.** `ImportanceCalculator` produces `source_weight × novelty × event_class_prior × magnitude`. Source weights from `SourceReputation`, novelty from earliest article `PublishedAt` in the cluster, event-class priors hardcoded from conventional wisdom (upgradable from EDT/FNSPID backtests later), magnitude from the LLM's structured signal. The LLM never produces the importance score.

Audio transcripts are first-class citizens: queued via `/api/transcripts`, transcribed by the Whisper sidecar (faster-whisper large-v3, with yt-dlp for replay discovery), segmented and embedded, then matched to theses on a separate semantic pathway.

## Architecture — the research workspace

Theses are not chat sessions. A `ResearchThesis` is a persistent, embedded hypothesis with attached `ResearchAsset` links (tickers, concepts), `ThesisRule` rows (asset/concept keywords, event-type filters, source filters, similarity thresholds), and an accumulating `ResearchEvidence` ledger. The flow:

- **Bootstrap.** On thesis creation, `ThesisBootstrapper` builds a corpus context from the most-similar existing clusters in the last 60 days, then calls `OllamaThesisPlanner` to generate a plan with sub-tracks. Each sub-track becomes a `[plan] …` thesis rule with its own keyword bag and similarity threshold. The plan refreshes on a cadence via `ThesisPlanRefreshService`.
- **Match.** `ResearchMatcherService` runs every 60 seconds. For every active rule, it scans recent articles, events, transcript segments, and filing chunks; binds matches as `research_evidence` rows tagged with the matcher's reason and a cosine similarity. Asset keyword OR semantic similarity is sufficient — concept keywords broaden the reason text but never gate.
- **Classify.** `StanceClassificationService` drains unclassified evidence, calls Qwen with the thesis statement + cluster/segment/chunk context, and writes `Stance` (supports/contradicts/neutral/unknown), `StanceConfidence`, and a one-sentence `StanceRationale`. The user confirms or overrides; the override never bypasses the matcher's record.
- **Snapshot.** `ResearchSnapshot` rows freeze conviction state over time (the conviction-trajectory math itself is the next piece on deck — see `docs/research-workspace.md`).

The status vocabulary is `draft | active | exploration | watching | paused | validated | invalidated | archived`. Explorations are pre-thesis; promotion converts them to active.

## Architecture — the pipeline run ledger

`PipelineRun` and `PipelineMaterialization` give the four scheduled drains (`event_extraction`, `research_matcher`, `stance_classification`, `market_snapshots`) a self-reported observability surface. Every run records inputs/outputs/skips/errors, an error category (`transient | database | cancelled | unexpected`), free-form metadata as JSONB, and a list of asset keys it materialized. Read at `/api/pipeline/runs`, `/api/pipeline/stages` (rolled-up per-stage view), `/api/pipeline/materializations`. Use this before assuming the pipeline is healthy: a green sources health view tells you ingestion ran, but a green pipeline ledger tells you events, matches, and stances actually landed.

## Stack

- .NET 10 — `MarketLens.Api` / `MarketLens.Core` / `MarketLens.Infrastructure` under `MarketLens.slnx`
- Postgres 17 + pgvector (Docker, port 5434 on localhost; container `marketlens-postgres`)
- EF Core 10 + Npgsql + Pgvector.EntityFrameworkCore; migrations under `Infrastructure/Migrations`
- Embeddings sidecar — HuggingFace `text-embeddings-inference` running `BAAI/bge-large-en-v1.5` (port 5435, GPU)
- Triage sidecar — FastAPI container running `MoritzLaurer/deberta-v3-large-zeroshot-v2.0` (port 5436, GPU)
- Whisper sidecar — FastAPI + `faster-whisper large-v3` + `yt-dlp` for replay discovery (port 5437, GPU). Mounts the host Firefox profile read-only so it can use stored cookies for YouTube auth.
- Ollama on host (`http://localhost:11434`) running `qwen3:latest` (the model bound from `Ollama:Model` in appsettings) for cluster-level extraction, stance classification, and thesis planning. These are constrained-JSON jobs; classification proper is FinBERT-class work and lives in the triage sidecar.
- Polygon.io is the live-quote vendor for indices and futures (the Open / Sunday-night view). Configure with `POLYGON_API_KEY` in env or `dotnet user-secrets set Polygon:ApiKey <key>`. Without a key the quote ingestor logs a one-line warning at startup and the Open view shows the configured tickers in an unconfigured state — nothing else degrades.
- Web on port 5216 (Vite dev server in container `marketlens-web`); API on port 5210; both bound to localhost and to the tailscale interface.

## Schema cheatsheet

All Postgres column names are quoted PascalCase. **Every psql query against this DB must double-quote columns** (`select "Source", count(*) from articles ...`) — unquoted lowercase will error. Tables (snake_case) and their purpose:

- `articles` / `article_chunks` — raw ingest + passage chunks of long filings; both have a 1024-d embedding column
- `clusters` — dedup unit; one row per topic-window grouping; carries triage event type and confidence
- `events` — one row per cluster (PK = ClusterId); typed event with extracted slots, computed importance and components
- `suppressions` — anything dropped pre-extraction with a reason; a queryable explanation for "why isn't X here"
- `market_snapshots` — price/volume reaction at event time, with benchmark-relative move and a reaction score
- `price_bars` — backfilled OHLCV via Yahoo/Finnhub; intervals 1d primarily, with 3d/1w aggregated server-side
- `economic_events` — earnings calendar + FRED/BLS release calendar
- `transcripts` / `transcript_segments` — per-call audio; segments carry embeddings for thesis matching
- `research_assets` / `research_theses` / `thesis_assets` / `thesis_rules` — the thesis registry and its rules
- `research_evidence` — every matcher- or manually-attached binding from a thesis rule to an article/cluster/segment/chunk, with stance + rationale + review status
- `research_snapshots` — frozen conviction state per thesis over time
- `pipeline_runs` / `pipeline_materializations` — drain observability

The vector index lives on `transcript_segments.Embedding` (HNSW); article embeddings are unindexed today (corpus small enough for sequential scan inside a thesis-similarity batch).

## Source taxonomy

Sources fan into five tiers with explicit reputation weights (`Domain/SourceReputation.cs`):

- **Primary (1.00 / 0.95)** — EDGAR, FRED, BLS, BEA, BIS, Fed (speeches and press), SEC enforcement, FTC, DOJ Antitrust, CourtListener, transcripts. The arithmetic favors these; that's the entire point of the tier system.
- **Wire (0.90)** — BusinessWire, GlobeNewswire, PRNewswire. Company-issued press through the standard wires.
- **IR feeds (0.72)** — direct from each issuer's newsroom RSS where available (AAPL/MSFT/NVDA/GOOGL/AMZN/AMD/INTC/AVGO/MU/QCOM, etc.). Treated as primary tier in code but downweighted relative to EDGAR.
- **Trade press (0.55–0.85)** — mining.com, Tom's Hardware, Wccftech, HotHardware, ServeTheHome, The Register; SemiAnalysis / Stratechery / FabricatedKnowledge / The Chip Letter / Asianometry as `industry_analyst`.
- **Aggregator / opinion (0.30–0.40)** — Finnhub, Reddit. Kept for breadth; the math is supposed to drown them under low importance.

The watchlist (semis-heavy) lives in `TickerMetadata` and the seeded `research_assets` (kind `ticker`). EDGAR polls by CIK from this list; RSS feeds with `FilterByWatchlist: true` keep only matching items.

## Local dev

```
docker compose up -d                 # postgres + embeddings + triage + whisper + api + web
dotnet user-secrets set "Finnhub:ApiKey" "<key>" --project src/MarketLens.Api
dotnet user-secrets set "Fred:ApiKey"    "<key>" --project src/MarketLens.Api
dotnet ef database update --project src/MarketLens.Infrastructure --startup-project src/MarketLens.Api
```

API at `http://localhost:5210`, web at `http://localhost:5216`. Endpoints in five groups: events (`/api/events`, `/api/clusters/*`, `/api/inbox`, `/api/stats`, `/api/suppressions/*`, `/api/market-snapshots`), research (`/api/research/theses[/{id}/{scan,bootstrap,promote,rules,evidence,assets}]`, `/api/research/explorations`, `/api/research/assets`), chart (`/api/chart/{symbols/search,watch,watchlist,bars,marks,calendar}`), transcripts (`/api/transcripts/*`), sources health (`/api/sources/health`), pipeline ledger (`/api/pipeline/{runs,stages,materializations}`).

## Operational playbook — when given a research task

The default loop, in order, before doing anything generative:

1. **Identify symbols and concepts in scope.** Lean on `TickerMetadata` for canonical tickers and aliases. If a ticker isn't in the watchlist, decide whether the task warrants adding it (`POST /api/chart/watch`) versus declining for now.
2. **Check existing theses.** `GET /api/research/theses?symbol=X` or `select * from research_theses where "Status" = 'active'`. If a relevant thesis exists, your output should reference its evidence and stance distribution, not invent a parallel take. If none exists and the task is non-trivial, create an exploration (`POST /api/research/explorations`) — that triggers bootstrap, plan generation, and a 30-day initial scan in the background.
3. **Pull the high-importance events.** `GET /api/events?symbol=X&minImportance=0.3` for the bullets. Importance is already computed from source × novelty × class × magnitude, so trust the ranking.
4. **Pull the cluster drill where it matters.** `GET /api/clusters/{id}` for member articles, the extracted slots, and the market reaction snapshot. The reaction score is benchmark-relative against QQQ.
5. **For deeper passages, read the chunks and segments.** `select "Text" from article_chunks where "ArticleId" in (...)` for filings; `select "Text", "StartSeconds" from transcript_segments where "TranscriptId" = ...` for calls. These are 1024-d embedded — semantic queries against them work via `<=>` (cosine distance) in pgvector.
6. **Spot-check freshness.** `GET /api/sources/health` (the silent/stale/healthy summary) plus `select "Stage", "Status", max("StartedAt") from pipeline_runs group by 1, 2 order by 1, 3 desc` to confirm the drains are alive. If a source is silent, say so; do not pretend its absence is a finding.
7. **Synthesize from the corpus.** Quote cluster IDs, article URLs, segment timestamps, filing accessions. The output should be auditable back to rows in this database.

If the task asks for a *belief* ("is X bullish on Y") rather than a fact, frame the answer in terms of the thesis evidence ledger: stance distribution by source tier, top accepted contradicting evidence, the most recent kill-criterion-adjacent material. If the thesis hasn't been created, create it and let the matcher run before answering — five minutes of latency is cheaper than a vacuous take.

## Anti-patterns

Do not reach for the open web. No Brave / Google / Perplexity / Bing / fetch-and-summarize-an-article. No "let me search for recent news on X." If the pipeline doesn't have it, the answer is "the corpus doesn't have this; here's how to get it" — extend a source, queue a transcript, raise a thesis — not "let me improvise."

Do not use the LLM as a stance classifier or fact-extractor in chat. The pipeline already does it; queue the work and read the result. One-off model calls on individual articles bypass the source-weight math and produce conclusions you cannot reproduce.

Do not invent ticker metadata, source weights, or event priors at the chat layer. They live in `TickerMetadata`, `SourceReputation`, `EventClassPriors`. Edit the source if a value is wrong; do not paper over it conversationally.

Do not query Postgres with unquoted columns. Every column is PascalCase and case-sensitive — `select "Source"`, not `select source`.

Do not summarize what the user just saw. They have the diff and the dashboard; the value you add is decisions and the corpus you can cite.

## Verify before claiming

Memory and prior conversations describe the system at a frozen point in time. Before quoting numbers, table shapes, or endpoints, re-derive from the live state: `/api/stats`, `/api/sources/health`, `/api/pipeline/stages`, `psql` for ground truth. The watchlist, source list, and event-type vocabulary all evolve. If a remembered fact contradicts the live state, trust the live state and update the memory rather than the world.

## Non-goals (current POC)

No authentication; the API is bound to localhost. No multi-user features, no portfolio integration, no dollar-amount position sizing. No fine-tuned domain triage classifier yet (zero-shot DeBERTa is the floor; a domain fine-tune is the upgrade once a few hundred labeled examples exist). No event-class priors derived from EDT/FNSPID backtesting yet (hardcoded conventional-wisdom priors). No conviction-trajectory snapshots being computed yet — `ResearchSnapshot` exists; the Bayesian rollup is the next piece, specced in `docs/research-workspace.md`.
