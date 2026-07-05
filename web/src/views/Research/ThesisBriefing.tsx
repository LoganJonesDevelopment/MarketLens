import { useMemo, useState, type ReactNode } from 'react'
import { ArrowLeft, ExternalLink, Settings2, ListFilter, Loader2, RefreshCw } from 'lucide-react'
import {
  type ThesisDetailDto,
  type ThesisDto,
  type ThesisEvidenceDto,
  type ThesisPlanDto,
  type ThesisSubTrackDto,
} from '../../lib/api'
import { fmtAge } from '../../lib/format'
import { cn } from '../../lib/cn'
import { ThesisTradingSection } from './ThesisTradingSection'

function fmtTimestamp(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}:${s.toString().padStart(2, '0')}`
}

const TIER_WEIGHT: Record<string, number> = {
  primary: 1.0,
  wire: 0.85,
  trade_press: 0.7,
  ir_feed: 0.85,
  aggregator: 0.45,
  opinion: 0.3,
}

function tierWeight(tier?: string | null): number {
  return TIER_WEIGHT[tier ?? 'aggregator'] ?? 0.5
}

function evidenceMagnitude(e: ThesisEvidenceDto): number {
  const conf = e.stanceConfidence ?? 0
  const tier = tierWeight(e.sourceTier)
  const importance = e.importance ?? 0.3
  return conf * (0.4 + 0.6 * tier) * (0.5 + 0.5 * importance)
}

function matchesSubTrack(e: ThesisEvidenceDto, sub: ThesisSubTrackDto): boolean {
  const haystack = `${e.headline ?? ''} ${e.summary ?? ''} ${e.symbol ?? ''} ${e.eventType ?? ''} ${e.segmentText ?? ''} ${e.chunkText ?? ''} ${e.chunkSection ?? ''} ${e.chunkFilingSymbol ?? ''}`.toLowerCase()
  const has = (term: string) => term.length > 0 && haystack.includes(term.toLowerCase())
  if (sub.assetTerms.some(has)) return true
  if (sub.conceptTerms.some(has)) return true
  if (sub.eventTypes.length > 0 && e.eventType && sub.eventTypes.some(t => t.toLowerCase() === e.eventType!.toLowerCase())) return true
  return false
}

type SubTrackAggregate = {
  sub: ThesisSubTrackDto
  matched: ThesisEvidenceDto[]
  classifiedCount: number
  supports: number
  contradicts: number
  neutral: number
  net: number
  topEvidence: ThesisEvidenceDto[]
  status: SubTrackStatus
  hasSignal: boolean
}

type SubTrackStatus = 'on_track' | 'wobbling' | 'against' | 'noise' | 'awaiting' | 'classifying'

function aggregateSubTrack(sub: ThesisSubTrackDto, all: ThesisEvidenceDto[]): SubTrackAggregate {
  const matched = all.filter(e => matchesSubTrack(e, sub))
  const classified = matched.filter(e => e.classifiedAt != null)
  const supports = classified.filter(e => e.stance === 'supports')
  const contradicts = classified.filter(e => e.stance === 'contradicts')
  const neutral = classified.filter(e => e.stance === 'neutral')
  const supportMag = supports.reduce((s, e) => s + evidenceMagnitude(e), 0)
  const contradictMag = contradicts.reduce((s, e) => s + evidenceMagnitude(e), 0)
  const net = supportMag - contradictMag
  const expectsConfirm = sub.expectedDirection === 'confirms_if'
  const expectsContradict = sub.expectedDirection === 'contradicts_if'

  let status: SubTrackStatus
  if (matched.length === 0) {
    status = 'awaiting'
  } else if (classified.length === 0) {
    status = 'classifying'
  } else if (Math.abs(net) < 0.15) {
    status = neutral.length >= classified.length / 2 ? 'noise' : 'wobbling'
  } else if (net > 0) {
    status = expectsContradict ? 'against' : 'on_track'
  } else {
    status = expectsConfirm ? 'against' : 'on_track'
  }

  const rank = (e: ThesisEvidenceDto) => {
    const conf = e.stanceConfidence ?? 0
    const tier = tierWeight(e.sourceTier)
    const importance = e.importance ?? 0.3
    return conf * (0.5 + 0.5 * tier) * (0.5 + 0.5 * importance) + (e.isPinned ? 1 : 0)
  }

  const topEvidence = [...matched]
    .sort((a, b) => rank(b) - rank(a))
    .slice(0, 3)

  return {
    sub,
    matched,
    classifiedCount: classified.length,
    supports: supports.length,
    contradicts: contradicts.length,
    neutral: neutral.length,
    net,
    topEvidence,
    status,
    hasSignal: classified.length > 0,
  }
}

function statusMeta(status: SubTrackStatus): { label: string; cls: string; dotCls: string } {
  switch (status) {
    case 'on_track':    return { label: 'on track',     cls: 'text-positive',         dotCls: 'bg-positive' }
    case 'against':     return { label: 'against',      cls: 'text-negative',         dotCls: 'bg-negative' }
    case 'wobbling':    return { label: 'mixed',        cls: 'text-accent',           dotCls: 'bg-accent' }
    case 'noise':       return { label: 'mostly noise', cls: 'text-muted',            dotCls: 'bg-line' }
    case 'classifying': return { label: 'classifying',  cls: 'text-trust',            dotCls: 'bg-trust pulse-soft' }
    case 'awaiting':    return { label: 'no matches',   cls: 'text-faint',            dotCls: 'bg-line-soft' }
  }
}

function Section({ title, action, children, dense }: { title: string; action?: ReactNode; children: ReactNode; dense?: boolean }) {
  return (
    <section className={cn(
      'border-b border-line-soft last:border-b-0',
      dense ? 'px-3 py-2 sm:px-4' : 'px-3 py-2.5 sm:px-5 sm:py-3',
    )}>
      <div className="mb-1.5 flex items-center gap-3">
        <h3 className="text-[10px] tracking-[0.1em] uppercase text-dim">{title}</h3>
        {action && <div className="ml-auto">{action}</div>}
      </div>
      {children}
    </section>
  )
}

function SubTrackPanel({
  agg,
  onOpenEvidence,
}: {
  agg: SubTrackAggregate
  onOpenEvidence: (id: string, subTrackName: string) => void
}) {
  const meta = statusMeta(agg.status)
  const [expanded, setExpanded] = useState(false)
  return (
    <div className="rounded-sm border border-line-soft bg-surface/30">
      <button
        onClick={() => setExpanded(e => !e)}
        className="flex w-full cursor-pointer items-center gap-2 px-2.5 py-2 text-left transition hover:bg-surface/60 active:bg-surface/80"
      >
        <span className={cn('size-1.5 shrink-0 rounded-full', meta.dotCls)} />
        <span className="min-w-0 flex-1 break-words text-[12.5px] font-medium leading-snug text-fg">{agg.sub.name}</span>
        <span className="shrink-0 text-[10px] tabular text-faint">
          {agg.status === 'classifying'
            ? `${agg.classifiedCount}/${agg.matched.length}`
            : agg.matched.length}
        </span>
        {agg.hasSignal && (
          <span className="shrink-0 font-mono text-[10px] tabular">
            {agg.supports > 0 && <span className="text-positive">+{agg.supports}</span>}
            {agg.supports > 0 && agg.contradicts > 0 && <span className="text-faint"> </span>}
            {agg.contradicts > 0 && <span className="text-negative">−{agg.contradicts}</span>}
          </span>
        )}
        <span className={cn('shrink-0 text-[9.5px] tracking-[0.08em] uppercase tabular', meta.cls)}>
          {meta.label}
        </span>
      </button>
      {expanded && (
        <div className="border-t border-line-soft px-2.5 pb-2 pt-1.5">
          <p className="text-[11.5px] leading-snug text-muted">{agg.sub.question}</p>
          {(agg.sub.assetTerms.length > 0 || agg.sub.conceptTerms.length > 0) && (
            <div className="mt-1.5 flex flex-wrap gap-1">
              {agg.sub.assetTerms.slice(0, 8).map(t => (
                <span key={`a-${t}`} className="inline-flex h-4 items-center rounded-sm border border-line bg-surface-2/60 px-1 text-[9.5px] tabular text-fg">
                  {t}
                </span>
              ))}
              {agg.sub.conceptTerms.slice(0, 8).map(t => (
                <span key={`c-${t}`} className="inline-flex h-4 items-center rounded-sm border border-line-soft bg-surface-2/40 px-1 text-[9.5px] tabular text-muted">
                  {t}
                </span>
              ))}
            </div>
          )}
          {agg.topEvidence.length > 0 ? (
            <div className="mt-2 space-y-1">
              {agg.topEvidence.map(e => (
                <EvidenceRow key={e.id} evidence={e} onOpen={id => onOpenEvidence(id, agg.sub.name)} />
              ))}
            </div>
          ) : (
            <div className="mt-1.5 text-[10.5px] text-faint">no matches yet</div>
          )}
        </div>
      )}
    </div>
  )
}

function EvidenceRow({
  evidence,
  onOpen,
}: {
  evidence: ThesisEvidenceDto
  onOpen: (id: string) => void
}) {
  const stanceCls =
    evidence.stance === 'supports'    ? 'text-positive' :
    evidence.stance === 'contradicts' ? 'text-negative' :
    evidence.stance === 'neutral'     ? 'text-muted' :
                                        'text-faint'
  const stanceMark =
    evidence.stance === 'supports' ? '+' :
    evidence.stance === 'contradicts' ? '−' :
    '·'

  const isSegment = evidence.evidenceKind === 'segment'
  const isChunk = evidence.evidenceKind === 'chunk'

  if (isChunk) {
    const text = evidence.chunkText ?? ''
    const displayText = text.length > 220 ? text.slice(0, 220).trimEnd() + '...' : text
    const symbol = evidence.chunkFilingSymbol ?? evidence.symbol
    const form = evidence.chunkFilingForm
    const section = evidence.chunkSection
    const label = [form, section, symbol].filter(Boolean).join(' · ')
    const href = evidence.chunkFilingUrl ?? undefined

    return (
      <div className="flex w-full items-start gap-2 rounded-sm px-1.5 py-1">
        <span className={cn('mt-0.5 shrink-0 font-mono text-[11px] leading-none tabular', stanceCls)}>
          {stanceMark}
        </span>
        <button
          onClick={() => onOpen(evidence.id)}
          className="min-w-0 flex-1 cursor-pointer text-left hover:opacity-80"
        >
          <span className="block line-clamp-2 text-[11.5px] leading-snug text-fg">{displayText || '(filing chunk)'}</span>
          {label && <span className="mt-0.5 block line-clamp-1 text-[10px] text-trust">{label}</span>}
        </button>
        <div className="flex shrink-0 flex-col items-end gap-0.5">
          {evidence.observedAt && (
            <span className="text-[10px] tabular text-faint">{fmtAge(evidence.observedAt)}</span>
          )}
          {href && (
            <a
              href={href}
              target="_blank"
              rel="noopener noreferrer"
              onClick={e => e.stopPropagation()}
              className="inline-flex items-center gap-0.5 text-[10px] text-trust hover:text-fg"
              title="Open filing"
            >
              <ExternalLink className="size-2.5" />
            </a>
          )}
        </div>
      </div>
    )
  }

  if (isSegment) {
    const text = evidence.segmentText ?? ''
    const displayText = text.length > 220 ? text.slice(0, 220).trimEnd() + '...' : text
    const timestamp = evidence.startSeconds != null ? fmtTimestamp(evidence.startSeconds) : null
    const audioHref = evidence.audioUrl != null && evidence.startSeconds != null
      ? `${evidence.audioUrl}#t=${Math.floor(evidence.startSeconds)}`
      : evidence.audioUrl ?? undefined

    return (
      <div className="flex w-full items-start gap-2 rounded-sm px-1.5 py-1">
        <span className={cn('mt-0.5 shrink-0 font-mono text-[11px] leading-none tabular', stanceCls)}>
          {stanceMark}
        </span>
        <button
          onClick={() => onOpen(evidence.id)}
          className="min-w-0 flex-1 cursor-pointer text-left hover:opacity-80"
        >
          <span className="block line-clamp-2 text-[11.5px] leading-snug text-fg">{displayText || '(segment)'}</span>
          <span className="mt-0.5 flex items-center gap-1.5">
            {timestamp && (
              <span className="font-mono text-[10px] tabular text-trust">{timestamp}</span>
            )}
            {evidence.speaker && (
              <span className="text-[10px] text-muted">{evidence.speaker}</span>
            )}
          </span>
        </button>
        <div className="flex shrink-0 flex-col items-end gap-0.5">
          {evidence.observedAt && (
            <span className="text-[10px] tabular text-faint">{fmtAge(evidence.observedAt)}</span>
          )}
          {audioHref && (
            <a
              href={audioHref}
              target="_blank"
              rel="noopener noreferrer"
              onClick={e => e.stopPropagation()}
              className="inline-flex items-center gap-0.5 text-[10px] text-trust hover:text-fg"
              title="Open audio at timestamp"
            >
              <ExternalLink className="size-2.5" />
            </a>
          )}
        </div>
      </div>
    )
  }

  const headline = evidence.headline ?? evidence.summary ?? 'cluster'
  return (
    <button
      onClick={() => onOpen(evidence.id)}
      className="flex w-full cursor-pointer items-start gap-2 rounded-sm px-1.5 py-1 text-left transition hover:bg-surface/60 active:bg-surface/80"
    >
      <span className={cn('mt-0.5 shrink-0 font-mono text-[11px] leading-none tabular', stanceCls)}>
        {stanceMark}
      </span>
      <span className="min-w-0 flex-1 line-clamp-2 text-[11.5px] leading-snug text-fg">{headline}</span>
      {evidence.publishedAt && (
        <span className="shrink-0 text-[10px] tabular text-faint">{fmtAge(evidence.publishedAt)}</span>
      )}
    </button>
  )
}

function PlanPendingState({
  thesis,
  detail,
  onRetry,
  retrying,
}: {
  thesis: ThesisDto | ThesisDetailDto
  detail: ThesisDetailDto | null
  onRetry: () => void
  retrying: boolean
}) {
  const isReady = detail?.plan != null
  if (isReady) return null
  const ageHint = thesis.createdAt ? fmtAge(thesis.createdAt) : null
  return (
    <Section title="Bootstrap">
      <div className="flex items-start gap-3">
        <Loader2 className={cn('mt-0.5 size-4 text-accent', !retrying && 'animate-spin')} />
        <div className="min-w-0 flex-1">
          <div className="text-[12.5px] text-fg">
            Researching the thesis against your corpus.
          </div>
          <div className="mt-1 text-[11px] leading-snug text-muted">
            Reading recent semantically related clusters and decomposing your statement into a tracking plan.
            This usually takes a minute or two on first run.
            {ageHint && <> Created {ageHint}.</>}
          </div>
          <button
            onClick={onRetry}
            disabled={retrying}
            className="mt-3 inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
          >
            <RefreshCw className={cn('size-3', retrying && 'animate-spin')} />
            {retrying ? 'Bootstrapping' : 'Run now'}
          </button>
        </div>
      </div>
    </Section>
  )
}

function TrackedEntities({ plan }: { plan: ThesisPlanDto }) {
  const [expanded, setExpanded] = useState(false)
  if (plan.trackedEntities.length === 0) return null
  return (
    <Section title={`Tracked (${plan.trackedEntities.length})`} dense>
      {!expanded ? (
        <button
          onClick={() => setExpanded(true)}
          className="flex w-full flex-wrap items-baseline gap-x-2 gap-y-1 text-left"
        >
          {plan.trackedEntities.map((e, idx) => (
            <span key={`${e.symbol ?? e.name}-${idx}`} className="text-[11.5px] tabular text-fg">
              {e.symbol ?? e.name}
            </span>
          ))}
          <span className="ml-auto text-[10px] text-faint">why?</span>
        </button>
      ) : (
        <ul className="space-y-1">
          {plan.trackedEntities.map((e, idx) => (
            <li key={`${e.symbol ?? e.name}-${idx}`} className="space-y-0.5">
              <div className="flex min-w-0 flex-wrap items-baseline gap-x-2 gap-y-0.5">
                {e.symbol && <span className="font-medium tabular text-[11.5px] text-fg">{e.symbol}</span>}
                <span className="min-w-0 break-words text-[11.5px] text-fg">{e.name}</span>
              </div>
              {e.rationale && (
                <div className="break-words text-[10.5px] leading-snug text-muted">{e.rationale}</div>
              )}
            </li>
          ))}
          <li>
            <button onClick={() => setExpanded(false)} className="text-[10px] text-faint hover:text-fg">
              collapse
            </button>
          </li>
        </ul>
      )}
    </Section>
  )
}

function SignalLists({ plan }: { plan: ThesisPlanDto }) {
  const [expanded, setExpanded] = useState(false)
  if (plan.confirmingSignals.length === 0 && plan.refutingSignals.length === 0) return null
  if (!expanded) {
    return (
      <Section title="Signals to watch" dense>
        <button onClick={() => setExpanded(true)} className="text-[11px] text-muted hover:text-fg">
          {plan.confirmingSignals.length} confirms · {plan.refutingSignals.length} contradicts · show
        </button>
      </Section>
    )
  }
  return (
    <Section
      title="Signals to watch"
      action={<button onClick={() => setExpanded(false)} className="text-[10px] text-faint hover:text-fg">hide</button>}
    >
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
        <ul className="space-y-0.5 text-[11px] leading-snug text-fg">
          {plan.confirmingSignals.map((s, i) => (
            <li key={i} className="flex gap-1"><span className="text-positive">+</span><span className="min-w-0 flex-1 break-words">{s}</span></li>
          ))}
        </ul>
        <ul className="space-y-0.5 text-[11px] leading-snug text-fg">
          {plan.refutingSignals.map((s, i) => (
            <li key={i} className="flex gap-1"><span className="text-negative">−</span><span className="min-w-0 flex-1 break-words">{s}</span></li>
          ))}
        </ul>
      </div>
    </Section>
  )
}

export function ThesisBriefing({
  thesis,
  detail,
  evidence,
  loading,
  onOpenEvidence,
  onOpenAllEvidence,
  onOpenConfigure,
  onBootstrap,
  bootstrapping,
  onBackToList,
}: {
  thesis: ThesisDto | null
  detail: ThesisDetailDto | null
  evidence: ThesisEvidenceDto[]
  loading: boolean
  onOpenEvidence: (id: string, subTrackName: string) => void
  onOpenAllEvidence: () => void
  onOpenConfigure: () => void
  onBootstrap: () => void
  bootstrapping: boolean
  onBackToList?: () => void
}) {
  const [statementExpanded, setStatementExpanded] = useState(false)
  const active = detail ?? thesis
  const lastEvidenceAt = detail?.lastEvidenceAt ?? thesis?.lastEvidenceAt ?? null
  const merged = useMemo(
    () => (evidence.length > 0 ? evidence : detail?.evidence ?? []),
    [detail?.evidence, evidence],
  )
  const plan = detail?.plan ?? null
  const aggregates = useMemo(
    () => plan ? plan.subTracks.map(s => aggregateSubTrack(s, merged)) : [],
    [plan, merged],
  )

  if (!active) {
    return (
      <div className="flex h-full items-center justify-center text-[12px] text-faint">
        Select a thesis
      </div>
    )
  }

  const totalEvidence = merged.length
  const pending = active.planStatus === 'pending' || (!plan && active.planStatus !== 'ready')
  const subTrackOnTrack = aggregates.filter(a => a.status === 'on_track').length
  const subTrackAgainst = aggregates.filter(a => a.status === 'against').length
  const subTrackWobbling = aggregates.filter(a => a.status === 'wobbling').length
  const subTrackClassifying = aggregates.filter(a => a.status === 'classifying').length
  const subTrackAwaiting = aggregates.filter(a => a.status === 'awaiting').length
  const totalUnclassified = aggregates.reduce((sum, a) => sum + (a.matched.length - a.classifiedCount), 0)
  const headerStatement = plan?.summary ?? active.statement ?? active.summary

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <header className="sticky top-0 z-10 border-b border-line bg-surface px-3 py-2 sm:px-5 sm:py-2.5">
        <div className="flex items-center gap-2">
          {onBackToList && (
            <button
              onClick={onBackToList}
              className="inline-flex h-6 shrink-0 items-center justify-center rounded-sm px-1 text-muted transition hover:bg-surface-2 hover:text-fg"
              aria-label="Back to theses"
            >
              <ArrowLeft className="size-3.5" />
            </button>
          )}
          {active.symbol && (
            <span className="shrink-0 text-[12.5px] font-medium tabular">{active.symbol}</span>
          )}
          <h2 className="min-w-0 flex-1 break-words text-[13.5px] font-medium leading-snug">{active.title}</h2>
          <button
            onClick={onOpenAllEvidence}
            className="inline-flex h-6 shrink-0 items-center gap-1 rounded-sm px-1.5 text-[10.5px] text-muted transition hover:bg-surface-2 hover:text-fg"
            title="Evidence list"
          >
            <ListFilter className="size-3" />
            <span className="tabular">{totalEvidence}</span>
          </button>
          <button
            onClick={onOpenConfigure}
            className="inline-flex h-6 shrink-0 items-center justify-center rounded-sm px-1 text-muted transition hover:bg-surface-2 hover:text-fg"
            title="Configure"
          >
            <Settings2 className="size-3" />
          </button>
          {plan && (
            <button
              onClick={onBootstrap}
              disabled={bootstrapping}
              className="inline-flex h-6 shrink-0 items-center justify-center rounded-sm px-1 text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
              title="Refresh plan"
            >
              <RefreshCw className={cn('size-3', bootstrapping && 'animate-spin')} />
            </button>
          )}
        </div>
        {headerStatement && (
          <button
            onClick={() => setStatementExpanded(s => !s)}
            className={cn(
              'mt-1 block w-full text-left text-[11.5px] leading-snug text-muted transition hover:text-fg',
              !statementExpanded && 'line-clamp-1',
            )}
          >
            {headerStatement}
          </button>
        )}
        <div className="mt-1 flex items-center gap-3 text-[10px] tabular text-faint">
          <span className="inline-flex items-center gap-1">
            <span className="rounded-sm bg-surface-2/60 px-1 tracking-[0.06em] uppercase">{active.status ?? 'active'}</span>
          </span>
          <span>last evidence {lastEvidenceAt ? fmtAge(lastEvidenceAt) : '—'}</span>
          {plan && (
            <span className="ml-auto inline-flex items-center gap-2">
              {subTrackOnTrack > 0 && <span className="text-positive">{subTrackOnTrack} on track</span>}
              {subTrackAgainst > 0 && <span className="text-negative">{subTrackAgainst} against</span>}
              {subTrackWobbling > 0 && <span className="text-accent">{subTrackWobbling} mixed</span>}
              {subTrackClassifying > 0 && (
                <span className="text-trust inline-flex items-center gap-1">
                  <span className="size-1 rounded-full bg-trust pulse-soft" />
                  classifying {totalUnclassified}
                </span>
              )}
              {subTrackAwaiting > 0 && <span>{subTrackAwaiting} no matches</span>}
            </span>
          )}
        </div>
      </header>

      {pending && (
        <PlanPendingState
          thesis={active}
          detail={detail}
          onRetry={onBootstrap}
          retrying={bootstrapping}
        />
      )}

      {plan && (
        <>
          <Section title={`Sub-tracks (${plan.subTracks.length})`}>
            {aggregates.length === 0 ? (
              <div className="text-[11.5px] text-faint">Plan has no sub-tracks.</div>
            ) : (
              <div className="space-y-1">
                {aggregates.map(agg => (
                  <SubTrackPanel
                    key={agg.sub.name}
                    agg={agg}
                    onOpenEvidence={onOpenEvidence}
                  />
                ))}
              </div>
            )}
          </Section>

          <TrackedEntities plan={plan} />
          <SignalLists plan={plan} />
        </>
      )}

      <ThesisTradingSection thesisId={active.id} />

      {loading && totalEvidence === 0 && !plan && (
        <div className="px-6 py-12 text-[12px] text-faint pulse-soft">Loading…</div>
      )}
    </div>
  )
}
