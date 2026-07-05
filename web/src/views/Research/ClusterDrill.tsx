import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { ExternalLink } from 'lucide-react'
import {
  fetchClusterDetail,
  type ClusterDetailDto,
  type ClusterMemberDto,
} from '../../lib/api'
import { SourceTierDot, SourceTierLabel } from '../../components/SourceTier'
import { EventTypeBadge } from '../../components/EventTypeBadge'
import { SentimentDot } from '../../components/SentimentDot'
import { importanceColor } from '../../components/ImportanceBar'
import { fmtAge, fmtSigned, sourceLabel } from '../../lib/format'
import { SlotRenderer } from '../Inbox/SlotRenderer'

const TIER_ORDER: Record<string, number> = {
  primary: 0, wire: 1, trade_press: 2, aggregator: 3, opinion: 4,
}

function isAbort(err: unknown): boolean {
  return (err as { name?: string }).name === 'AbortError'
}

function Section({
  title, action, children, subtle,
}: { title: string; action?: ReactNode; children: ReactNode; subtle?: boolean }) {
  return (
    <section className="border-b border-line-soft px-4 py-3 last:border-b-0">
      <div className="mb-2 flex items-center gap-2">
        <h3 className={subtle ? 'label-xs text-faint' : 'label-xs text-dim'}>{title}</h3>
        {action && <div className="ml-auto">{action}</div>}
      </div>
      {children}
    </section>
  )
}

function KV({ k, children }: { k: string; children: ReactNode }) {
  return (
    <div className="grid grid-cols-[110px_1fr] items-center gap-2 py-0.5">
      <div className="label-xs text-dim">{k}</div>
      <div className="text-[11.5px] text-fg">{children}</div>
    </div>
  )
}

function ImportanceRow({ label, hint, value }: { label: string; hint: string; value: number }) {
  return (
    <div className="grid grid-cols-[1fr_50px_30px] items-center gap-2 py-0.5">
      <div>
        <div className="text-[11px] text-fg">{label}</div>
        <div className="text-[9.5px] tracking-wide text-faint">{hint}</div>
      </div>
      <div className="h-[3px] w-full overflow-hidden rounded-full bg-line-soft">
        <div className="h-full bg-fg/55" style={{ width: `${(value * 100).toFixed(1)}%` }} />
      </div>
      <div className="text-right text-[11px] tabular text-fg">{value.toFixed(2)}</div>
    </div>
  )
}

function MarketBlock({ market }: { market: NonNullable<ClusterDetailDto['extractedEvent']>['market'] }) {
  if (!market) return null
  const move = market.movePercent
  const rel = market.relativeMovePercent
  const reaction = market.reactionScore
  return (
    <div className="space-y-0.5 tabular">
      <KV k="Last">
        <span className="font-mono">
          {market.lastPrice != null ? market.lastPrice.toFixed(2) : '—'}
          {market.previousClose != null && (
            <span className="ml-1.5 text-[10.5px] text-faint">prev {market.previousClose.toFixed(2)}</span>
          )}
        </span>
      </KV>
      <KV k="Move">
        <span className={
          move == null ? 'text-faint' : move > 0 ? 'text-positive' : move < 0 ? 'text-negative' : ''
        }>
          {move == null ? '—' : `${move.toFixed(2)}%`}
        </span>
      </KV>
      {rel != null && (
        <KV k={`vs ${market.benchmarkSymbol ?? 'bench'}`}>
          <span className={rel > 0 ? 'text-positive' : rel < 0 ? 'text-negative' : ''}>
            {rel.toFixed(2)}%
          </span>
        </KV>
      )}
      {market.relativeVolume != null && (
        <KV k="Rel volume"><span>{market.relativeVolume.toFixed(2)}×</span></KV>
      )}
      {reaction != null && (
        <KV k="Reaction"><span className="font-mono">{fmtSigned(reaction)}</span></KV>
      )}
      {market.isAfterHours && <KV k="Window"><span className="text-faint">after-hours</span></KV>}
      {market.isStale && <KV k="Quote"><span className="text-faint">stale</span></KV>}
    </div>
  )
}

function MembersList({ members }: { members: ClusterMemberDto[] }) {
  const sorted = useMemo(
    () => [...members].sort((a, b) => (TIER_ORDER[a.sourceTier] ?? 9) - (TIER_ORDER[b.sourceTier] ?? 9)),
    [members],
  )
  if (sorted.length === 0) {
    return <div className="text-[11.5px] text-faint">no member articles</div>
  }
  return (
    <ul className="space-y-2.5">
      {sorted.map(m => (
        <li key={m.id} className="flex items-start gap-2">
          <div className="pt-1">
            <SourceTierDot tier={m.sourceTier} />
          </div>
          <div className="min-w-0 flex-1">
            <div className="flex items-center gap-1.5">
              <span className="text-[10px] tracking-[0.085em] uppercase font-medium text-muted">
                {sourceLabel(m.source)}
              </span>
              <SourceTierLabel tier={m.sourceTier} />
              <span className="ml-auto text-[10px] tabular text-faint">{fmtAge(m.publishedAt)}</span>
            </div>
            {m.url ? (
              <a
                href={m.url}
                target="_blank"
                rel="noreferrer"
                onClick={e => e.stopPropagation()}
                className="mt-0.5 flex items-start gap-1 text-[11.5px] leading-snug text-fg hover:underline underline-offset-2"
              >
                <span className="line-clamp-3">{m.headline}</span>
                <ExternalLink className="mt-0.5 size-3 shrink-0 opacity-50" />
              </a>
            ) : (
              <div className="mt-0.5 line-clamp-3 text-[11.5px] leading-snug text-fg">{m.headline}</div>
            )}
            {m.publisher && (
              <div className="mt-0.5 text-[10.5px] text-faint">{m.publisher}</div>
            )}
          </div>
        </li>
      ))}
    </ul>
  )
}

export function ClusterDrill({ clusterId }: { clusterId: string }) {
  const [data, setData] = useState<ClusterDetailDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    setData(null)
    setError(null)
    setLoading(true)
    const ac = new AbortController()
    fetchClusterDetail(clusterId, ac.signal)
      .then(d => { setData(d); setLoading(false) })
      .catch(err => {
        if (isAbort(err)) return
        setError(err instanceof Error ? err.message : 'Request failed')
        setLoading(false)
      })
    return () => ac.abort()
  }, [clusterId])

  const rawSlots = data?.extractedEvent?.slots
  const slots = useMemo<Record<string, unknown> | null>(() => {
    if (!rawSlots) return null
    try {
      const parsed = JSON.parse(rawSlots)
      return parsed && typeof parsed === 'object' ? parsed as Record<string, unknown> : null
    } catch { return null }
  }, [rawSlots])

  if (loading && !data) {
    return <div className="px-4 py-6 text-[11.5px] text-faint pulse-soft">Loading cluster…</div>
  }
  if (error) {
    return <div className="px-4 py-6 text-[11.5px] text-negative">Cluster: {error}</div>
  }
  if (!data) return null

  const ev = data.extractedEvent

  return (
    <div className="text-fg">
      <header className="sticky top-0 z-10 border-b border-line bg-surface px-4 py-3">
        <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
          <span className="text-[13px] font-medium tabular">{data.symbol ?? '—'}</span>
          {ev?.eventType
            ? <EventTypeBadge type={ev.eventType} />
            : data.triageEventType && <EventTypeBadge type={data.triageEventType} />}
          {data.dominantSourceTier && <SourceTierLabel tier={data.dominantSourceTier} />}
          <span className="ml-auto text-[10.5px] tabular text-faint">
            {data.memberCount} {data.memberCount === 1 ? 'src' : 'srcs'}
          </span>
        </div>
        {ev?.summary && (
          <p className="mt-1.5 text-[12px] leading-snug text-fg">{ev.summary}</p>
        )}
        {!ev && (
          <p className="mt-1.5 text-[11.5px] leading-snug text-faint">
            no extracted event yet, triage{' '}
            {data.triageConfidence != null ? `${data.triageConfidence.toFixed(2)} conf` : 'pending'}
          </p>
        )}
      </header>

      {ev && (
        <Section title="Importance" action={
          <span
            className="text-[14px] font-medium tabular leading-none"
            style={{ color: importanceColor(ev.importance) }}
          >
            {ev.importance.toFixed(2)}
          </span>
        }>
          <div className="space-y-0.5">
            <ImportanceRow label="Source weight" hint="tier × publisher" value={ev.components.sourceWeight} />
            <ImportanceRow label="Novelty" hint="cluster age decay" value={ev.components.noveltyWeight} />
            <ImportanceRow label="Class prior" hint="category materiality" value={ev.components.eventClassPrior} />
            <ImportanceRow label="Magnitude" hint="event scale" value={ev.components.magnitudeSignal} />
          </div>
          <div className="mt-2 flex items-center gap-1.5 text-[10.5px] tabular text-faint">
            <SentimentDot value={ev.sentiment} size={5} />
            <span>sentiment {fmtSigned(ev.sentiment)}</span>
          </div>
        </Section>
      )}

      {ev?.market && (
        <Section title="Market reaction">
          <MarketBlock market={ev.market} />
        </Section>
      )}

      {ev && slots && Object.keys(slots).length > 0 && (
        <Section title="Slots">
          <SlotRenderer eventType={ev.eventType} slots={slots} />
        </Section>
      )}

      <Section title={`Members (${data.memberCount})`}>
        <MembersList members={data.members} />
      </Section>

      <Section title="Cluster" subtle>
        <KV k="First seen">
          <span title={data.firstSeenAt}>{fmtAge(data.firstSeenAt)} ago</span>
        </KV>
        <KV k="Last seen">
          <span title={data.lastSeenAt}>{fmtAge(data.lastSeenAt)} ago</span>
        </KV>
        {data.topSourceWeight != null && (
          <KV k="Top src wgt">
            <span className="tabular">{data.topSourceWeight.toFixed(2)}</span>
          </KV>
        )}
        {data.triageConfidence != null && (
          <KV k="Triage conf">
            <span className="tabular">{data.triageConfidence.toFixed(2)}</span>
          </KV>
        )}
      </Section>

      {ev && (
        <Section title="Provenance" subtle>
          <KV k="Extracted">
            <span title={ev.model.extractedAt}>{fmtAge(ev.model.extractedAt)} ago</span>
          </KV>
        </Section>
      )}
    </div>
  )
}
