import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { ArrowLeft, ArrowRight, ExternalLink, Loader2, Sparkles } from 'lucide-react'
import {
  createExploration,
  fetchClusterDetail,
  fetchThesis,
  promoteThesis,
  type ClusterDetailDto,
  type ThesisDetailDto,
  type ThesisLeaning,
  type ThesisCoverage,
  type ThesisPlanDto,
} from '../../lib/api'
import { fmtAge, sourceLabel } from '../../lib/format'
import { SourceTierLabel } from '../../components/SourceTier'
import { EventTypeBadge } from '../../components/EventTypeBadge'
import { cn } from '../../lib/cn'

type Stage = 'compose' | 'running' | 'result' | 'error'

const LEANING_META: Record<string, { label: string; cls: string }> = {
  supports:     { label: 'corpus leans supportive',    cls: 'text-positive' },
  contradicts:  { label: 'corpus leans against',       cls: 'text-negative' },
  mixed:        { label: 'corpus is mixed',            cls: 'text-accent' },
  insufficient: { label: 'corpus signal insufficient', cls: 'text-faint' },
}

const COVERAGE_META: Record<string, { label: string; cls: string }> = {
  thick:    { label: 'thick coverage',    cls: 'text-positive' },
  moderate: { label: 'moderate coverage', cls: 'text-accent' },
  thin:     { label: 'thin coverage',     cls: 'text-faint' },
}

function leaningMeta(l: ThesisLeaning) { return LEANING_META[l] ?? LEANING_META.insufficient }
function coverageMeta(c: ThesisCoverage) { return COVERAGE_META[c] ?? COVERAGE_META.thin }

function VerdictParagraph({ verdict, plan }: { verdict: string; plan: ThesisPlanDto }) {
  const supportSet = useMemo(() => new Set(plan.strongestSupportClusterIds), [plan.strongestSupportClusterIds])
  const contradictSet = useMemo(() => new Set(plan.strongestContradictClusterIds), [plan.strongestContradictClusterIds])
  const totalCited = supportSet.size + contradictSet.size
  return (
    <div className="space-y-3">
      <p className="text-[14px] leading-relaxed text-fg whitespace-pre-line">{verdict}</p>
      {totalCited > 0 && (
        <p className="text-[10.5px] tabular text-faint">
          citations resolve to {plan.strongestSupportClusterIds.length} supporting, {plan.strongestContradictClusterIds.length} contradicting clusters
        </p>
      )}
    </div>
  )
}

function CitationCard({
  clusterId,
  stance,
}: {
  clusterId: string
  stance: 'supports' | 'contradicts'
}) {
  const [data, setData] = useState<ClusterDetailDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  useEffect(() => {
    const ac = new AbortController()
    fetchClusterDetail(clusterId, ac.signal)
      .then(setData)
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Failed to load')
      })
    return () => ac.abort()
  }, [clusterId])

  const lead = data?.members[0]
  const ev = data?.extractedEvent
  const headline = lead?.headline ?? ev?.summary ?? '—'
  const cls = stance === 'supports' ? 'border-l-positive/60' : 'border-l-negative/60'

  if (error) {
    return (
      <div className={cn('rounded-sm border border-line-soft bg-surface/40 px-3 py-2.5', cls, 'border-l-2')}>
        <div className="text-[11px] text-faint">cluster failed to load: {error}</div>
      </div>
    )
  }

  if (!data) {
    return (
      <div className={cn('rounded-sm border border-line-soft bg-surface/40 px-3 py-2.5', cls, 'border-l-2 pulse-soft')}>
        <div className="text-[11px] text-faint">loading citation…</div>
      </div>
    )
  }

  return (
    <div className={cn('rounded-sm border border-line-soft bg-surface/40 px-3 py-2.5', cls, 'border-l-2')}>
      <div className="flex flex-wrap items-center gap-1.5">
        {ev?.eventType && <EventTypeBadge type={ev.eventType} />}
        {data.dominantSourceTier && <SourceTierLabel tier={data.dominantSourceTier} />}
        {data.symbol && <span className="text-[10.5px] font-medium tabular text-fg">{data.symbol}</span>}
        {lead?.source && (
          <span className="text-[9.5px] tracking-[0.085em] uppercase text-muted">{sourceLabel(lead.source)}</span>
        )}
        <span className="ml-auto text-[10px] tabular text-faint">
          {data.memberCount} src · {fmtAge(data.lastSeenAt)}
        </span>
      </div>
      <div className="mt-1 text-[12.5px] leading-snug text-fg line-clamp-3">{headline}</div>
      {lead?.url && (
        <a
          href={lead.url}
          target="_blank"
          rel="noreferrer"
          onClick={e => e.stopPropagation()}
          className="mt-1 inline-flex items-center gap-1 text-[10.5px] text-faint hover:text-fg"
        >
          source <ExternalLink className="size-3" />
        </a>
      )}
    </div>
  )
}

function SubTrackPreview({ plan }: { plan: ThesisPlanDto }) {
  if (plan.subTracks.length === 0) return null
  return (
    <Section title={`If promoted, this would track ${plan.subTracks.length} levers`}>
      <ul className="space-y-1.5">
        {plan.subTracks.map(s => (
          <li key={s.name} className="flex flex-wrap items-baseline gap-x-2 gap-y-0.5 text-[12px]">
            <span className="font-medium text-fg">{s.name}</span>
            <span className="min-w-0 flex-1 text-[11.5px] leading-snug text-muted">{s.question}</span>
          </li>
        ))}
      </ul>
    </Section>
  )
}

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-b border-line-soft px-4 py-4 last:border-b-0 sm:px-6 sm:py-5">
      <h3 className="mb-3 text-[11px] tracking-[0.1em] uppercase text-dim">{title}</h3>
      {children}
    </section>
  )
}

function ResultView({
  detail,
  onPromote,
  onBack,
  promoting,
  promoteError,
}: {
  detail: ThesisDetailDto
  onPromote: () => void
  onBack: () => void
  promoting: boolean
  promoteError: string | null
}) {
  const plan = detail.plan
  if (!plan) {
    return (
      <Section title="Result">
        <div className="text-[12px] text-faint">No plan generated.</div>
      </Section>
    )
  }
  const lm = leaningMeta(plan.leaning)
  const cm = coverageMeta(plan.coverage)
  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <header className="sticky top-0 z-10 border-b border-line bg-surface px-4 py-4 sm:px-6 sm:py-5">
        <button
          onClick={onBack}
          className="mb-2 inline-flex h-7 items-center gap-1 rounded-sm px-1.5 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
        >
          <ArrowLeft className="size-3" />
          <span>Explore</span>
        </button>
        <div className="flex items-start gap-3">
          <div className="min-w-0 flex-1">
            <div className="flex flex-wrap items-center gap-2">
              <span className="inline-flex h-5 items-center rounded-sm border border-accent/40 bg-accent-soft px-1.5 text-[10px] tracking-[0.06em] uppercase text-accent">
                exploration
              </span>
              <span className={cn('text-[10.5px] tracking-[0.08em] uppercase', lm.cls)}>{lm.label}</span>
              <span className={cn('text-[10.5px] tracking-[0.08em] uppercase', cm.cls)}>· {cm.label}</span>
            </div>
            <h2 className="mt-2 text-[15px] font-medium leading-snug">{detail.title}</h2>
            <p className="mt-1.5 max-w-3xl text-[12px] leading-relaxed text-muted">{detail.statement}</p>
          </div>
        </div>
      </header>

      <Section title="Verdict">
        <VerdictParagraph verdict={plan.verdict} plan={plan} />
      </Section>

      {plan.strongestSupportClusterIds.length > 0 && (
        <Section title={`Strongest support (${plan.strongestSupportClusterIds.length})`}>
          <div className="space-y-2">
            {plan.strongestSupportClusterIds.map(id => (
              <CitationCard key={id} clusterId={id} stance="supports" />
            ))}
          </div>
        </Section>
      )}

      {plan.strongestContradictClusterIds.length > 0 && (
        <Section title={`Strongest contradiction (${plan.strongestContradictClusterIds.length})`}>
          <div className="space-y-2">
            {plan.strongestContradictClusterIds.map(id => (
              <CitationCard key={id} clusterId={id} stance="contradicts" />
            ))}
          </div>
        </Section>
      )}

      <SubTrackPreview plan={plan} />

      <Section title="Decision">
        <div className="space-y-3">
          <div className="text-[12px] leading-relaxed text-muted">
            Promoting starts continuous tracking. The system will route incoming evidence into each lever, classify stance, and update the briefing as the corpus grows.
            Discarding leaves no trace beyond this exploration.
          </div>
          {promoteError && <div className="text-[11.5px] text-negative">{promoteError}</div>}
          <div className="flex flex-wrap items-center gap-2">
            <button
              onClick={onPromote}
              disabled={promoting}
              className="inline-flex h-8 items-center gap-1.5 rounded-sm border border-accent/60 bg-accent-soft px-3 text-[12px] text-fg transition hover:bg-accent/15 disabled:opacity-50"
            >
              {promoting ? <Loader2 className="size-3.5 animate-spin" /> : <ArrowRight className="size-3.5" />}
              <span>{promoting ? 'Promoting' : 'Promote to thesis'}</span>
            </button>
            <button
              onClick={onBack}
              disabled={promoting}
              className="inline-flex h-8 items-center gap-1.5 rounded-sm border border-line bg-canvas px-3 text-[12px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
            >
              Back
            </button>
          </div>
        </div>
      </Section>
    </div>
  )
}

export function ExplorationScreen({
  initialText,
  onCancel,
  onPromoted,
}: {
  initialText?: string
  onCancel: () => void
  onPromoted: (thesisId: string) => void
}) {
  const [stage, setStage] = useState<Stage>('compose')
  const [text, setText] = useState(initialText ?? '')
  const [error, setError] = useState<string | null>(null)
  const [explorationId, setExplorationId] = useState<string | null>(null)
  const [detail, setDetail] = useState<ThesisDetailDto | null>(null)
  const [promoting, setPromoting] = useState(false)
  const [promoteError, setPromoteError] = useState<string | null>(null)

  const handleRun = async () => {
    const trimmed = text.trim()
    if (trimmed.length < 30) {
      setError('Write at least a sentence describing what you think might be true.')
      return
    }
    setError(null)
    setStage('running')
    try {
      const res = await createExploration({ thesisText: trimmed })
      setExplorationId(res.explorationId)
      const d = await fetchThesis(res.explorationId)
      setDetail(d)
      setStage('result')
    } catch (e) {
      setError(e instanceof Error ? e.message : 'Exploration failed')
      setStage('error')
    }
  }

  const handlePromote = async () => {
    if (!explorationId) return
    setPromoting(true)
    setPromoteError(null)
    try {
      await promoteThesis(explorationId)
      onPromoted(explorationId)
    } catch (e) {
      setPromoteError(e instanceof Error ? e.message : 'Promotion failed')
      setPromoting(false)
    }
  }

  if (stage === 'result' && detail) {
    return (
      <ResultView
        detail={detail}
        onPromote={handlePromote}
        onBack={() => { setStage('compose'); setExplorationId(null); setDetail(null) }}
        promoting={promoting}
        promoteError={promoteError}
      />
    )
  }

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <header className="sticky top-0 z-10 border-b border-line bg-surface px-4 py-4 sm:px-6 sm:py-5">
        <button
          onClick={onCancel}
          className="mb-2 inline-flex h-7 items-center gap-1 rounded-sm px-1.5 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
        >
          <ArrowLeft className="size-3" />
          <span>Research</span>
        </button>
        <div className="flex items-center gap-2">
          <Sparkles className="size-4 text-accent" />
          <h2 className="text-[16px] font-medium leading-snug">Explore a thesis</h2>
        </div>
        <p className="mt-1 max-w-2xl text-[12px] leading-relaxed text-muted">
          Paste a hypothesis. The system reads it against your corpus and tells you whether it's worth tracking, or whether the signal is too thin to bother.
          Nothing is committed until you promote.
        </p>
      </header>

      <Section title="Hypothesis">
        <textarea
          value={text}
          onChange={e => setText(e.target.value)}
          rows={6}
          disabled={stage === 'running'}
          placeholder="e.g. Persistent copper supply constraints and demand growth from grid investment will push prices materially higher over 12-24 months despite cyclical demand softness in China."
          className="w-full resize-y rounded-sm border border-line bg-canvas px-3 py-2 text-[13px] leading-relaxed text-fg outline-none focus:border-accent/70 disabled:opacity-50"
        />
        {error && <div className="mt-2 text-[11.5px] text-negative">{error}</div>}
        <div className="mt-3 flex flex-wrap items-center gap-2">
          <button
            onClick={handleRun}
            disabled={stage === 'running'}
            className="inline-flex h-8 items-center gap-1.5 rounded-sm border border-accent/60 bg-accent-soft px-3 text-[12px] text-fg transition hover:bg-accent/15 disabled:opacity-50"
          >
            {stage === 'running'
              ? <Loader2 className="size-3.5 animate-spin" />
              : <Sparkles className="size-3.5" />}
            <span>{stage === 'running' ? 'Reading the corpus' : 'Explore'}</span>
          </button>
          <button
            onClick={onCancel}
            disabled={stage === 'running'}
            className="inline-flex h-8 items-center gap-1.5 rounded-sm border border-line bg-canvas px-3 text-[12px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
          >
            Cancel
          </button>
        </div>
      </Section>

      {stage === 'running' && (
        <Section title="Working">
          <div className="space-y-2 text-[11.5px] leading-relaxed text-muted">
            <div className="flex items-center gap-2">
              <Loader2 className="size-3.5 animate-spin text-accent" />
              <span>Embedding the hypothesis and pulling the most semantically related clusters from the corpus.</span>
            </div>
            <div className="text-[10.5px] text-faint">First read takes about 30-90 seconds on the local model. Don't navigate away.</div>
          </div>
        </Section>
      )}
    </div>
  )
}
