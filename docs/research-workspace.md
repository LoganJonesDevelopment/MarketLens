# MarketLens Research Workspace — Feature Plan

## Premise

The Research workspace is where a person tests a hypothesis against the news pipeline over time. Today it works — but it is shaped like an inbox: a list of articles with accept / reject / defer buttons. It needs to be shaped like an instrument: a set of beliefs that move as evidence accumulates, with falsification triggers, expected catalysts, and a closing loop on source quality.

This document is the feature plan for that redesign. It is UX-led. Schema and route choices are noted where they constrain the UX but specified at implementation time.

## Design principles

**Conviction is computed, not generated.** Same principle the event importance pipeline already applies. The user's degree of belief in a thesis is a derived number with documented inputs (source weight, novelty, importance, stance), not an LLM-emitted score and not a slider in isolation. The user's input is the prior and any explicit override; the math does the rest.

**Stance is assisted, not asked.** Every evidence row should arrive with a Qwen-classified stance and a one-sentence rationale. The user's job is to confirm or override, not to enter from blank. The LLM is allowed to say "ambiguous" — that is a routing signal, not a failure mode.

**Falsification has equal billing with confirmation.** Kill criteria and pre-mortem causes are first-class fields on a thesis, embedded as separate vectors and scanned on a separate signal pathway. A clean hit on a kill criterion outranks a hundred concept-keyword matches.

**Catalysts are pre-registered, not retrofitted.** When a catalyst is on the calendar, the user records what would support and what would contradict *before* the date arrives. The post-date entry is a forced reckoning, not retrospective rationalization.

**Every click trains the system.** Accept/reject and stance overrides feed a per-publisher accuracy posterior, refining source weight personally over time. The user's labor produces signal beyond the immediate decision.

**The workspace is a daily driver, not a dashboard.** Hotkeys for the dominant action loops. "Since last visit" indicators. Stale-thesis nudges. Weekly digests. The workspace expects to be opened on a routine.

## Layout

A three-pane layout replaces the current two-pane.

**Left rail** is the thesis list. Each row carries the thesis name, primary symbol, status badge, current conviction reading with a four-week trajectory sparkline, an evidence-since-last-visit pill, and a stale flag if applicable. Filters at the top: status, scope (all / watchlist / specific symbol), recent activity. The current "All / Watchlist / hardcoded ticker chips" arrangement is replaced with the actual user asset list driven by the thesis-asset linkage; nothing about the watchlist should be hardcoded in the page.

**Center pane** is the thesis detail and evidence queue. Header shows thesis name, status, conviction reading, time since last update, and a kebab menu for actions (edit, archive, snapshot now, generate brief, close as validated/invalidated). Below the header: tabs for Queue, Catalysts, Kill Criteria, Snapshots, Linked Theses, Briefs. Default tab is Queue.

**Right rail** is contextual. When an evidence row is focused: cluster drill (member articles, market reaction, extracted event, stance rationale with article citations). When a catalyst is focused: pre-registered expectations and the actual outcome. When the LLM has an open suggestion (rationale, kill-criterion match, related thesis): the suggestion card. The right rail collapses to a strip on narrow screens; the left rail collapses to a hamburger.

## Thesis creation and editing

The single big text box on the current creation panel is replaced with structured fields, each justified by a workflow it enables.

A short name and a one-paragraph statement remain. Below them, a *consensus view* field — what does the market currently believe — and a *variant view* field — what do you believe instead. The two together let stance classification ask "does this support the variant or the consensus?" rather than the more ambiguous "support the thesis." Both fields are embedded as separate vectors so the matcher can score similarity against either.

Below the variant view, *kill criteria* — three to five specific observations that would invalidate the thesis. Free text, one per line. Embedded separately and scanned on a distinct pathway from concept keywords. A clean similarity hit on a kill criterion is surfaced as a high-priority anti-thesis signal regardless of stance score.

Below kill criteria, *pre-mortem causes* — "twelve months from now this thesis has failed; what happened?" Three to five entries, free text, embedded the same way. Distinct from kill criteria in that they are failure modes, not falsification observations.

Asset keywords, concept keywords, exclusion terms, source-tier filters, event-type filters remain as today but are surfaced explicitly with help text explaining how each is used. Asset keywords are hard filters; concept keywords are soft semantic anchors; exclusions are hard rejects at the cluster level (a cluster with one excluded article is not dropped if the rest is on-topic). Each rule line is editable in place once the thesis exists; the current "create-only" pattern is replaced.

A *catalyst block* completes the form. Optional but encouraged. Each catalyst has a date or date range, a label, a class (earnings / FOMC / FDA / SEC filing window / FRED release / custom), a pre-registered expectation, and a "what would support / what would contradict" pair. Catalysts can be added, removed, or rolled forward post-creation.

*Initial conviction* is a single 0–100% slider with a default of 50. The user is asked, in plain language: "how confident are you that this thesis will be borne out?" That value seeds the prior for the conviction math.

The form is split into a primary section (name, statement, conviction prior) and an "expand for falsification structure / catalysts / detailed rules" disclosure. A user creating a quick thesis can save with just the primary section; the structured fields are encouraged for theses promoted from draft to active.

A *smart-synonym expansion* runs at save time: a single Qwen call expands asset and concept keywords into a synonym bag (e.g., "rate cuts" → "FOMC easing, dovish pivot, lower fed funds, terminal rate"), stored on the rule and used at scan time. The user sees and can edit the expanded list before save.

## Evidence queue (the dominant view)

The queue is the action loop. It needs to feel like a Bloomberg-style power tool, not a CRUD form.

**Default mode: cluster, not article.** One row per cluster, with the dominant article surfaced and a "+N more" affordance to expand inline. Toggle to flatten if needed. The schema already supports this; the current per-article rendering is the lower-resolution view of higher-resolution data.

Each row shows: source-tier dot, dominant publisher and tier badge, headline (linked), Qwen stance verdict with a confidence bar, a supports/contradicts/ambiguous/unknown pill, the relevance/novelty/importance triad as three small bars (not a single composite — composites hide the case where high importance meets low confidence), age, sentiment dot, and the matcher's reason as a one-line subtitle. Cosine similarity is part of the row, not hidden.

A pinned **"Strongest contradictions" rail** sits above the chronological queue, showing the top two or three contradicting clusters by importance × source weight. Confirmation-bias drift is the dominant failure mode of accept/reject UIs; pinning the worst news structurally defends against it.

A **source-tier × stance heatmap** sits above the queue in a collapsible strip. Three stances across, three tiers down, intensity by count, click any cell to filter. Catches the canonical failure where supports is forty aggregator listicles and contradicts is one primary filing.

**Filter chips** above the queue: stance, tier, event type, similarity threshold, "since last visit," "kill-criterion hits only," "pinned only." Filters are additive and persisted per thesis.

**Review actions:** accept, reject, defer (with optional reason and resurface rule), pin, add note. *Defer is not a graveyard* — it is a queue that resurfaces when a related cluster forms or when importance crosses a threshold. The user picks the resurface rule when deferring; defaults are sensible (resurface in 7 days or on related cluster of importance > 0.6).

**Stance override:** clicking the stance pill opens a small popover with all four stances and a free-text rationale. Override is captured separately from accept/reject. The system records both the LLM's original verdict and the user's correction for training-signal purposes.

**When rejecting,** the user picks a reason from a short menu (off-topic, wrong company, too speculative, stance wrong, low quality). Required, not optional. Rejection reasons roll up into the source quality dashboard.

**Hotkeys:** j/k navigate, a/r/d accept/reject/defer, p pin, n note, s override stance, c expand cluster, enter open right-rail drill, ? cheat sheet. The current mouse-only interaction is the single biggest source of triage friction.

A small **"new since last visit" pill** appears on rows added since the user last opened the thesis. The pill clears on open. This is the daily-driver hook that converts the workspace from "I should check this" into "I open this every morning."

## Cluster drill (right rail)

Today the cluster reference is a truncated string with no action. The drill panel shows: every member article ranked by source tier with publisher, headline, age, link; the cluster's dominant tier and member count; the extracted event with full slot breakdown if extraction has run; the market reaction snapshot (price move, relative volume, reaction score) if captured; the LLM's stance rationale citing specific article IDs; and an option to attach this cluster manually to other theses.

Stance rationale citations are clickable — each cited article ID highlights and scrolls into view in the article list above. This is the personal-pipeline analog of Tegus's citation paragraphs.

## Conviction trajectory

The single feature that turns this from queue into instrument.

A **sparkline on the thesis detail header** showing conviction over time, with the y-axis the posterior probability the thesis is borne out and the x-axis the life of the thesis. Annotations on the line mark the evidence events that moved it materially; a tooltip on each annotation shows the cluster headline, stance, importance, and the Bayes-factor contribution.

Below the sparkline: the prior, the running posterior, the count of accepted supports and contradictions, and the source-weight-weighted balance. A "snapshot now" action freezes the current state to the snapshot store; auto-snapshots happen daily.

A **"what's moved this" panel** under the sparkline shows the top three evidence items that have most affected conviction in the last seven days, by Bayes-factor magnitude. Clicking any item jumps the queue to that row.

The math is documented in plain language in a "how this is computed" disclosure below the sparkline. No hidden formulas. The Bayes factor for an evidence item is a function of stance (sign), confidence (magnitude), source weight (multiplier), novelty (multiplier), and importance (multiplier). Posterior = prior × product of accepted Bayes factors. Rejected and deferred evidence does not contribute. Override stances replace LLM verdicts in the math.

## Catalysts tab

A timeline of upcoming and past catalysts for the thesis. **Past catalysts** show: pre-registered expectation, actual outcome (a structured event the user pinned or the system auto-matched), the conviction movement attributable to the catalyst, and any user note. **Upcoming catalysts** show: date, label, class, pre-registered expectation, and a countdown. Catalysts within seven days appear as cards on the thesis detail header as well, not just inside the tab.

When a catalyst date hits, the system surfaces a **"what happened?" prompt** forcing the user to record the outcome before reading new evidence about it. This is the structural defense against retroactive rationalization. The user can pick from clusters in a date window or write a free-form outcome note. Outcome above expectation, in line, or below expectation each generate a Bayes-factor contribution; magnitude is recorded.

EDGAR scheduled filings and FRED release calendars feed candidate catalysts at thesis creation; the user picks which to register. Earnings dates pulled from the events stream supplement.

## Kill criteria tab

The kill criteria registered at thesis creation, with a similarity-hit log under each one. Every cluster matching a kill criterion above threshold appears here, with date, headline, stance, and an "acknowledge" action. Kill-criterion hits also surface in the queue with a distinctive treatment — a red border and pinned position above the strongest-contradictions rail.

The pre-mortem section sits below kill criteria with the same hit-log structure.

When a kill criterion fires three or more times in a thirty-day window, the system suggests promoting the thesis to "watching" or "invalidated" status with a one-click action. It does not auto-promote.

## Linked theses tab

Other theses sharing concepts, asset keywords, or evidence with this one. Surfaces three structural patterns: thesis pairs sharing an asset, thesis pairs whose concept embeddings are close (cosine above threshold), and thesis pairs where the same cluster is accepted in both with conflicting stances. The third is rare and structurally interesting; it gets a distinct treatment.

Clicking a linked thesis opens a side-by-side compare mode: two conviction trajectories, two evidence streams, common clusters in the middle, divergent points called out.

## Snapshots tab and personal calibration

The snapshots tab lists every conviction snapshot for the thesis with date, conviction reading, evidence count, top supporting cluster, top contradicting cluster, and a delta from the previous snapshot. Snapshots are taken daily and on user demand.

When a thesis closes (status moves to validated, invalidated, or expired), the conviction trajectory is preserved and added to the **personal calibration page** — a separate top-level page, not inside a thesis. The calibration page shows: a calibration plot (for theses where the user was N% confident at close, did N% resolve true), a Brier score over time, breakdowns by thesis class (earnings / macro / regulatory / etc.) and by asset class, and a "where you are well-calibrated / where you are not" summary. This is the long-arc payoff of the workspace; it only becomes meaningful after several closed theses, but it should be built early so the data accumulates.

## Weekly digest

A weekly LLM-generated digest per active thesis: a one-screen brief covering accepted-supports and accepted-contradictions of the week, conviction movement, biggest cluster, upcoming catalysts within fourteen days, kill-criterion hit count, and suggested actions ("the consensus seems to be moving away from variant; consider snapshotting and re-examining"). One Qwen call per active thesis per week. Surfaced as a dismissable banner on the thesis detail and accumulated in a Briefs tab.

## Generative search

A natural-language search across the user's accepted-evidence corpus, scoped by thesis or global. "What evidence have I accepted about copper supply this quarter?" Returns a synthesis with cluster citations. Embedding retrieval plus a constrained Qwen call. Lives in the global header as a command palette opened with cmd-k.

## Snippet capture

A paste-box at the bottom of the queue accepts a URL or pasted text, fetches the URL if applicable, runs it through embedding and triage, and proposes attaching it to the current thesis as evidence with a Qwen-classified stance. Closes the gap when the matcher misses something or when the user wants to attach a podcast transcript, a tweet, or a paragraph from a paywalled source. Manual evidence is tagged distinctly so it does not pollute the source-weight posterior.

## Source quality dashboard

A sub-tab on the thesis. Per-publisher accept-vs-reject ratios on this thesis, with confidence intervals; the numbers are noisy at low N and the UI says so. A separate global page aggregates across all theses and feeds back into the source-weight posterior used by the importance calculator. The static source tier becomes a prior; the user's clicks become the likelihood; the personal posterior is what drives ranking. This closes the loop that the static-tier system today only half-builds.

## Status semantics

Today: draft / active / paused / archived.

Proposed: draft / active / watching / paused / validated / invalidated / archived. *Watching* means the thesis is paused but kill criteria and catalysts are still scanning. *Validated* and *invalidated* are terminal states triggered by user action; they freeze the snapshot history and feed calibration.

The frontend type contract already includes watching, validated, and invalidated as recognized statuses; they are not yet rendered or set anywhere.

## Vocabulary consistency

The Inbox and Pipeline views establish patterns the Research workspace should reuse: chips for scope filters, dropdowns for enumerations, sliders for thresholds, source-tier dots, sentiment dots, importance bars, tabular numerics with two decimals, ages as relative time. Buttons follow the existing height and border conventions. The redesigned Research workspace adds: the conviction sparkline component, the source-tier × stance heatmap, the catalyst timeline strip, the right-rail drill panel, the hotkey overlay, and the calibration plot.

## Phasing

The redesign breaks into five phases. Each ships independently and improves the workspace meaningfully on its own.

**Phase 0 — surface what's already there.** Render the matcher reason and similarity score, the cluster member list (cluster drill in right rail), tags, related events, the pin and rationale fields. Replace the hardcoded watchlist with the actual asset linkage. Wire up rule editing post-creation. Wire up the manual-attach endpoint as a paste-box. Add hotkeys to the queue. Rename the Scan button to reflect that the matcher already runs continuously and the button is a wider-lookback rescan. No backend changes; pure UI surfacing of capabilities that already exist. This phase removes the "bare-bones" feel immediately and is a prerequisite for confident later work.

**Phase 1 — assisted stance and conviction trajectory.** Wire Qwen stance pre-classification into the matcher pipeline; render the stance verdict and rationale on every evidence row. Define and document the Bayes-factor formulation; back-fill historical snapshots from the existing accept/reject log. Build the sparkline component, the "what's moved this" panel, and the snapshot history tab. This phase is the spine; everything later produces real numbers only because of it.

**Phase 2 — falsification structure.** Add consensus / variant view fields to the thesis form. Add kill criteria and pre-mortem fields with separate vector storage and a separate matcher pathway. Build the kill criteria tab. Surface kill-criterion hits with distinctive treatment in the queue.

**Phase 3 — catalysts and calibration.** Add catalyst registration to the thesis form. Wire EDGAR and FRED release calendars as catalyst suggestions. Build the catalysts tab and the date-arrival prompt. Add the validated/invalidated terminal statuses with the close flow. Build the personal calibration page.

**Phase 4 — synthesis.** Weekly digests, generative search, source quality dashboard, defer-with-resurface, stale-thesis nudge, cross-thesis link view, side-by-side thesis compare, smart-synonym keyword expansion.

## Backend touchpoints (deferred)

The UX above implies the following backend work, to be specified at implementation time:

- A stance classification call inside the existing matcher pipeline returning stance, confidence, and a rationale string per evidence row, alongside the existing match metadata.
- A conviction computation routine taking the per-thesis evidence log and emitting a posterior plus a per-evidence Bayes-factor contribution. Triggered on review and on a daily schedule.
- A snapshot-write routine, daily and on demand, persisting to the existing snapshot store with conviction reading and contributing-evidence references.
- New thesis fields: consensus view, variant view, kill criteria array, pre-mortem causes array, initial conviction prior. Each free-text field embedded as a 1024-dim vector at save time.
- A thesis-catalyst entity with date, class, label, pre-registered expectation, support/contradict pair, outcome reference, outcome note. A catalyst-suggestions endpoint pulling EDGAR filing windows and FRED release schedules.
- A defer-resurface rule on the evidence record: resurface trigger (related cluster / importance threshold / time elapsed) and a resurface check in the matcher loop.
- A per-publisher accept/reject ledger feeding the source-weight posterior.
- A cluster detail endpoint returning members, dominant tier, extracted event, market snapshot, and the stance rationale's article citations.
- A weekly-digest job per active thesis writing to a brief store and surfacing in the UI.
- A generative search endpoint backed by pgvector retrieval and a constrained Qwen call.
- A calibration aggregation endpoint computing Brier score, calibration bins, and breakdowns from closed-thesis snapshots.

Schema details — exact columns, indexes, unique constraints, migration order — are decided at implementation time based on the matcher and snapshot patterns already in the codebase.

## Out of scope

No multi-user features. No social or sharing. No portfolio integration or dollar-amount position sizing. No real-time price action UI (the market snapshot at event time is sufficient). No mobile-first design — this is a desktop research tool. No fine-tuned domain stance classifier in this plan; zero-shot Qwen with the constrained-JSON pattern already used elsewhere is the floor, and a domain fine-tune is a future path once enough labeled stance overrides exist.

## Why this design

The current page is shaped like a CRM contact list applied to news articles. The proposed design is shaped like a working analyst's notebook with a Bayesian belief tracker bolted on — closer to Bridgewater's dot collector, AlphaSense's research grid, and Roam's discourse graph than to any consumer fintech product. That is appropriate: the cost structure of running this system (a 27B local LLM, a custom triage sidecar, primary-tier ingestion) only makes sense if the workspace pays back the spend with research-instrument-grade ergonomics, not prettier news cards.

The biggest single bet in the plan is conviction-as-a-tracked-quantity. Everything else is downstream of it — kill criteria are valuable because they move conviction, catalysts are valuable because they move conviction, per-publisher posteriors matter because they refine conviction. Build that spine first; the limbs follow.
