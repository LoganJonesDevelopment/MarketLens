import { useCallback, useEffect, useMemo, useState, type MouseEvent, type ReactNode } from 'react'
import {
  Activity,
  AlertCircle,
  ArrowLeft,
  CalendarDays,
  CandlestickChart,
  CheckCircle2,
  ExternalLink,
  FileText,
  Gauge,
  PanelRightOpen,
  RefreshCw,
  Scale,
  ShieldAlert,
  X,
  TrendingDown,
  TrendingUp,
  UserCheck,
} from 'lucide-react'
import {
  fetchForwardIdeas,
  fetchForwardPipelines,
  fetchIdeaBrief,
  fetchIdeaEvidence,
  fetchIdeaMemo,
  refreshIdeaMemo,
  type ForwardIdeaItemDto,
  type ForwardIdeasDto,
  type ForwardPipelineCatalogDto,
  type IdeaBriefDto,
  type IdeaEvidenceDto,
  type IdeaMemoAction,
  type IdeaMemoClaim,
  type IdeaMemoDto,
  type IdeaOverpricing,
  type IdeaRadarItem,
  type IdeaScoutScores,
  type IdeaTopEvent,
} from '../../lib/api'
import { cn } from '../../lib/cn'
import { eventLabel, fmtAge, fmtCount, fmtImportance, fmtSigned, sourceLabel } from '../../lib/format'
import { EventTypeBadge } from '../../components/EventTypeBadge'
import { Select } from '../../components/Select'
import { SourceTierLabel } from '../../components/SourceTier'
import { useIsMobile } from '../../lib/useMediaQuery'

const STORAGE_PREFIX = 'ml-ideas-'

function usePersistedState<T>(key: string, fallback: T, parse?: (raw: string) => T): [T, (value: T | ((prev: T) => T)) => void] {
  const storageKey = STORAGE_PREFIX + key
  const [value, setValueRaw] = useState<T>(() => {
    try {
      const stored = localStorage.getItem(storageKey)
      if (stored === null) return fallback
      return parse ? parse(stored) : JSON.parse(stored) as T
    } catch { return fallback }
  })
  const setValue = useCallback((next: T | ((prev: T) => T)) => {
    setValueRaw(prev => {
      const resolved = typeof next === 'function' ? (next as (prev: T) => T)(prev) : next
      try { localStorage.setItem(storageKey, JSON.stringify(resolved)) } catch { /* */ }
      return resolved
    })
  }, [storageKey])
  return [value, setValue]
}

const WINDOW_OPTIONS = [7, 14, 30, 60]
const MEMO_POLL_MAX_FAILURES = 6

function chartHref(symbol: string) {
  return `/chart?symbol=${encodeURIComponent(symbol)}`
}

function openChart(event: MouseEvent<HTMLAnchorElement>, symbol: string) {
  if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) return
  event.preventDefault()
  window.history.pushState(null, '', chartHref(symbol))
  window.dispatchEvent(new PopStateEvent('popstate'))
}

function scoreTone(value: number, inverted = false) {
  const v = inverted ? 100 - value : value
  if (v >= 65) return 'text-positive'
  if (v >= 40) return 'text-accent'
  return 'text-dim'
}

function categoryTone(category: string) {
  if (category === 'deep-dive') return 'border-trust/40 bg-trust/10 text-trust'
  if (category === 'hype-check') return 'border-negative/45 bg-negative/10 text-negative'
  if (category === 'fresh') return 'border-accent/45 bg-accent-soft text-accent'
  if (category === 'watch') return 'border-line bg-surface-2 text-muted'
  return 'border-line-soft bg-surface text-dim'
}

function actionabilityTone(actionability: string) {
  if (actionability === 'research-now') return 'border-positive/40 bg-positive/10 text-positive'
  if (actionability === 'watchlist') return 'border-accent/45 bg-accent-soft text-accent'
  if (actionability === 'wait') return 'border-negative/45 bg-negative/10 text-negative'
  return 'border-line bg-surface-2 text-muted'
}

function stanceIcon(stance: string) {
  if (stance === 'constructive') return <TrendingUp className="size-3.5 text-positive" />
  if (stance === 'caution') return <TrendingDown className="size-3.5 text-negative" />
  return <Activity className="size-3.5 text-accent" />
}

function pctLabel(value: number | null | undefined, digits = 1) {
  return value == null ? '--' : `${fmtSigned(value, digits)}%`
}

function priceLabel(value: number | null | undefined) {
  if (value == null) return '--'
  return value.toLocaleString(undefined, { maximumFractionDigits: value >= 1000 ? 1 : 2 })
}

function moneyLabel(value: number | null | undefined) {
  if (value == null) return '--'
  const abs = Math.abs(value)
  const sign = value < 0 ? '-' : value > 0 ? '+' : ''
  if (abs >= 1_000_000_000) return `${sign}$${(abs / 1_000_000_000).toFixed(1)}B`
  if (abs >= 1_000_000) return `${sign}$${(abs / 1_000_000).toFixed(1)}M`
  if (abs >= 1_000) return `${sign}$${(abs / 1_000).toFixed(1)}K`
  return `${sign}$${abs.toFixed(0)}`
}

function multipleLabel(value: number | null | undefined) {
  return value == null ? '--' : `${value.toFixed(value >= 100 ? 0 : 1)}x`
}

function ageLabel(value: string | null | undefined) {
  return value ? fmtAge(value) : '--'
}

function metricWidth(score: number) {
  return `${Math.max(4, Math.min(100, score))}%`
}

function overpricingTone(level: IdeaOverpricing['level']) {
  if (level === 'high') return 'border-negative/45 bg-negative/10 text-negative'
  if (level === 'moderate') return 'border-accent/45 bg-accent-soft text-accent'
  if (level === 'low') return 'border-positive/35 bg-positive/10 text-positive'
  return 'border-line bg-surface-2 text-muted'
}

function fieldString(row: Record<string, unknown>, key: string): string | null {
  const value = row[key]
  return typeof value === 'string' && value.trim() ? value : null
}

function fieldNumber(row: Record<string, unknown>, key: string): number | null {
  const value = row[key]
  return typeof value === 'number' ? value : null
}

function recordValue(value: unknown): Record<string, unknown> {
  return value && typeof value === 'object' && !Array.isArray(value) ? value as Record<string, unknown> : {}
}

function dataString(data: unknown, key: string): string | null {
  return fieldString(recordValue(data), key)
}

function dataNumber(data: unknown, key: string): number | null {
  return fieldNumber(recordValue(data), key)
}

function Panel({
  title,
  icon,
  action,
  children,
  className,
}: {
  title: string
  icon?: ReactNode
  action?: ReactNode
  children: ReactNode
  className?: string
}) {
  return (
    <section className={cn('min-w-0 border border-line bg-surface', className)}>
      <header className="flex h-10 items-center gap-2 border-b border-line-soft px-3">
        {icon}
        <h2 className="label-xs text-muted">{title}</h2>
        {action && <div className="ml-auto">{action}</div>}
      </header>
      {children}
    </section>
  )
}

function ScoreBar({ label, score, inverted }: { label: string; score: number; inverted?: boolean }) {
  return (
    <div>
      <div className="mb-1 flex items-center justify-between gap-3 text-[10.5px]">
        <span className="text-muted">{label}</span>
        <span className={cn('tabular font-medium', scoreTone(score, inverted))}>{score.toFixed(0)}</span>
      </div>
      <div className="h-1.5 overflow-hidden rounded-full bg-line-soft">
        <div
          className={cn('h-full rounded-full', inverted ? 'bg-negative' : 'bg-positive')}
          style={{ width: metricWidth(score) }}
        />
      </div>
    </div>
  )
}

function ScoutTile({ label, value, tone }: { label: string; value: number; tone?: 'risk' | 'quality' }) {
  const score = value * 100
  return (
    <div className="border border-line-soft bg-canvas px-2.5 py-2">
      <div className="label-xs text-dim">{label}</div>
      <div className={cn('mt-1 text-[17px] font-medium tabular', scoreTone(score, tone === 'risk'))}>
        {score.toFixed(0)}
      </div>
    </div>
  )
}

function ScoutGrid({ scouts }: { scouts: IdeaScoutScores }) {
  return (
    <div className="grid grid-cols-2 gap-px bg-line-soft sm:grid-cols-3">
      <ScoutTile label="Events" value={scouts.eventIntensity} />
      <ScoutTile label="Sources" value={scouts.sourceQuality} tone="quality" />
      <ScoutTile label="Price" value={scouts.priceAction} />
      <ScoutTile label="Insiders" value={scouts.insiderSignal} />
      <ScoutTile label="Reaction" value={scouts.marketReaction} />
      <ScoutTile label="Hype" value={scouts.hypeRisk} tone="risk" />
    </div>
  )
}

function ThesisContextPanel({
  item,
  thesis,
}: {
  item: ForwardIdeaItemDto
  thesis: ForwardIdeasDto['thesis'] | null
}) {
  const sortedModules = item.modules.slice().sort((a, b) => b.contribution - a.contribution)

  return (
    <Panel
      title="Thesis Context"
      icon={<Gauge className="size-3.5 text-accent" />}
      className="border-l-2 border-l-accent"
    >
      <div className="p-4">
        <div className="flex flex-wrap items-start justify-between gap-3">
          <div className="min-w-0">
            {thesis && <div className="text-[13px] font-medium text-fg">{thesis.label}</div>}
            <div className="mt-1 flex flex-wrap items-center gap-x-2 gap-y-1 text-[11.5px] text-muted">
              {item.group && <span>{item.group}</span>}
              {item.group && item.setupType && <span className="text-dim">&middot;</span>}
              <span>{item.setupType}</span>
            </div>
          </div>
          <div className="flex flex-wrap items-center gap-1.5">
            <span className={cn('rounded-sm border px-1.5 py-0.5 text-[9.5px] uppercase', actionabilityTone(item.actionability))}>
              {item.actionability.replaceAll('-', ' ')}
            </span>
            <span className="rounded-sm border border-line-soft bg-canvas px-1.5 py-0.5 text-[9.5px] text-dim">
              {item.tradeIntent}
            </span>
          </div>
        </div>

        <div className="mt-4 grid grid-cols-2 gap-px overflow-hidden border border-line-soft bg-line-soft sm:grid-cols-4">
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Pipeline</div>
            <div className={cn('mt-1 text-[21px] font-medium tabular', scoreTone(item.pipelineScore))}>
              {item.pipelineScore.toFixed(0)}
            </div>
          </div>
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Fit</div>
            <div className={cn('mt-1 text-[21px] font-medium tabular', scoreTone(item.thesisFit))}>
              {item.thesisFit.toFixed(0)}
            </div>
          </div>
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Crowding</div>
            <div className={cn('mt-1 text-[21px] font-medium tabular', scoreTone(item.crowdingRisk, true))}>
              {item.crowdingRisk.toFixed(0)}
            </div>
          </div>
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Signals</div>
            <div className="mt-1 text-[21px] font-medium tabular text-fg">
              {item.current.evidence.eventCount}
            </div>
          </div>
        </div>
      </div>

      <div className="border-t border-line-soft px-4 py-3">
        <div className="label-xs mb-3 text-dim">Module Scores</div>
        <div className="grid gap-3">
          {sortedModules.map(mod => (
            <div key={mod.key}>
              <ScoreBar label={mod.label} score={mod.score} />
              <div className="mt-1 text-[10.5px] leading-relaxed text-dim">{mod.rationale}</div>
            </div>
          ))}
        </div>
      </div>

      <div className="grid gap-px border-t border-line-soft bg-line-soft lg:grid-cols-3">
        <div className="bg-surface">
          <div className="border-b border-line-soft px-3 py-2 label-xs">Rationale</div>
          <div className="divide-y divide-line-soft">
            {item.rationale.map((line, i) => (
              <div key={i} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">{line}</div>
            ))}
          </div>
        </div>
        <div className="bg-surface">
          <div className="border-b border-line-soft px-3 py-2 label-xs">Next Checks</div>
          <div className="divide-y divide-line-soft">
            {item.nextChecks.map((line, i) => (
              <div key={i} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">{line}</div>
            ))}
          </div>
        </div>
        <div className="bg-surface">
          <div className="border-b border-line-soft px-3 py-2 label-xs text-negative">Invalidates</div>
          <div className="divide-y divide-line-soft">
            {item.invalidates.map((line, i) => (
              <div key={i} className="px-3 py-2.5 text-[12px] leading-relaxed text-negative/80">{line}</div>
            ))}
          </div>
        </div>
      </div>
    </Panel>
  )
}

function RadarItem({
  item,
  selected,
  onSelect,
}: {
  item: ForwardIdeaItemDto
  selected: boolean
  onSelect: (symbol: string) => void
}) {
  const current = item.current
  const topModules = item.modules
    .slice()
    .sort((a, b) => b.contribution - a.contribution)
    .slice(0, 2)

  return (
    <button
      type="button"
      onClick={() => onSelect(item.symbol)}
      aria-pressed={selected}
      className={cn(
        'block w-full border-b border-line-soft px-3 py-3 text-left transition hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/45',
        selected && 'bg-surface-2 ring-1 ring-inset ring-accent/45',
      )}
    >
      <div className="flex items-start justify-between gap-3">
        <div className="min-w-0">
          <div className="flex items-center gap-2">
            <span className="font-mono text-[13px] font-semibold text-fg">{item.symbol}</span>
            <span className={cn('rounded-sm border px-1.5 py-0.5 text-[9.5px] uppercase', actionabilityTone(item.actionability))}>
              {item.actionability.replaceAll('-', ' ')}
            </span>
          </div>
          <div className="mt-1 truncate text-[12px] text-muted">{item.name}</div>
          <div className="mt-1 truncate text-[10.5px] text-dim">{item.group ?? item.setupType}</div>
        </div>
        <div className="shrink-0 text-right">
          <div className="text-[18px] font-medium tabular text-fg">{item.pipelineScore.toFixed(0)}</div>
          <div className="text-[10px] text-dim">{ageLabel(item.latestSignalAt)}</div>
        </div>
      </div>

      <div className="mt-3 grid grid-cols-3 gap-2 text-[11px]">
        <div>
          <div className="label-xs text-dim">30d</div>
          <div className={cn('mt-0.5 tabular', current.price.return30d != null && current.price.return30d > 0 ? 'text-positive' : current.price.return30d != null && current.price.return30d < 0 ? 'text-negative' : 'text-muted')}>
            {pctLabel(current.price.return30d)}
          </div>
        </div>
        <div>
          <div className="label-xs text-dim">Fit</div>
          <div className={cn('mt-0.5 tabular', scoreTone(item.thesisFit))}>{item.thesisFit.toFixed(0)}</div>
        </div>
        <div>
          <div className="label-xs text-dim">Crowd</div>
          <div className={cn('mt-0.5 tabular', scoreTone(item.crowdingRisk, true))}>{item.crowdingRisk.toFixed(0)}</div>
        </div>
      </div>

      <div className="mt-3 flex items-center gap-2 text-[11px] text-muted">
        {stanceIcon(current.stance)}
        <span className="truncate">{item.rationale[0] ?? item.tradeIntent}</span>
      </div>
      {topModules.length > 0 && (
        <div className="mt-2 flex min-w-0 flex-wrap gap-1">
          {topModules.map(module => (
            <span key={module.key} className="rounded-sm border border-line-soft bg-canvas px-1.5 py-0.5 text-[9.5px] text-dim">
              {module.label} {module.score.toFixed(0)}
            </span>
          ))}
        </div>
      )}
    </button>
  )
}

function RadarPanel({
  data,
  selectedSymbol,
  onSelect,
}: {
  data: ForwardIdeasDto
  selectedSymbol: string | null
  onSelect: (symbol: string) => void
}) {
  return (
    <Panel
      title="Forward Pipeline"
      icon={<Gauge className="size-3.5 text-accent" />}
      action={<span className="label-xs text-dim">{fmtCount(data.universe.candidates)} candidates</span>}
      className="xl:sticky xl:top-[68px] xl:max-h-[calc(100vh-92px)]"
    >
      <div className="border-b border-line-soft px-3 py-2.5">
        <div className="text-[12px] font-medium text-fg">{data.thesis.label}</div>
        <p className="mt-1 line-clamp-2 text-[11.5px] leading-relaxed text-muted">{data.thesis.description}</p>
      </div>
      <div className="grid grid-cols-3 gap-px border-b border-line-soft bg-line-soft">
        <div className="bg-surface px-3 py-2">
          <div className="label-xs text-dim">Ideas</div>
          <div className="mt-0.5 text-[18px] font-medium tabular text-fg">{data.items.length}</div>
        </div>
        <div className="bg-surface px-3 py-2">
          <div className="label-xs text-dim">Guarded</div>
          <div className="mt-0.5 text-[18px] font-medium tabular text-fg">{fmtCount(data.pipeline.crowdingGuard.excluded)}</div>
        </div>
        <div className="bg-surface px-3 py-2">
          <div className="label-xs text-dim">Modules</div>
          <div className="mt-0.5 text-[18px] font-medium tabular text-fg">{fmtCount(data.pipeline.modules.length)}</div>
        </div>
      </div>
      <div className="md:max-h-[680px] md:overflow-y-auto xl:max-h-[calc(100vh-196px)]">
        {data.items.length === 0 ? (
          <div className="px-3 py-8 text-[12px] text-faint">No forward candidates in this window.</div>
        ) : data.items.map(item => (
          <RadarItem
            key={item.symbol}
            item={item}
            selected={selectedSymbol === item.symbol}
            onSelect={onSelect}
          />
        ))}
      </div>
    </Panel>
  )
}

function BriefHero({ brief }: { brief: IdeaBriefDto }) {
  const idea = brief.idea
  return (
    <Panel title="Brief" icon={<Activity className="size-3.5 text-accent" />}>
      <div className="p-4">
        <div className="flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <h1 className="font-mono text-[24px] font-semibold leading-none text-fg">{brief.symbol}</h1>
              <span className={cn('rounded-sm border px-1.5 py-0.5 text-[9.5px] uppercase tracking-[0.07em]', categoryTone(idea.category))}>
                {idea.category.replaceAll('-', ' ')}
              </span>
              <span className="inline-flex items-center gap-1 text-[11px] text-muted">
                {stanceIcon(idea.stance)}
                {idea.stance}
              </span>
            </div>
            <div className="mt-1 text-[14px] text-muted">{brief.metadata?.companyName ?? idea.name}</div>
            <p className="mt-4 max-w-4xl text-[14px] leading-relaxed text-fg">{brief.brief.bottomLine}</p>
          </div>
          <a
            href={chartHref(brief.symbol)}
            onClick={event => openChart(event, brief.symbol)}
            className="inline-flex h-8 shrink-0 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2.5 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
          >
            <CandlestickChart className="size-3.5" />
            <span className="label-xs">Chart</span>
          </a>
        </div>

        <div className="mt-5 grid gap-3 md:grid-cols-2 xl:grid-cols-4">
          <div className="border border-line-soft bg-canvas px-3 py-2.5">
            <div className="label-xs text-dim">Last</div>
            <div className="mt-1 text-[21px] font-medium tabular text-fg">{priceLabel(brief.price.latestClose)}</div>
            <div className="mt-0.5 text-[10.5px] text-dim">{ageLabel(brief.price.latestDate)}</div>
          </div>
          <div className="border border-line-soft bg-canvas px-3 py-2.5">
            <div className="label-xs text-dim">30d / 90d</div>
            <div className="mt-1 text-[21px] font-medium tabular text-fg">{pctLabel(brief.price.return30d)} / {pctLabel(brief.price.return90d)}</div>
            <div className="mt-0.5 text-[10.5px] text-dim">YTD {pctLabel(brief.price.ytdReturn)}</div>
          </div>
          <div className="border border-line-soft bg-canvas px-3 py-2.5">
            <div className="label-xs text-dim">Interest</div>
            <div className="mt-1 text-[21px] font-medium tabular text-fg">{idea.interestScore.toFixed(0)}</div>
            <div className="mt-0.5 text-[10.5px] text-dim">Quality {idea.qualityScore.toFixed(0)}</div>
          </div>
          <div className="border border-line-soft bg-canvas px-3 py-2.5">
            <div className="label-xs text-dim">Overpricing</div>
            <div className={cn('mt-1 text-[21px] font-medium tabular', scoreTone(brief.overpricing.score ?? idea.hypeRisk, true))}>
              {brief.overpricing.score == null ? '--' : brief.overpricing.score.toFixed(0)}
            </div>
            <div className="mt-0.5 truncate text-[10.5px] text-dim">{brief.overpricing.label}</div>
          </div>
        </div>
      </div>
    </Panel>
  )
}

function ResearchCards({ brief }: { brief: IdeaBriefDto }) {
  const cards = [
    { title: 'Why Now', items: brief.idea.whyNow, icon: <Activity className="size-3.5 text-accent" /> },
    { title: 'Hype Check', items: brief.idea.hypeCheck, icon: <ShieldAlert className="size-3.5 text-negative" /> },
    { title: 'Watch Next', items: brief.idea.watchNext, icon: <CalendarDays className="size-3.5 text-trust" /> },
  ]

  return (
    <div className="grid gap-3 lg:grid-cols-3">
      {cards.map(card => (
        <Panel key={card.title} title={card.title} icon={card.icon}>
          <div className="divide-y divide-line-soft">
            {card.items.map((item, index) => (
              <div key={`${card.title}-${index}`} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">
                {item}
              </div>
            ))}
          </div>
        </Panel>
      ))}
    </div>
  )
}

function EvidenceIds({ ids, onOpen }: { ids: string[]; onOpen?: (id: string) => void }) {
  if (ids.length === 0) return null
  return (
    <div className="mt-1 flex flex-wrap gap-1">
      {ids.slice(0, 4).map(id => (
        <button
          key={id}
          type="button"
          onClick={() => onOpen?.(id)}
          className="rounded-sm border border-line-soft bg-canvas px-1.5 py-0.5 text-[9.5px] tabular text-dim transition hover:border-accent/45 hover:bg-accent-soft hover:text-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40"
        >
          {id}
        </button>
      ))}
    </div>
  )
}

function MemoClaimList({ items, empty, onEvidenceOpen }: { items: IdeaMemoClaim[]; empty: string; onEvidenceOpen?: (id: string) => void }) {
  if (items.length === 0) return <div className="px-3 py-5 text-[12px] text-faint">{empty}</div>
  return (
    <div className="divide-y divide-line-soft">
      {items.map((item, index) => (
        <div key={index} className="px-3 py-2.5">
          <div className="text-[12px] leading-relaxed text-muted">{item.claim}</div>
          <EvidenceIds ids={item.evidenceIds} onOpen={onEvidenceOpen} />
        </div>
      ))}
    </div>
  )
}

function MemoActionList({ items, mode, onEvidenceOpen }: { items: IdeaMemoAction[]; mode: 'action' | 'trigger'; onEvidenceOpen?: (id: string) => void }) {
  if (items.length === 0) return <div className="px-3 py-5 text-[12px] text-faint">No model-generated items yet.</div>
  return (
    <div className="divide-y divide-line-soft">
      {items.map((item, index) => (
        <div key={index} className="px-3 py-2.5">
          <div className="text-[12px] leading-relaxed text-muted">{mode === 'action' ? item.action : item.trigger}</div>
          <EvidenceIds ids={item.evidenceIds} onOpen={onEvidenceOpen} />
        </div>
      ))}
    </div>
  )
}

function AiMemoPanel({
  memo,
  loading,
  refreshing,
  pollStalled,
  onRefresh,
  onEvidenceOpen,
}: {
  memo: IdeaMemoDto | null
  loading: boolean
  refreshing: boolean
  pollStalled: boolean
  onRefresh: () => void
  onEvidenceOpen: (id: string) => void
}) {
  const content = memo?.memo
  const statusLabel = memo
    ? memo.isCurrent ? 'current' : memo.currentStatus === 'ready' ? 'stale' : memo.currentStatus
    : 'not requested'

  return (
    <Panel
      title="AI Memo"
      icon={<FileText className="size-3.5 text-accent" />}
      action={(
        <button
          type="button"
          onClick={onRefresh}
          disabled={refreshing}
          className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:cursor-wait disabled:opacity-60"
        >
          <RefreshCw className={cn('size-3', refreshing && 'animate-spin')} />
          <span className="label-xs">Refresh</span>
        </button>
      )}
    >
      <div className="border-b border-line-soft px-3 py-2 text-[10.5px] text-dim">
        <span className="uppercase tracking-[0.08em]">{statusLabel}</span>
        {memo?.generatedAt && <span className="ml-3">Generated {ageLabel(memo.generatedAt)}</span>}
        {memo?.modelName && <span className="ml-3">{memo.modelName}</span>}
        {pollStalled && <span className="ml-3 text-negative">Status checks failing — refresh to retry</span>}
      </div>

      {loading && !memo && (
        <div className="px-3 py-8 text-[12px] text-faint pulse-soft">Checking memo cache...</div>
      )}

      {!loading && memo && !content && memo.currentStatus !== 'failed' && (
        <div className="px-3 py-8 text-[12px] leading-relaxed text-muted">
          Memo queued for grounded Ollama analysis. The deterministic brief is usable while the model works in the background.
        </div>
      )}

      {memo?.currentStatus === 'failed' && !content && (
        <div className="px-3 py-8 text-[12px] leading-relaxed text-negative">
          Memo generation failed: {memo.error ?? 'unknown error'}
        </div>
      )}

      {content && (
        <div>
          <div className="p-3">
            <div className="flex flex-wrap items-center gap-2">
              <span className={cn('rounded-sm border px-1.5 py-0.5 text-[9.5px] uppercase tracking-[0.07em]', categoryTone(content.researchMode))}>
                {content.researchMode.replaceAll('-', ' ')}
              </span>
              <span className={cn('rounded-sm border px-1.5 py-0.5 text-[9.5px] uppercase tracking-[0.07em]', overpricingTone(content.overpricingRisk.level))}>
                overpricing {content.overpricingRisk.level}
              </span>
            </div>
            <p className="mt-3 text-[13px] leading-relaxed text-fg">{content.bottomLine}</p>
            <p className="mt-2 text-[12px] leading-relaxed text-muted">{content.overpricingRisk.rationale}</p>
            <EvidenceIds ids={content.overpricingRisk.evidenceIds} onOpen={onEvidenceOpen} />
          </div>
          <div className="grid gap-px border-t border-line-soft bg-line-soft lg:grid-cols-2">
            <div className="bg-surface">
              <div className="border-b border-line-soft px-3 py-2 label-xs">Bull</div>
              <MemoClaimList items={content.bullCase} empty="No bull claims from the evidence packet." onEvidenceOpen={onEvidenceOpen} />
            </div>
            <div className="bg-surface">
              <div className="border-b border-line-soft px-3 py-2 label-xs">Bear</div>
              <MemoClaimList items={content.bearCase} empty="No bear claims from the evidence packet." onEvidenceOpen={onEvidenceOpen} />
            </div>
            <div className="bg-surface">
              <div className="border-b border-line-soft px-3 py-2 label-xs">Contradictions</div>
              <MemoClaimList items={content.contradictions} empty="No internal contradictions flagged." onEvidenceOpen={onEvidenceOpen} />
            </div>
            <div className="bg-surface">
              <div className="border-b border-line-soft px-3 py-2 label-xs">Next Actions</div>
              <MemoActionList items={content.nextResearchActions} mode="action" onEvidenceOpen={onEvidenceOpen} />
            </div>
          </div>
          <div className="grid gap-px border-t border-line-soft bg-line-soft lg:grid-cols-2">
            <div className="bg-surface">
              <div className="border-b border-line-soft px-3 py-2 label-xs">Watch Triggers</div>
              <MemoActionList items={content.watchTriggers} mode="trigger" onEvidenceOpen={onEvidenceOpen} />
            </div>
            <div className="bg-surface">
              <div className="border-b border-line-soft px-3 py-2 label-xs">Unknowns</div>
              <div className="divide-y divide-line-soft">
                {content.keyUnknowns.map((item, index) => (
                  <div key={index} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">{item}</div>
                ))}
                {content.dataQualityWarnings.map((item, index) => (
                  <div key={`warning-${index}`} className="px-3 py-2.5 text-[12px] leading-relaxed text-accent">{item}</div>
                ))}
              </div>
            </div>
          </div>
        </div>
      )}
    </Panel>
  )
}

function CasePanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <div className="grid gap-3 lg:grid-cols-2">
      <Panel title="Bull Case" icon={<TrendingUp className="size-3.5 text-positive" />}>
        <div className="divide-y divide-line-soft">
          {brief.brief.bullCase.length === 0 ? (
            <div className="px-3 py-8 text-[12px] text-faint">No strong bull case in the current corpus.</div>
          ) : brief.brief.bullCase.map((item, index) => (
            <div key={index} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">{item}</div>
          ))}
        </div>
      </Panel>
      <Panel title="Bear Case" icon={<TrendingDown className="size-3.5 text-negative" />}>
        <div className="divide-y divide-line-soft">
          {brief.brief.bearCase.length === 0 ? (
            <div className="px-3 py-8 text-[12px] text-faint">No strong bear case in the current corpus.</div>
          ) : brief.brief.bearCase.map((item, index) => (
            <div key={index} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">{item}</div>
          ))}
        </div>
      </Panel>
    </div>
  )
}

function ScorePanel({ idea }: { idea: IdeaRadarItem }) {
  return (
    <Panel title="Scouts" icon={<Gauge className="size-3.5 text-accent" />}>
      <div className="p-3">
        <ScoutGrid scouts={idea.scouts} />
        <div className="mt-4 grid gap-3">
          <ScoreBar label="Source quality" score={idea.qualityScore} />
          <ScoreBar label="Hype risk" score={idea.hypeRisk} inverted />
          <ScoreBar label="Interest" score={idea.interestScore} />
        </div>
      </div>
    </Panel>
  )
}

function PricePanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Price Context" icon={<CandlestickChart className="size-3.5 text-accent" />}>
      <div className="p-3">
        <div className="grid grid-cols-2 gap-px overflow-hidden border border-line-soft bg-line-soft">
          {[
            ['7d', pctLabel(brief.price.return7d)],
            ['30d', pctLabel(brief.price.return30d)],
            ['90d', pctLabel(brief.price.return90d)],
            ['1y', pctLabel(brief.price.return1y)],
          ].map(([label, value]) => (
            <div key={label} className="bg-canvas px-2.5 py-2">
              <div className="label-xs text-dim">{label}</div>
              <div className="mt-1 text-[15px] font-medium tabular text-fg">{value}</div>
            </div>
          ))}
        </div>
        {brief.price.rangePosition != null && (
          <div className="mt-4">
            <ScoreBar label="1y range position" score={brief.price.rangePosition} />
            <div className="mt-2 flex items-center justify-between gap-3 text-[10.5px] text-dim">
              <span>{priceLabel(brief.price.yearLow)}</span>
              <span>{priceLabel(brief.price.yearHigh)}</span>
            </div>
          </div>
        )}
      </div>
    </Panel>
  )
}

function ValuationPanel({ brief }: { brief: IdeaBriefDto }) {
  const f = brief.fundamentals
  const risk = brief.overpricing
  return (
    <Panel title="Valuation" icon={<Scale className="size-3.5 text-accent" />}>
      <div className="p-3">
        <div className="mb-3 flex flex-wrap items-center gap-2">
          <span className={cn('rounded-sm border px-1.5 py-0.5 text-[9.5px] uppercase tracking-[0.07em]', overpricingTone(risk.level))}>
            {risk.level}
          </span>
          {risk.score != null && <span className="tabular text-[11px] text-muted">{risk.score.toFixed(0)} risk</span>}
          {f.ingestedAt && <span className="text-[10.5px] text-dim">{ageLabel(f.ingestedAt)}</span>}
        </div>

        {!f.hasFundamentals ? (
          <div className="text-[12px] leading-relaxed text-muted">
            Fundamentals are not cached yet.
          </div>
        ) : (
          <>
            <div className="grid grid-cols-2 gap-px overflow-hidden border border-line-soft bg-line-soft">
              {[
                ['Market cap', moneyLabel(f.marketCap)],
                ['P/E TTM', multipleLabel(f.peTtm)],
                ['Forward P/E', multipleLabel(f.forwardPe)],
                ['P/S TTM', multipleLabel(f.psTtm)],
                ['EV/Sales', multipleLabel(f.evRevenueTtm)],
                ['Rev growth', pctLabel(f.revenueGrowthTtmYoy)],
              ].map(([label, value]) => (
                <div key={label} className="bg-canvas px-2.5 py-2">
                  <div className="label-xs text-dim">{label}</div>
                  <div className="mt-1 text-[15px] font-medium tabular text-fg">{value}</div>
                </div>
              ))}
            </div>
            <div className="mt-3 space-y-2">
              {risk.reasons.slice(0, 5).map(reason => (
                <div key={reason} className="text-[11.5px] leading-relaxed text-muted">{reason}</div>
              ))}
            </div>
          </>
        )}
      </div>
    </Panel>
  )
}

function SourceMixPanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Source Mix" icon={<FileText className="size-3.5 text-trust" />}>
      <div className="p-3">
        <div className="grid gap-px overflow-hidden border border-line-soft bg-line-soft">
          {brief.sourceMix.byTier.map(tier => (
            <div key={tier.tier} className="grid grid-cols-[1fr_auto_auto] items-center gap-2 bg-canvas px-2.5 py-2 text-[11.5px]">
              <SourceTierLabel tier={tier.tier} />
              <span className="tabular text-muted">{fmtCount(tier.count)}</span>
              <span className="tabular text-dim">{tier.share.toFixed(0)}%</span>
            </div>
          ))}
        </div>
        <div className="mt-3 space-y-2">
          {brief.sourceMix.bySource.slice(0, 6).map(source => (
            <div key={source.source} className="flex items-center justify-between gap-3 text-[11.5px]">
              <span className="truncate text-muted">{sourceLabel(source.source)}</span>
              <span className="shrink-0 tabular text-dim">{fmtCount(source.count)}</span>
            </div>
          ))}
        </div>
      </div>
    </Panel>
  )
}

function InsiderPanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Insiders" icon={<UserCheck className="size-3.5 text-accent" />}>
      <div className="p-3">
        <div className="grid grid-cols-3 gap-px overflow-hidden border border-line-soft bg-line-soft">
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Open</div>
            <div className="mt-1 text-[16px] font-medium tabular text-fg">{fmtCount(brief.insiders.openMarketTransactions)}</div>
          </div>
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Bought</div>
            <div className="mt-1 text-[16px] font-medium tabular text-positive">{moneyLabel(brief.insiders.bought)}</div>
          </div>
          <div className="bg-canvas px-2.5 py-2">
            <div className="label-xs text-dim">Net</div>
            <div className={cn('mt-1 text-[16px] font-medium tabular', brief.insiders.netDollars >= 0 ? 'text-positive' : 'text-negative')}>
              {moneyLabel(brief.insiders.netDollars)}
            </div>
          </div>
        </div>
        <div className="mt-3 divide-y divide-line-soft">
          {brief.insiders.topInsiders.length === 0 ? (
            <div className="py-3 text-[12px] text-faint">No recent open-market trades.</div>
          ) : brief.insiders.topInsiders.slice(0, 5).map(owner => (
            <div key={`${owner.ownerName}-${owner.latestTransactionAt ?? ''}`} className="py-2">
              <div className="flex items-center justify-between gap-3">
                <div className="min-w-0">
                  <div className="truncate text-[12px] text-fg">{owner.ownerName}</div>
                  <div className="mt-0.5 truncate text-[10.5px] text-dim">{owner.role}</div>
                </div>
                <div className={cn('shrink-0 text-right text-[12px] font-medium tabular', owner.netDollars >= 0 ? 'text-positive' : 'text-negative')}>
                  {moneyLabel(owner.netDollars)}
                </div>
              </div>
            </div>
          ))}
        </div>
      </div>
    </Panel>
  )
}

function EventRow({ event }: { event: IdeaTopEvent }) {
  return (
    <div className="grid gap-3 border-b border-line-soft px-3 py-3 last:border-0 lg:grid-cols-[150px_1fr_auto]">
      <div className="min-w-0">
        <EventTypeBadge type={event.eventType} />
        <div className="mt-2 flex items-center gap-2">
          <SourceTierLabel tier={event.sourceTier} />
          <span className="text-[10.5px] tabular text-dim">{ageLabel(event.lastSeenAt)}</span>
        </div>
      </div>
      <div className="min-w-0">
        <div className="text-[13px] leading-relaxed text-fg">{event.summary}</div>
        {event.topSource.topHeadline && (
          <div className="mt-1 flex min-w-0 items-center gap-1.5 text-[11px] text-dim">
            {event.topSource.topUrl ? (
              <a
                href={event.topSource.topUrl}
                target="_blank"
                rel="noreferrer"
                className="inline-flex min-w-0 items-center gap-1 transition hover:text-accent"
              >
                <span className="truncate">{event.topSource.topHeadline}</span>
                <ExternalLink className="size-3 shrink-0" />
              </a>
            ) : (
              <span className="truncate">{event.topSource.topHeadline}</span>
            )}
          </div>
        )}
      </div>
      <div className="grid grid-cols-3 gap-2 text-right lg:w-[190px]">
        <div>
          <div className="label-xs text-dim">Imp</div>
          <div className="mt-1 tabular text-fg">{fmtImportance(event.importance)}</div>
        </div>
        <div>
          <div className="label-xs text-dim">Move</div>
          <div className="mt-1 tabular text-muted">{pctLabel(event.market.movePercent)}</div>
        </div>
        <div>
          <div className="label-xs text-dim">Vol</div>
          <div className="mt-1 tabular text-muted">{event.market.relativeVolume == null ? '--' : `${event.market.relativeVolume.toFixed(1)}x`}</div>
        </div>
      </div>
    </div>
  )
}

function EventPanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel
      title="Event Tape"
      icon={<Activity className="size-3.5 text-accent" />}
      action={<span className="label-xs text-dim">{brief.topEvents.length} events</span>}
    >
      <div>
        {brief.topEvents.length === 0 ? (
          <div className="px-3 py-8 text-[12px] text-faint">No extracted events in this window.</div>
        ) : brief.topEvents.map(event => (
          <EventRow key={event.clusterId} event={event} />
        ))}
      </div>
    </Panel>
  )
}

function ThesisPanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Thesis Links" icon={<Activity className="size-3.5 text-trust" />}>
      <div className="divide-y divide-line-soft">
        {brief.theses.length === 0 ? (
          <div className="px-3 py-8 text-[12px] text-faint">No attached thesis.</div>
        ) : brief.theses.map(row => {
          const id = fieldString(row, 'id') ?? fieldString(row, 'Id') ?? fieldString(row, 'name') ?? ''
          const name = fieldString(row, 'name') ?? 'Untitled thesis'
          const total = fieldNumber(row, 'total') ?? 0
          const supports = fieldNumber(row, 'supports') ?? 0
          const contradicts = fieldNumber(row, 'contradicts') ?? 0
          return (
            <div key={id} className="px-3 py-2.5">
              <div className="truncate text-[12.5px] text-fg">{name}</div>
              <div className="mt-1 flex items-center gap-3 text-[10.5px] text-dim">
                <span>{fmtCount(total)} evidence</span>
                <span className="text-positive">{fmtCount(supports)} supports</span>
                <span className="text-negative">{fmtCount(contradicts)} contradicts</span>
              </div>
            </div>
          )
        })}
      </div>
    </Panel>
  )
}

function DocumentsPanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Primary Reads" icon={<FileText className="size-3.5 text-trust" />}>
      <div className="divide-y divide-line-soft">
        {brief.filingChunks.length === 0 ? (
          <div className="px-3 py-8 text-[12px] text-faint">No recent filing passages.</div>
        ) : brief.filingChunks.slice(0, 6).map(row => {
          const id = fieldString(row, 'id') ?? `${fieldString(row, 'filingHeadline') ?? 'filing'}-${fieldNumber(row, 'chunkIndex') ?? 0}`
          const section = fieldString(row, 'section') ?? 'Filing'
          const text = fieldString(row, 'text') ?? ''
          const headline = fieldString(row, 'filingHeadline')
          const url = fieldString(row, 'filingUrl')
          return (
            <div key={id} className="px-3 py-3">
              <div className="flex items-start justify-between gap-3">
                <div className="min-w-0">
                  <div className="label-xs text-dim">{section}</div>
                  {headline && <div className="mt-1 truncate text-[12px] text-fg">{headline}</div>}
                </div>
                {url && (
                  <a href={url} target="_blank" rel="noreferrer" className="shrink-0 text-dim transition hover:text-accent">
                    <ExternalLink className="size-3.5" />
                  </a>
                )}
              </div>
              <p className="mt-2 line-clamp-3 text-[12px] leading-relaxed text-muted">{text}</p>
            </div>
          )
        })}
      </div>
    </Panel>
  )
}

function CalendarPanel({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Catalysts" icon={<CalendarDays className="size-3.5 text-accent" />}>
      <div className="divide-y divide-line-soft">
        {brief.calendar.length === 0 ? (
          <div className="px-3 py-8 text-[12px] text-faint">No scheduled catalysts.</div>
        ) : brief.calendar.slice(0, 8).map(item => (
          <div key={`${item.scheduledAt}-${item.label}`} className="grid grid-cols-[74px_1fr] gap-3 px-3 py-2.5">
            <div className="tabular text-[11px] text-dim">{new Date(item.scheduledAt).toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}</div>
            <div className="min-w-0">
              <div className="truncate text-[12px] text-fg">{item.label}</div>
              <div className="mt-0.5 text-[10.5px] text-dim">{eventLabel(item.eventType)} / {sourceLabel(item.source)}</div>
            </div>
          </div>
        ))}
      </div>
    </Panel>
  )
}

function DataGaps({ brief }: { brief: IdeaBriefDto }) {
  return (
    <Panel title="Missing Inputs" icon={<AlertCircle className="size-3.5 text-negative" />}>
      <div className="divide-y divide-line-soft">
        {brief.dataGaps.map((gap, index) => (
          <div key={index} className="px-3 py-2.5 text-[12px] leading-relaxed text-muted">{gap}</div>
        ))}
      </div>
    </Panel>
  )
}

function EvidenceMetric({ label, value }: { label: string; value: string }) {
  return (
    <div className="bg-canvas px-2.5 py-2">
      <div className="label-xs text-dim">{label}</div>
      <div className="mt-1 text-[14px] font-medium tabular text-fg">{value}</div>
    </div>
  )
}

function EvidenceDrawer({
  evidence,
  loading,
  error,
  onClose,
}: {
  evidence: IdeaEvidenceDto | null
  loading: boolean
  error: string | null
  onClose: () => void
}) {
  if (!evidence && !loading && !error) return null

  const type = evidence?.evidenceType
  const data = evidence?.data
  const url = dataString(data, 'topUrl') ?? dataString(data, 'url') ?? dataString(data, 'filingUrl')

  return (
    <aside className="fixed inset-y-0 right-0 z-30 flex w-full max-w-[460px] flex-col border-l border-line bg-surface shadow-2xl sm:top-12 sm:h-[calc(100vh-3rem)]">
      <header className="flex min-h-12 items-center gap-2 border-b border-line-soft px-3">
        <PanelRightOpen className="size-4 text-accent" />
        <div className="min-w-0">
          <div className="label-xs text-dim">Evidence</div>
          <div className="truncate text-[12px] tabular text-muted">{evidence?.evidenceId ?? 'loading'}</div>
        </div>
        <button
          type="button"
          onClick={onClose}
          className="ml-auto inline-flex size-8 items-center justify-center rounded-sm border border-line bg-canvas text-muted transition hover:bg-surface-2 hover:text-fg"
        >
          <X className="size-4" />
        </button>
      </header>

      <div className="min-h-0 flex-1 overflow-y-auto p-3">
        {loading && (
          <div className="border border-line-soft bg-canvas px-3 py-8 text-[12px] text-faint pulse-soft">Loading evidence...</div>
        )}

        {!loading && error && (
          <div className="border border-negative/35 bg-negative/10 px-3 py-3 text-[12px] leading-relaxed text-negative">{error}</div>
        )}

        {!loading && evidence && (
          <div className="grid gap-3">
            <section className="border border-line-soft bg-canvas p-3">
              <div className="flex flex-wrap items-center gap-2">
                <span className="rounded-sm border border-line bg-surface px-1.5 py-0.5 text-[9.5px] uppercase tracking-[0.07em] text-muted">
                  {evidence.evidenceType}
                </span>
                {evidence.symbol && <span className="text-[10.5px] tabular text-dim">{evidence.symbol}</span>}
              </div>
              <h3 className="mt-3 text-[15px] font-semibold leading-snug text-fg">{evidence.title}</h3>
              {evidence.subtitle && <div className="mt-1 text-[12px] text-muted">{evidence.subtitle}</div>}
              {evidence.summary && <p className="mt-3 text-[12px] leading-relaxed text-muted">{evidence.summary}</p>}
              {url && (
                <a href={url} target="_blank" rel="noreferrer" className="mt-3 inline-flex items-center gap-1.5 text-[11px] text-accent transition hover:text-fg">
                  <ExternalLink className="size-3.5" />
                  <span className="label-xs">Open Source</span>
                </a>
              )}
            </section>

            {type === 'event' && (
              <section className="grid grid-cols-2 gap-px border border-line-soft bg-line-soft">
                <EvidenceMetric label="Importance" value={fmtImportance(dataNumber(data, 'importance') ?? 0)} />
                <EvidenceMetric label="Reaction" value={dataNumber(data, 'reactionScore') == null ? '--' : dataNumber(data, 'reactionScore')!.toFixed(2)} />
                <EvidenceMetric label="Move" value={pctLabel(dataNumber(data, 'movePercent'))} />
                <EvidenceMetric label="Rel move" value={pctLabel(dataNumber(data, 'relativeMovePercent'))} />
              </section>
            )}

            {type === 'insider' && (
              <section className="grid grid-cols-2 gap-px border border-line-soft bg-line-soft">
                <EvidenceMetric label="Date" value={dataString(data, 'transactionDate') ? new Date(dataString(data, 'transactionDate')!).toLocaleDateString() : '--'} />
                <EvidenceMetric label="Code" value={`${dataString(data, 'transactionCode') ?? '--'} / ${dataString(data, 'acquiredDisposedCode') ?? '--'}`} />
                <EvidenceMetric label="Shares" value={dataNumber(data, 'shares')?.toLocaleString() ?? '--'} />
                <EvidenceMetric label="Value" value={moneyLabel(dataNumber(data, 'dollarValue'))} />
              </section>
            )}

            {type === 'fundamentals' && (
              <section className="grid grid-cols-2 gap-px border border-line-soft bg-line-soft">
                <EvidenceMetric label="Market cap" value={moneyLabel(dataNumber(data, 'marketCap'))} />
                <EvidenceMetric label="P/E TTM" value={multipleLabel(dataNumber(data, 'peTtm'))} />
                <EvidenceMetric label="Forward P/E" value={multipleLabel(dataNumber(data, 'forwardPe'))} />
                <EvidenceMetric label="P/S TTM" value={multipleLabel(dataNumber(data, 'psTtm'))} />
                <EvidenceMetric label="EV/Sales" value={multipleLabel(dataNumber(data, 'evRevenueTtm'))} />
                <EvidenceMetric label="Rev growth" value={pctLabel(dataNumber(data, 'revenueGrowthTtmYoy'))} />
              </section>
            )}

            {type === 'price' && (
              <section className="grid grid-cols-2 gap-px border border-line-soft bg-line-soft">
                <EvidenceMetric label="Last" value={priceLabel(dataNumber(data, 'latestClose'))} />
                <EvidenceMetric label="7d" value={pctLabel(dataNumber(data, 'return7d'))} />
                <EvidenceMetric label="30d" value={pctLabel(dataNumber(data, 'return30d'))} />
                <EvidenceMetric label="90d" value={pctLabel(dataNumber(data, 'return90d'))} />
              </section>
            )}

            {(type === 'filing' || type === 'transcript') && evidence.summary && (
              <section className="border border-line-soft bg-canvas p-3">
                <div className="label-xs text-dim">Text</div>
                <p className="mt-2 whitespace-pre-wrap text-[12px] leading-relaxed text-muted">{evidence.summary}</p>
              </section>
            )}

            {type === 'dataGaps' && Array.isArray(data) && (
              <section className="divide-y divide-line-soft border border-line-soft bg-canvas">
                {data.map((item, index) => (
                  <div key={index} className="flex gap-2 px-3 py-2.5 text-[12px] leading-relaxed text-muted">
                    <CheckCircle2 className="mt-0.5 size-3.5 shrink-0 text-accent" />
                    <span>{String(item)}</span>
                  </div>
                ))}
              </section>
            )}
          </div>
        )}
      </div>
    </aside>
  )
}

function BriefWorkspace({
  brief,
  memo,
  memoLoading,
  memoRefreshing,
  memoPollStalled,
  onMemoRefresh,
  evidence,
  evidenceLoading,
  evidenceError,
  onEvidenceOpen,
  onEvidenceClose,
  forwardItem,
  pipelineThesis,
}: {
  brief: IdeaBriefDto
  memo: IdeaMemoDto | null
  memoLoading: boolean
  memoRefreshing: boolean
  memoPollStalled: boolean
  onMemoRefresh: () => void
  evidence: IdeaEvidenceDto | null
  evidenceLoading: boolean
  evidenceError: string | null
  onEvidenceOpen: (id: string) => void
  onEvidenceClose: () => void
  forwardItem: ForwardIdeaItemDto | null
  pipelineThesis: ForwardIdeasDto['thesis'] | null
}) {
  return (
    <>
      <div className="grid gap-3">
        {forwardItem && <ThesisContextPanel item={forwardItem} thesis={pipelineThesis} />}
        <BriefHero brief={brief} />
        <AiMemoPanel memo={memo} loading={memoLoading} refreshing={memoRefreshing} pollStalled={memoPollStalled} onRefresh={onMemoRefresh} onEvidenceOpen={onEvidenceOpen} />
        <ResearchCards brief={brief} />
        <CasePanel brief={brief} />
        <div className="grid gap-3 xl:grid-cols-[1fr_340px]">
          <EventPanel brief={brief} />
          <div className="grid content-start gap-3">
            <ValuationPanel brief={brief} />
            <PricePanel brief={brief} />
            <ScorePanel idea={brief.idea} />
            <SourceMixPanel brief={brief} />
            <InsiderPanel brief={brief} />
          </div>
        </div>
        <div className="grid gap-3 lg:grid-cols-2 xl:grid-cols-4">
          <ThesisPanel brief={brief} />
          <DocumentsPanel brief={brief} />
          <CalendarPanel brief={brief} />
          <DataGaps brief={brief} />
        </div>
      </div>
      <EvidenceDrawer evidence={evidence} loading={evidenceLoading} error={evidenceError} onClose={onEvidenceClose} />
    </>
  )
}

export function IdeasView() {
  const [windowDays, setWindowDays] = usePersistedState('windowDays', 14)
  const [refreshKey, setRefreshKey] = useState(0)
  const [includeCrowded, setIncludeCrowded] = usePersistedState('includeCrowded', false)
  const [pipelineCatalog, setPipelineCatalog] = useState<ForwardPipelineCatalogDto | null>(null)
  const [selectedPipelineKey, setSelectedPipelineKey] = usePersistedState<string | null>('pipelineKey', null)
  const [pipeline, setPipeline] = useState<ForwardIdeasDto | null>(null)
  const [brief, setBrief] = useState<IdeaBriefDto | null>(null)
  const [selectedSymbol, setSelectedSymbol] = useState<string | null>(null)
  const [memo, setMemo] = useState<IdeaMemoDto | null>(null)
  const [selectedEvidenceId, setSelectedEvidenceId] = useState<string | null>(null)
  const [evidence, setEvidence] = useState<IdeaEvidenceDto | null>(null)
  const [pipelineLoading, setPipelineLoading] = useState(false)
  const [briefLoading, setBriefLoading] = useState(false)
  const [memoLoading, setMemoLoading] = useState(false)
  const [memoRefreshing, setMemoRefreshing] = useState(false)
  const [memoPollStalled, setMemoPollStalled] = useState(false)
  const [evidenceLoading, setEvidenceLoading] = useState(false)
  const [evidenceError, setEvidenceError] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const isMobile = useIsMobile()

  useEffect(() => {
    const ac = new AbortController()
    fetchForwardPipelines(ac.signal)
      .then(res => {
        setPipelineCatalog(res)
        setSelectedPipelineKey(current => current ?? res.defaultPipelineKey ?? res.items[0]?.key ?? null)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
      })
    return () => ac.abort()
  }, [setSelectedPipelineKey])

  useEffect(() => {
    if (!selectedPipelineKey) return
    const ac = new AbortController()
    setPipelineLoading(true)
    setError(null)
    fetchForwardIdeas({ thesis: selectedPipelineKey, windowDays, take: 36, includeCrowded }, ac.signal)
      .then(res => {
        setPipeline(res)
        setPipelineLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setPipelineLoading(false)
      })
    return () => ac.abort()
  }, [selectedPipelineKey, windowDays, includeCrowded, refreshKey])

  useEffect(() => {
    if (!pipeline) return
    setSelectedSymbol(current => {
      if (current && pipeline.items.some(item => item.symbol === current)) return current
      return isMobile ? null : pipeline.items[0]?.symbol ?? null
    })
  }, [pipeline, isMobile])

  const briefWindow = useMemo(() => Math.max(60, windowDays * 4), [windowDays])

  const selectedForwardItem = useMemo(
    () => selectedSymbol && pipeline ? pipeline.items.find(i => i.symbol === selectedSymbol) ?? null : null,
    [pipeline, selectedSymbol],
  )

  const pipelineThesis = useMemo(
    () => pipeline?.thesis ?? null,
    [pipeline],
  )

  useEffect(() => {
    if (!selectedSymbol) {
      setBrief(null)
      setMemo(null)
      setSelectedEvidenceId(null)
      setEvidence(null)
      return
    }

    const ac = new AbortController()
    setBriefLoading(true)
    fetchIdeaBrief(selectedSymbol, briefWindow, ac.signal)
      .then(res => {
        setBrief(res)
        setBriefLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setBriefLoading(false)
      })
    return () => ac.abort()
  }, [selectedSymbol, briefWindow])

  useEffect(() => {
    if (!selectedSymbol) {
      setMemo(null)
      return
    }

    const ac = new AbortController()
    setMemoLoading(true)
    fetchIdeaMemo(selectedSymbol, briefWindow, ac.signal)
      .then(res => {
        setMemo(res)
        setMemoLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setMemoLoading(false)
      })
    return () => ac.abort()
  }, [selectedSymbol, briefWindow])

  const memoIsCurrent = memo?.isCurrent
  const memoStatus = memo?.currentStatus

  useEffect(() => {
    if (!selectedSymbol || memoIsCurrent == null || memoIsCurrent || memoStatus === 'failed') return
    let cancelled = false
    let timer = 0
    let failures = 0
    setMemoPollStalled(false)
    const poll = () => {
      if (cancelled) return
      fetchIdeaMemo(selectedSymbol, briefWindow)
        .then(res => {
          if (cancelled) return
          failures = 0
          setMemo(res)
          if (!res.isCurrent && res.currentStatus !== 'failed') {
            timer = window.setTimeout(poll, 10_000)
          }
        })
        .catch(() => {
          if (cancelled) return
          failures += 1
          if (failures >= MEMO_POLL_MAX_FAILURES) {
            setMemoPollStalled(true)
            return
          }
          timer = window.setTimeout(poll, 10_000)
        })
    }
    timer = window.setTimeout(poll, 5_000)
    return () => { cancelled = true; window.clearTimeout(timer) }
  }, [selectedSymbol, briefWindow, memoIsCurrent, memoStatus])

  useEffect(() => {
    setSelectedEvidenceId(null)
    setEvidence(null)
    setEvidenceError(null)
    setEvidenceLoading(false)
  }, [selectedSymbol, briefWindow])

  useEffect(() => {
    if (!selectedSymbol || !selectedEvidenceId) {
      setEvidence(null)
      setEvidenceError(null)
      setEvidenceLoading(false)
      return
    }

    const ac = new AbortController()
    setEvidenceLoading(true)
    setEvidenceError(null)
    fetchIdeaEvidence(selectedSymbol, selectedEvidenceId, briefWindow, memo?.evidenceHash, ac.signal)
      .then(res => {
        setEvidence(res)
        setEvidenceLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setEvidence(null)
        setEvidenceError(err instanceof Error ? err.message : 'Evidence lookup failed')
        setEvidenceLoading(false)
      })
    return () => ac.abort()
  }, [selectedSymbol, selectedEvidenceId, briefWindow, memo?.evidenceHash])

  const handleMemoRefresh = () => {
    if (!selectedSymbol) return
    const ac = new AbortController()
    setMemoRefreshing(true)
    setMemoPollStalled(false)
    refreshIdeaMemo(selectedSymbol, briefWindow, ac.signal)
      .then(res => {
        setMemo(res)
        setMemoRefreshing(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setMemoRefreshing(false)
      })
  }

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <div className="sticky top-0 z-10 flex min-h-12 flex-wrap items-center gap-3 border-b border-line-soft bg-canvas/90 px-3 py-2.5 backdrop-blur sm:px-5">
        <div className="flex w-full items-center gap-2 sm:w-auto">
          {isMobile && selectedSymbol && (
            <button
              type="button"
              onClick={() => setSelectedSymbol(null)}
              className="inline-flex h-8 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
            >
              <ArrowLeft className="size-3.5" />
              <span className="label-xs">Ideas</span>
            </button>
          )}
          <div className="flex min-w-0 items-center gap-2">
            <Gauge className="size-4 shrink-0 text-accent" />
            <h2 className="label-xs text-dim">Ideas</h2>
            {isMobile && selectedSymbol && <span className="truncate font-mono text-[12px] text-fg">{selectedSymbol}</span>}
          </div>
        </div>
        {pipelineCatalog && (
          <Select
            value={selectedPipelineKey}
            options={pipelineCatalog.items.map(item => ({ value: item.key, label: item.label }))}
            onChange={setSelectedPipelineKey}
            width={isMobile ? 150 : 250}
            className="max-w-[calc(100vw-1.5rem)] shrink-0"
          />
        )}
        <Select
          value={String(windowDays)}
          options={WINDOW_OPTIONS.map(d => ({ value: String(d), label: `${d}d` }))}
          onChange={(v) => { if (v) setWindowDays(Number(v)) }}
          width={70}
          className="shrink-0"
        />
        <label className="inline-flex h-7 items-center gap-2 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted">
          <input
            type="checkbox"
            checked={includeCrowded}
            onChange={event => setIncludeCrowded(event.currentTarget.checked)}
            className="size-3 accent-accent"
          />
          <span className="label-xs hidden sm:inline">Include crowded</span>
        </label>
        <button
          type="button"
          onClick={() => setRefreshKey(k => k + 1)}
          className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
        >
          <RefreshCw className={cn('size-3', (pipelineLoading || briefLoading) && 'animate-spin')} />
          <span className="label-xs hidden sm:inline">Refresh</span>
        </button>
      </div>

      <div className="p-3 sm:p-5">
        {error && (
          <div className="mb-3 flex items-center gap-2 border border-negative/35 bg-negative/10 px-3 py-2 text-[13px] text-negative">
            <AlertCircle className="size-4" />
            <span>API error: {error}</span>
          </div>
        )}

        {!pipeline && !error && (
          <div className="text-[12px] text-faint pulse-soft">Loading...</div>
        )}

        {pipeline && (
          <div className={cn('grid gap-3', !isMobile && 'xl:grid-cols-[380px_minmax(0,1fr)]')}>
            {(!isMobile || !selectedSymbol) && (
              <RadarPanel data={pipeline} selectedSymbol={selectedSymbol} onSelect={setSelectedSymbol} />
            )}
            {(!isMobile || selectedSymbol) && (
              <main className="min-w-0">
                {briefLoading && !brief ? (
                  <div className="border border-line bg-surface px-3 py-8 text-[12px] text-faint pulse-soft">Loading brief...</div>
                ) : brief ? (
                  <BriefWorkspace
                    brief={brief}
                    memo={memo}
                    memoLoading={memoLoading}
                    memoRefreshing={memoRefreshing}
                    memoPollStalled={memoPollStalled}
                    onMemoRefresh={handleMemoRefresh}
                    evidence={evidence}
                    evidenceLoading={evidenceLoading}
                    evidenceError={evidenceError}
                    onEvidenceOpen={setSelectedEvidenceId}
                    onEvidenceClose={() => {
                      setSelectedEvidenceId(null)
                      setEvidence(null)
                      setEvidenceError(null)
                      setEvidenceLoading(false)
                    }}
                    forwardItem={selectedForwardItem}
                    pipelineThesis={pipelineThesis}
                  />
                ) : (
                  <div className="border border-line bg-surface px-3 py-8 text-[12px] text-faint">Select an idea.</div>
                )}
              </main>
            )}
          </div>
        )}


      </div>
    </div>
  )
}
