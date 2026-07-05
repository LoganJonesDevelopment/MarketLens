import { useEffect, useMemo, useState } from 'react'
import { ChevronDown, ChevronRight, ExternalLink } from 'lucide-react'
import { cn } from '../../lib/cn'
import { fmtAge, fmtImportance, fmtSigned, sourceLabel } from '../../lib/format'
import {
  fetchWeeklyOpen, fetchClusterDetail, fetchThesisEvidence, fetchMarketOverview,
  type MarketOverviewWeeklyPerformance,
  type MarketOverviewWeeklyPerformanceItem,
  type WeeklyOpenDto, type WeeklyOpenQuote, type WeeklyOpenCluster, type WeeklyOpenThesis, type WeeklyOpenCalendarEntry,
  type WeeklyOpenWindowPreset,
  type ClusterDetailDto, type ThesisEvidenceDto,
} from '../../lib/api'
import { EventTypeBadge } from '../../components/EventTypeBadge'
import { SourceTierLabel } from '../../components/SourceTier'
import { ImportanceBar } from '../../components/ImportanceBar'

const QUOTE_LABELS: Record<string, string> = {
  SPY: 'S&P', QQQ: 'Nasdaq', IWM: 'Russell', DIA: 'Dow', VXX: 'VIX',
  'I:SPX': 'S&P', 'I:NDX': 'Nasdaq', 'I:RUT': 'Russell', 'I:DJI': 'Dow', 'I:VIX': 'VIX',
  'ES=F': 'S&P', 'NQ=F': 'Nasdaq', 'RTY=F': 'Russell', 'YM=F': 'Dow', '^VIX': 'VIX',
}

const QUOTE_ORDER = ['SPY','QQQ','IWM','DIA','VXX','I:SPX','I:NDX','I:RUT','I:DJI','I:VIX','ES=F','NQ=F','RTY=F','YM=F','^VIX']

const WINDOW_OPTIONS: Array<{ value: WeeklyOpenWindowPreset; label: string; hint: string }> = [
  { value: 'auto', label: 'Auto', hint: 'Smart default for the day and time' },
  { value: '1d', label: '1d', hint: 'Last 24 hours' },
  { value: '3d', label: '3d', hint: 'Last 3 days' },
  { value: '1w', label: '1w', hint: 'Last 7 days' },
  { value: '1m', label: '1m', hint: 'Last 30 days' },
  { value: 'since-friday', label: 'Since Fri', hint: 'Since the most recent Friday close' },
]

const STORAGE_KEY = 'marketlens.brief.window'

function loadInitialWindow(): WeeklyOpenWindowPreset {
  if (typeof window === 'undefined') return 'auto'
  const stored = window.localStorage.getItem(STORAGE_KEY)
  if (!stored) return 'auto'
  if (WINDOW_OPTIONS.some(o => o.value === stored)) return stored as WeeklyOpenWindowPreset
  return 'auto'
}

export function OpenView() {
  const [data, setData] = useState<WeeklyOpenDto | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [refreshAt, setRefreshAt] = useState<Date>(new Date())
  const [preset, setPreset] = useState<WeeklyOpenWindowPreset>(() => loadInitialWindow())
  const [loading, setLoading] = useState(false)
  const [weeklyPerformance, setWeeklyPerformance] = useState<MarketOverviewWeeklyPerformance | null>(null)
  const [weeklyPerformanceError, setWeeklyPerformanceError] = useState<string | null>(null)
  const [weeklyPerformanceLoading, setWeeklyPerformanceLoading] = useState(false)

  useEffect(() => {
    if (typeof window !== 'undefined') window.localStorage.setItem(STORAGE_KEY, preset)
    let cancelled = false
    const ctrl = new AbortController()
    const load = (silent: boolean) => {
      if (!silent) setLoading(true)
      fetchWeeklyOpen({ window: preset, topClusters: 10 }, ctrl.signal)
        .then(d => {
          if (cancelled) return
          setData(d); setError(null); setRefreshAt(new Date())
        })
        .catch(err => {
          if (cancelled || err.name === 'AbortError') return
          setError(String(err.message ?? err))
        })
        .finally(() => { if (!cancelled) setLoading(false) })
    }
    load(false)
    const id = window.setInterval(() => load(true), 90_000)
    return () => { cancelled = true; ctrl.abort(); window.clearInterval(id) }
  }, [preset])

  useEffect(() => {
    let cancelled = false
    const ctrl = new AbortController()
    const load = (silent: boolean) => {
      if (!silent) setWeeklyPerformanceLoading(true)
      fetchMarketOverview(ctrl.signal)
        .then(d => {
          if (cancelled) return
          setWeeklyPerformance(d.weeklyPerformance ?? null)
          setWeeklyPerformanceError(null)
        })
        .catch(err => {
          if (cancelled || err.name === 'AbortError') return
          setWeeklyPerformanceError(String(err.message ?? err))
        })
        .finally(() => { if (!cancelled) setWeeklyPerformanceLoading(false) })
    }
    load(false)
    const id = window.setInterval(() => load(true), 90_000)
    return () => { cancelled = true; ctrl.abort(); window.clearInterval(id) }
  }, [])

  if (error && !data) {
    return (
      <div className="p-4 text-sm text-negative">
        Failed to load brief: {error}
      </div>
    )
  }

  if (!data) {
    return <div className="p-4 text-sm text-dim">Loading…</div>
  }

  return (
    <div className="flex h-full flex-col overflow-y-auto">
      <FuturesStrip quotes={data.quotes} />
      <Header
        data={data}
        refreshAt={refreshAt}
        preset={preset}
        onPresetChange={setPreset}
        loading={loading}
      />
      <WeeklyPerformanceSection
        weeklyPerformance={weeklyPerformance}
        loading={weeklyPerformanceLoading}
        error={weeklyPerformanceError}
      />
      <Section
        title="Top moves"
        subtitle={`${data.clusters.length} clusters ranked by importance × activity in window`}
      >
        {data.clusters.length === 0 ? (
          <Empty text="No event clusters in this window. Either nothing material happened, or the pipeline hasn't drained — check the freshness pill above." />
        ) : (
          <ul className="divide-y divide-line-soft/70">
            {data.clusters.map(c => <ClusterRow key={c.clusterId} cluster={c} />)}
          </ul>
        )}
      </Section>
      <Section
        title="Theses with new evidence"
        subtitle={`${data.theses.length} theses moved · sorted by lean change`}
      >
        {data.theses.length === 0 ? (
          <Empty text="No new evidence on any active thesis in this window." />
        ) : (
          <ul className="divide-y divide-line-soft/70">
            {data.theses.map(t => <ThesisRow key={t.id} thesis={t} sinceUtc={data.windowStart} />)}
          </ul>
        )}
      </Section>
      <Section
        title="Ahead"
        subtitle={`Next ${data.calendarHorizonDays}d of earnings and macro releases`}
      >
        {data.calendar.length === 0 ? (
          <Empty text="No scheduled events in this horizon." />
        ) : (
          <ul className="divide-y divide-line-soft/70">
            {data.calendar.map(e => <CalendarRow key={e.id} entry={e} />)}
          </ul>
        )}
      </Section>
    </div>
  )
}

function formatCompactDate(value: string | null | undefined) {
  if (!value) return null
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) return null
  const sameYear = date.getFullYear() === new Date().getFullYear()
  return date.toLocaleDateString(undefined, sameYear
    ? { month: 'short', day: 'numeric' }
    : { month: 'short', day: 'numeric', year: 'numeric' })
}

function closeLabel(value: number | null | undefined) {
  if (value == null) return '—'
  return value.toLocaleString(undefined, { maximumFractionDigits: value >= 1000 ? 1 : 2 })
}

function performanceTone(value: number | null | undefined) {
  if (value == null) return 'text-dim'
  if (value > 0.005) return 'text-positive'
  if (value < -0.005) return 'text-negative'
  return 'text-dim'
}

function WeeklyPerformanceSection({
  weeklyPerformance,
  loading,
  error,
}: {
  weeklyPerformance: MarketOverviewWeeklyPerformance | null
  loading: boolean
  error: string | null
}) {
  const groups = (weeklyPerformance?.groups ?? []).filter(g => (g.items ?? []).length > 0)
  const movers = weeklyPerformance?.topMovers?.filter(Boolean) ?? []
  const start = formatCompactDate(weeklyPerformance?.windowStart)
  const end = formatCompactDate(weeklyPerformance?.windowEnd)
  const subtitle = start && end ? `${start} - ${end}` : start ? `since ${start}` : undefined

  return (
    <Section title="Week across assets" subtitle={subtitle}>
      {loading && !weeklyPerformance ? (
        <div className="px-3 py-4 text-[12px] text-dim sm:px-5">Loading…</div>
      ) : error && !weeklyPerformance ? (
        <div className="px-3 py-4 text-[12px] text-negative sm:px-5">Market overview unavailable: {error}</div>
      ) : groups.length === 0 ? (
        <div className="px-3 py-4 text-[12px] text-dim sm:px-5">No weekly performance data available.</div>
      ) : (
        <div className="px-3 py-3 sm:px-5">
          <div className="grid gap-3 xl:grid-cols-[minmax(0,1fr)_220px]">
            <div className="min-w-0 space-y-3">
              {groups.map(group => (
                <div key={group.key} className="min-w-0">
                  <div className="mb-1.5 text-[10px] font-medium uppercase tracking-wider text-faint">{group.label}</div>
                  <div className="grid gap-px overflow-hidden rounded-sm border border-line-soft bg-line-soft sm:grid-cols-2 lg:grid-cols-4">
                    {(group.items ?? []).map(item => (
                      <WeeklyPerformanceCell key={`${group.key}-${item.symbol}`} item={item} />
                    ))}
                  </div>
                </div>
              ))}
            </div>
            <WeeklyPerformanceMovers items={movers} />
          </div>
          {(weeklyPerformance?.warnings?.length ?? 0) > 0 && (
            <div className="mt-2 truncate text-[10.5px] text-faint" title={weeklyPerformance?.warnings?.join(' · ')}>
              {weeklyPerformance?.warnings?.[0]}
            </div>
          )}
        </div>
      )}
    </Section>
  )
}

function WeeklyPerformanceCell({ item }: { item: MarketOverviewWeeklyPerformanceItem }) {
  return (
    <div
      className={cn(
        'min-w-0 bg-canvas px-2.5 py-2',
        item.stale && 'bg-surface/60',
      )}
      title={item.warning ?? item.status ?? undefined}
    >
      <div className="flex items-start justify-between gap-2">
        <div className="min-w-0">
          <div className="truncate text-[12px] font-medium text-fg">{item.label || item.symbol}</div>
          <div className="text-[10px] uppercase text-faint">{item.symbol}</div>
        </div>
        <div className={cn('shrink-0 text-right text-[13px] font-medium tabular', performanceTone(item.returnPct))}>
          {item.returnPct == null ? '—' : `${fmtSigned(item.returnPct, 2)}%`}
        </div>
      </div>
      <div className="mt-1 flex items-center justify-between gap-2 text-[10.5px] tabular text-dim">
        <span>{closeLabel(item.startClose)}</span>
        <span className="text-faint">to</span>
        <span>{closeLabel(item.lastClose)}</span>
      </div>
      {(item.stale || item.warning || item.asOf) && (
        <div className="mt-1 truncate text-[10px] text-faint">
          {item.warning ?? (item.stale ? 'stale' : item.asOf ? fmtAge(item.asOf) : '')}
        </div>
      )}
    </div>
  )
}

function WeeklyPerformanceMovers({ items }: { items: MarketOverviewWeeklyPerformanceItem[] }) {
  if (items.length === 0) return null
  return (
    <div className="min-w-0">
      <div className="mb-1.5 text-[10px] font-medium uppercase tracking-wider text-faint">Top movers</div>
      <div className="divide-y divide-line-soft border border-line-soft bg-canvas">
        {items.slice(0, 6).map(item => (
          <div key={item.symbol} className="grid grid-cols-[52px_1fr_auto] items-center gap-2 px-2.5 py-1.5">
            <div className="font-medium tabular text-[11.5px] text-fg">{item.symbol}</div>
            <div className="min-w-0 truncate text-[11px] text-dim">{item.label}</div>
            <div className={cn('text-right text-[11.5px] font-medium tabular', performanceTone(item.returnPct))}>
              {item.returnPct == null ? '—' : `${fmtSigned(item.returnPct, 2)}%`}
            </div>
          </div>
        ))}
      </div>
    </div>
  )
}

function FuturesStrip({ quotes }: { quotes: WeeklyOpenQuote[] }) {
  const sorted = [...quotes].sort((a, b) => {
    const ai = QUOTE_ORDER.indexOf(a.symbol); const bi = QUOTE_ORDER.indexOf(b.symbol)
    return (ai === -1 ? 99 : ai) - (bi === -1 ? 99 : bi)
  })
  const allUnconfigured = sorted.length > 0 && sorted.every(q => q.status === 'unconfigured')
  if (allUnconfigured) {
    return (
      <div className="sticky top-0 z-10 border-b border-line bg-line-soft/40 px-3 py-2 text-[11.5px] text-dim sm:px-5">
        Live quotes off — set <span className="font-medium text-fg">POLYGON_API_KEY</span> (or <span className="font-mono text-fg">dotnet user-secrets set Polygon:ApiKey</span>) and restart the API.
      </div>
    )
  }
  return (
    <div
      className="sticky top-0 z-10 flex overflow-x-auto border-b border-line bg-line-soft/40 backdrop-blur"
    >
      {sorted.map(q => <QuoteCell key={q.symbol} q={q} />)}
    </div>
  )
}

function QuoteCell({ q }: { q: WeeklyOpenQuote }) {
  const label = QUOTE_LABELS[q.symbol] ?? q.symbol
  const pct = q.changePercent
  const dir = pct == null ? 'flat' : pct > 0.005 ? 'up' : pct < -0.005 ? 'down' : 'flat'
  const dirClass = dir === 'up' ? 'text-positive' : dir === 'down' ? 'text-negative' : 'text-dim'
  return (
    <div className="w-[86px] shrink-0 border-r border-line-soft/70 bg-canvas px-2.5 py-1.5 text-left sm:w-[96px]">
      <div className="truncate text-[9.5px] uppercase text-dim">{label}</div>
      <div className="mt-0.5 truncate text-[12.5px] font-medium tabular text-fg">
        {q.last == null ? '—' : q.last.toLocaleString(undefined, { maximumFractionDigits: 2 })}
      </div>
      <div className={cn('text-[10.5px] leading-tight tabular', dirClass)}>
        {pct == null ? '—' : `${fmtSigned(pct, 2)}%`}
      </div>
      {q.delayed && (
        <div className="text-[9px] uppercase tracking-wide text-faint" title={`${q.provider ?? 'fallback'} · ${q.asOf ? fmtAge(q.asOf) : 'unknown'}`}>
          delayed
        </div>
      )}
    </div>
  )
}

function describeWindow(data: WeeklyOpenDto): string {
  const start = new Date(data.windowStart)
  if (data.windowKind === 'duration') {
    const hours = Math.round(data.lookbackHours)
    if (hours < 36) return `last ${hours}h`
    const days = Math.max(1, Math.round(data.lookbackHours / 24))
    return `last ${days}d`
  }
  const sameYear = start.getFullYear() === new Date().getFullYear()
  const dateLabel = start.toLocaleString(undefined, sameYear
    ? { weekday: 'short', month: 'short', day: 'numeric', hour: 'numeric' }
    : { weekday: 'short', month: 'short', day: 'numeric', year: 'numeric', hour: 'numeric' })
  return `since ${dateLabel}`
}

function Header({
  data, refreshAt, preset, onPresetChange, loading,
}: {
  data: WeeklyOpenDto
  refreshAt: Date
  preset: WeeklyOpenWindowPreset
  onPresetChange: (p: WeeklyOpenWindowPreset) => void
  loading: boolean
}) {
  const ext = data.pipelineFreshness.find(f => f.stage === 'event_extraction')
  const matcher = data.pipelineFreshness.find(f => f.stage === 'research_matcher')
  const stale = (s: { latestStartedAt: string } | undefined) => {
    if (!s) return true
    const ageMin = (refreshAt.getTime() - new Date(s.latestStartedAt).getTime()) / 60_000
    return ageMin > 15
  }
  const anyStale = stale(ext) || stale(matcher)
  const label = useMemo(() => describeWindow(data), [data])

  return (
    <div className="border-b border-line bg-canvas px-3 py-2 sm:px-5">
      <div className="flex items-center justify-between gap-2">
        <div className="flex items-center gap-2 text-[12px] text-dim">
          <span className="text-fg font-medium">{label}</span>
          {preset === 'auto' && (
            <span className="rounded bg-line-soft/60 px-1 py-0.5 text-[9.5px] uppercase tracking-wider text-faint">auto</span>
          )}
          {loading && <span className="text-faint text-[10.5px]">refreshing…</span>}
        </div>
        <div className="flex items-center gap-2 text-[10px] uppercase tracking-wider">
          <span className={cn('inline-flex items-center gap-1', anyStale ? 'text-negative' : 'text-dim')}>
            <span className={cn('size-1.5 rounded-full', anyStale ? 'bg-negative' : 'bg-positive')} />
            {anyStale ? 'pipeline stale' : 'pipeline fresh'}
          </span>
          <span className="text-faint">· refreshed {fmtAge(refreshAt.toISOString())}</span>
        </div>
      </div>
      <div className="mt-2 flex flex-wrap items-center gap-1">
        {WINDOW_OPTIONS.map(opt => (
          <button
            key={opt.value}
            type="button"
            onClick={() => onPresetChange(opt.value)}
            title={opt.hint}
            className={cn(
              'rounded px-2 py-0.5 text-[11px] tabular transition',
              preset === opt.value
                ? 'bg-fg text-canvas'
                : 'bg-line-soft/40 text-dim hover:bg-line-soft/70 hover:text-fg',
            )}
          >
            {opt.label}
          </button>
        ))}
      </div>
    </div>
  )
}

function Section({ title, subtitle, children }: { title: string; subtitle?: string; children: React.ReactNode }) {
  return (
    <section className="border-b border-line">
      <header className="bg-surface/40 px-3 py-2 sm:px-5">
        <h2 className="text-[12.5px] font-medium text-fg">{title}</h2>
        {subtitle && <div className="text-[11px] text-dim">{subtitle}</div>}
      </header>
      {children}
    </section>
  )
}

function Empty({ text }: { text: string }) {
  return <div className="px-3 py-6 text-[12.5px] text-dim sm:px-5">{text}</div>
}

function ClusterRow({ cluster }: { cluster: WeeklyOpenCluster }) {
  const [open, setOpen] = useState(false)
  const [detail, setDetail] = useState<ClusterDetailDto | null>(null)
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')

  useEffect(() => {
    if (!open || state !== 'idle') return
    setState('loading')
    fetchClusterDetail(cluster.clusterId)
      .then(d => { setDetail(d); setState('done') })
      .catch(() => setState('error'))
  }, [open, state, cluster.clusterId])

  const ratioPct = Math.round((cluster.activityRatio ?? 1) * 100)
  const activityLabel = cluster.firstSeenInWindow
    ? 'new in window'
    : `${cluster.articlesInWindow}/${cluster.memberCount} in window`
  const topSourceLabel = cluster.topSource
    ? cluster.topSource.publisher ?? sourceLabel(cluster.topSource.source)
    : null

  return (
    <li>
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        className="block w-full px-3 py-3 text-left transition hover:bg-surface/40 sm:px-5"
      >
        <div className="flex items-center gap-2">
          <div className="w-12 shrink-0 text-[12.5px] font-medium tabular text-fg">{cluster.symbol ?? '—'}</div>
          <EventTypeBadge type={cluster.eventType} />
          {cluster.firstSeenInWindow && (
            <span className="rounded bg-accent/15 px-1 py-0.5 text-[9.5px] uppercase tracking-wider text-accent">new</span>
          )}
          <div className="ml-auto flex items-center gap-2">
            <ImportanceBar components={cluster.components} />
            <span className="tabular text-[11.5px] text-dim">{fmtImportance(cluster.importance)}</span>
            {open ? <ChevronDown className="size-3.5 text-faint" /> : <ChevronRight className="size-3.5 text-faint" />}
          </div>
        </div>
        <div className="mt-1.5 line-clamp-2 text-[13.5px] leading-snug text-fg">{cluster.summary}</div>
        <div className="mt-1 flex items-center gap-2 text-[11px] text-dim">
          <SourceTierLabel tier={cluster.sourceTier} />
          {topSourceLabel && <span className="truncate">· {topSourceLabel}</span>}
          <span className="ml-auto tabular">
            {activityLabel}{!cluster.firstSeenInWindow && ratioPct < 100 && ` (${ratioPct}%)`} · {fmtAge(cluster.lastSeenAt)}
          </span>
        </div>
      </button>
      {open && (
        <div className="border-t border-line-soft/70 bg-surface/30 px-3 py-2 sm:px-5">
          {state === 'loading' && <div className="text-[11px] text-dim">Loading sources…</div>}
          {state === 'error' && <div className="text-[11px] text-negative">Failed to load sources.</div>}
          {state === 'done' && detail && detail.members.length === 0 && <div className="text-[11px] text-dim">No member articles linked.</div>}
          {state === 'done' && detail && detail.members.length > 0 && (
            <ul className="space-y-1.5">
              {detail.members.slice(0, 6).map(m => (
                <li key={m.id} className="flex items-start gap-2 text-[12px] leading-snug">
                  <SourceTierLabel tier={m.sourceTier} />
                  <div className="min-w-0 flex-1">
                    {m.url ? (
                      <a
                        href={m.url}
                        target="_blank"
                        rel="noreferrer"
                        className="text-fg underline-offset-2 hover:underline"
                      >
                        {m.headline}
                        <ExternalLink className="ml-1 inline size-3 text-faint" />
                      </a>
                    ) : (
                      <span className="text-fg">{m.headline}</span>
                    )}
                    <div className="text-[10.5px] text-dim">
                      {m.publisher ?? m.source}{' · '}{fmtAge(m.publishedAt)}
                    </div>
                  </div>
                </li>
              ))}
            </ul>
          )}
        </div>
      )}
    </li>
  )
}

function ThesisRow({ thesis, sinceUtc }: { thesis: WeeklyOpenThesis; sinceUtc: string }) {
  const [open, setOpen] = useState(false)
  const [evidence, setEvidence] = useState<ThesisEvidenceDto[] | null>(null)
  const [state, setState] = useState<'idle' | 'loading' | 'done' | 'error'>('idle')
  const lean = thesis.leanDelta
  const leanClass = lean > 0 ? 'text-positive' : lean < 0 ? 'text-negative' : 'text-dim'
  const newLeanTotal = thesis.priorLean + thesis.leanDelta

  useEffect(() => {
    if (!open || state !== 'idle') return
    setState('loading')
    fetchThesisEvidence(thesis.id)
      .then(rows => {
        const cutoff = new Date(sinceUtc).getTime()
        const filtered = rows
          .filter(r => r.observedAt && new Date(r.observedAt).getTime() >= cutoff)
          .sort((a, b) => {
            if (a.isPinned !== b.isPinned) return a.isPinned ? -1 : 1
            const ai = a.importance ?? 0; const bi = b.importance ?? 0
            if (ai !== bi) return bi - ai
            return new Date(b.observedAt ?? 0).getTime() - new Date(a.observedAt ?? 0).getTime()
          })
          .slice(0, 6)
        setEvidence(filtered)
        setState('done')
      })
      .catch(() => setState('error'))
  }, [open, state, thesis.id, sinceUtc])

  return (
    <li>
      <button
        type="button"
        onClick={() => setOpen(o => !o)}
        className="block w-full px-3 py-3 text-left transition hover:bg-surface/40 sm:px-5"
      >
        <div className="flex items-center gap-2">
          {thesis.primarySymbol && (
            <div className="w-12 shrink-0 text-[12.5px] font-medium tabular text-fg">{thesis.primarySymbol}</div>
          )}
          <div className="min-w-0 flex-1 truncate text-[13.5px] font-medium text-fg">{thesis.name}</div>
          {open ? <ChevronDown className="size-3.5 text-faint" /> : <ChevronRight className="size-3.5 text-faint" />}
        </div>
        <div className="mt-1.5 flex items-center gap-3 text-[11px] tabular">
          <span className="text-positive">+{thesis.newSupports} sup</span>
          <span className="text-negative">−{thesis.newContradicts} con</span>
          <span className="text-dim">·{thesis.newNeutral + thesis.newUnknown} other</span>
          {thesis.newPinned > 0 && <span className="text-accent">★ {thesis.newPinned}</span>}
          <span className={cn('ml-auto', leanClass)} title={`prior lean ${thesis.priorLean >= 0 ? '+' : ''}${thesis.priorLean} → ${newLeanTotal >= 0 ? '+' : ''}${newLeanTotal}`}>
            {lean > 0 ? `+${lean}` : lean} lean
          </span>
        </div>
        {thesis.topNew?.headline && !open && (
          <div className="mt-1.5 line-clamp-2 text-[12.5px] leading-snug text-dim">
            {thesis.topNew.isPinned && <span className="text-accent">★ </span>}
            {thesis.topNew.headline}
          </div>
        )}
      </button>
      {open && (
        <div className="border-t border-line-soft/70 bg-surface/30 px-3 py-2 sm:px-5">
          {state === 'loading' && <div className="text-[11px] text-dim">Loading evidence…</div>}
          {state === 'error' && <div className="text-[11px] text-negative">Failed to load evidence.</div>}
          {state === 'done' && evidence && evidence.length === 0 && <div className="text-[11px] text-dim">No new evidence in this window.</div>}
          {state === 'done' && evidence && evidence.length > 0 && (
            <ul className="space-y-2">
              {evidence.map(e => <EvidenceItem key={e.id} ev={e} />)}
            </ul>
          )}
        </div>
      )}
    </li>
  )
}

function EvidenceItem({ ev }: { ev: ThesisEvidenceDto }) {
  const stance = ev.stance ?? 'unknown'
  const stanceClass =
    stance === 'supports' ? 'text-positive' :
    stance === 'contradicts' ? 'text-negative' :
    stance === 'neutral' ? 'text-dim' : 'text-faint'
  const stanceLabel =
    stance === 'supports' ? 'sup' :
    stance === 'contradicts' ? 'con' :
    stance === 'neutral' ? 'neu' : '?'
  const headline = ev.headline ?? ev.summary ?? ev.segmentText ?? ev.chunkText ?? '(no text)'
  const url = ev.url ?? ev.audioUrl ?? ev.chunkFilingUrl ?? null
  const sourceLabel = ev.publisher ?? ev.source ?? ev.sourceTier ?? null
  return (
    <li className="flex items-start gap-2 text-[12px] leading-snug">
      {ev.isPinned && <span className="text-accent">★</span>}
      <span className={cn('shrink-0 w-7 text-right tabular text-[10.5px]', stanceClass)}>{stanceLabel}</span>
      <div className="min-w-0 flex-1">
        {url ? (
          <a href={url} target="_blank" rel="noreferrer" className="text-fg underline-offset-2 hover:underline">
            {headline}
            <ExternalLink className="ml-1 inline size-3 text-faint" />
          </a>
        ) : (
          <span className="text-fg">{headline}</span>
        )}
        <div className="text-[10.5px] text-dim">
          {ev.symbol && <span className="font-medium">{ev.symbol} · </span>}
          {sourceLabel && <span>{sourceLabel} · </span>}
          {ev.observedAt && <span>{fmtAge(ev.observedAt)}</span>}
          {ev.stanceRationale && (
            <span className="text-faint"> — {ev.stanceRationale.length > 180 ? ev.stanceRationale.slice(0, 180) + '…' : ev.stanceRationale}</span>
          )}
        </div>
      </div>
    </li>
  )
}

function CalendarRow({ entry }: { entry: WeeklyOpenCalendarEntry }) {
  const when = new Date(entry.scheduledAt)
  const day = when.toLocaleDateString(undefined, { weekday: 'short', month: 'short', day: 'numeric' })
  const time = entry.isTimeSpecific
    ? when.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
    : null
  return (
    <li className="px-3 py-2.5 sm:px-5">
      <div className="flex items-center gap-2">
        {entry.symbol && (
          <div className="w-12 shrink-0 text-[12.5px] font-medium tabular text-fg">{entry.symbol}</div>
        )}
        <div className="min-w-0 flex-1 truncate text-[13px] text-fg">{entry.label}</div>
        <div className="ml-auto shrink-0 text-right text-[11px] tabular text-dim">
          <div>{day}</div>
          {time && <div className="text-faint">{time}</div>}
        </div>
      </div>
    </li>
  )
}
