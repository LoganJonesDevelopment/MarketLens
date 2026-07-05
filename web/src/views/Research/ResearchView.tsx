import { useCallback, useEffect, useMemo, useState, type ReactNode } from 'react'
import {
  AlertCircle,
  ArrowLeft,
  Check,
  ChevronLeft,
  ChevronRight,
  ExternalLink,
  Inbox,
  MessageSquarePlus,
  MessageSquareText,
  PanelLeftClose,
  PanelLeftOpen,
  PanelRightClose,
  PanelRightOpen,
  Pencil,
  Pin,
  Plus,
  RefreshCw,
  RotateCcw,
  X,
} from 'lucide-react'
import { useMediaQuery } from '../../lib/useMediaQuery'
import {
  bootstrapThesis,
  fetchTheses,
  fetchThesis,
  fetchThesisEvidence,
  reviewThesisEvidence,
  scanThesis,
  type ReviewThesisEvidenceCommand,
  type ThesisDetailDto,
  type ThesisDto,
  type ThesisEvidenceDto,
  type ThesisEvidenceReviewStatus,
  type ThesisRuleDto,
} from '../../lib/api'
import { Select } from '../../components/Select'
import { SourceTierDot, SourceTierLabel } from '../../components/SourceTier'
import { SentimentDot } from '../../components/SentimentDot'
import { EventTypeBadge } from '../../components/EventTypeBadge'
import { cn } from '../../lib/cn'
import { fmtAge, fmtCount, fmtSigned, sourceLabel } from '../../lib/format'
import { ClusterDrill } from './ClusterDrill'
import { ExplorationScreen } from './ExplorationScreen'
import { RuleEditor } from './RuleEditor'
import { ThesisBriefing } from './ThesisBriefing'
import { Sheet, SheetClose } from '../../components/Sheet'

const STATUS_OPTIONS = [
  { value: null, label: 'All' },
  { value: 'active', label: 'Active' },
  { value: 'paused', label: 'Paused' },
  { value: 'draft', label: 'Draft' },
  { value: 'archived', label: 'Archived' },
]

type ScopeMode = 'theses' | 'explorations'

const SCOPE_OPTIONS: Array<{ value: ScopeMode; label: string }> = [
  { value: 'theses', label: 'Tracked' },
  { value: 'explorations', label: 'Explorations' },
]

type SymbolScope =
  | { kind: 'all' }
  | { kind: 'ticker'; value: string }

type EvidenceFilter = 'queue' | 'accepted' | 'needs_review' | 'rejected' | 'all'

function isAbort(err: unknown): boolean {
  return (err as { name?: string }).name === 'AbortError'
}

function score(v: number | null | undefined): string {
  return v == null ? '—' : v.toFixed(2)
}

function age(v: string | null | undefined): string {
  return v ? fmtAge(v) : '—'
}

function statusLabel(v: string | null | undefined): string {
  return v ? v.replaceAll('_', ' ') : 'active'
}

function stanceLabel(v: string | null | undefined): string {
  return v ? v.replaceAll('_', ' ') : 'unknown'
}

function Button({
  active,
  children,
  className,
  ...rest
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { active?: boolean }) {
  return (
    <button
      className={cn(
        'inline-flex h-7 cursor-pointer items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition',
        'hover:bg-surface-2 hover:text-fg active:bg-surface-2 disabled:cursor-not-allowed disabled:opacity-50',
        active && 'border-accent/60 bg-accent-soft text-fg',
        className,
      )}
      {...rest}
    >
      {children}
    </button>
  )
}

function Chip({
  active, onClick, children,
}: { active: boolean; onClick: () => void; children: ReactNode }) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'h-6 cursor-pointer rounded-sm px-2 text-[11px] tracking-tight transition tabular',
        active ? 'bg-surface-2 text-fg' : 'text-dim hover:bg-surface/60 hover:text-fg active:bg-surface-2',
      )}
    >
      {children}
    </button>
  )
}

function Metric({ label, value, accent }: { label: string; value: ReactNode; accent?: boolean }) {
  return (
    <div className="border-r border-line-soft px-4 py-3 last:border-r-0">
      <div className="label-xs text-dim">{label}</div>
      <div className={cn('mt-1 text-[20px] font-medium tabular leading-none', accent && 'text-accent')}>
        {value}
      </div>
    </div>
  )
}

function StatusBadge({ status }: { status?: string | null }) {
  const s = statusLabel(status)
  return (
    <span className="inline-flex h-5 shrink-0 items-center rounded-sm border border-line bg-surface-2/60 px-1.5 text-[10px] tracking-[0.06em] uppercase text-muted">
      {s}
    </span>
  )
}

function StanceBadge({ stance, confidence }: { stance?: string | null; confidence?: number | null }) {
  const s = stance ?? 'unknown'
  const cls =
    s === 'supports' ? 'border-positive/40 text-positive' :
    s === 'contradicts' ? 'border-negative/40 text-negative' :
    s === 'neutral' ? 'border-line text-muted' :
    'border-line/60 text-faint'
  const dim = confidence != null && confidence < 0.4
  return (
    <span
      className={cn(
        'inline-flex h-5 shrink-0 items-center gap-1 rounded-sm border bg-surface-2/60 px-1.5 text-[10px] tracking-[0.06em] uppercase',
        cls,
        dim && 'opacity-60',
      )}
      title={confidence != null ? `confidence ${confidence.toFixed(2)}` : undefined}
    >
      {stanceLabel(stance)}
      {confidence != null && (
        <span className="font-mono text-[9px] tabular opacity-80">{confidence.toFixed(2)}</span>
      )}
    </span>
  )
}

function ReviewBadge({ status }: { status?: string | null }) {
  const s = status ?? 'pending'
  const cls =
    s === 'accepted' ? 'text-positive' :
    s === 'rejected' ? 'text-negative' :
    s === 'needs_review' ? 'text-accent' :
    'text-faint'
  return <span className={cn('label-xs tabular', cls)}>{statusLabel(s)}</span>
}

function ThesisNavigator({
  thesis,
  index,
  total,
  onPrev,
  onNext,
  onOpenList,
}: {
  thesis: ThesisDto | null
  index: number
  total: number
  onPrev: () => void
  onNext: () => void
  onOpenList: () => void
}) {
  if (!thesis || index < 0 || total === 0) return null
  const pending = thesis.pendingEvidenceCount ?? 0
  return (
    <div className="hidden h-7 min-w-0 max-w-[38vw] items-center overflow-hidden rounded-sm border border-line bg-canvas text-[11px] md:flex">
      <button
        onClick={onPrev}
        disabled={total < 2}
        className="inline-flex h-full w-7 shrink-0 items-center justify-center text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-40"
        title="Previous thesis"
        aria-label="Previous thesis"
      >
        <ChevronLeft className="size-3.5" />
      </button>
      <button
        onClick={onOpenList}
        className="flex h-full min-w-0 flex-1 items-center gap-1.5 border-x border-line-soft px-2 text-left transition hover:bg-surface-2"
        title="Show thesis list"
      >
        {thesis.symbol && <span className="shrink-0 font-medium tabular text-fg">{thesis.symbol}</span>}
        <span className="min-w-0 truncate text-muted">{thesis.title || 'Untitled thesis'}</span>
        {pending > 0 && <span className="shrink-0 tabular text-accent">{pending}</span>}
        <span className="shrink-0 tabular text-faint">{index + 1}/{total}</span>
      </button>
      <button
        onClick={onNext}
        disabled={total < 2}
        className="inline-flex h-full w-7 shrink-0 items-center justify-center text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-40"
        title="Next thesis"
        aria-label="Next thesis"
      >
        <ChevronRight className="size-3.5" />
      </button>
    </div>
  )
}

function ResearchFilterBar({
  symbol,
  status,
  scope,
  count,
  loading,
  symbols,
  leftPanelOpen,
  rightPanelOpen,
  canShowRightPanel,
  navigatorThesis,
  navigatorIndex,
  navigatorTotal,
  onSymbolChange,
  onStatusChange,
  onScopeChange,
  onToggleLeftPanel,
  onToggleRightPanel,
  onPreviousThesis,
  onNextThesis,
  onRefresh,
  onExplore,
}: {
  symbol: SymbolScope
  status: string | null
  scope: ScopeMode
  count: number
  loading: boolean
  symbols: string[]
  leftPanelOpen: boolean
  rightPanelOpen: boolean
  canShowRightPanel: boolean
  navigatorThesis: ThesisDto | null
  navigatorIndex: number
  navigatorTotal: number
  onSymbolChange: (v: SymbolScope) => void
  onStatusChange: (v: string | null) => void
  onScopeChange: (v: ScopeMode) => void
  onToggleLeftPanel: () => void
  onToggleRightPanel: () => void
  onPreviousThesis: () => void
  onNextThesis: () => void
  onRefresh: () => void
  onExplore: () => void
}) {
  const countLabel = scope === 'explorations'
    ? `${count} ${count === 1 ? 'exploration' : 'explorations'}`
    : `${count} ${count === 1 ? 'thesis' : 'theses'}`
  return (
    <div className="border-b border-line-soft bg-canvas/80 backdrop-blur">
      <div className="flex items-center gap-2 px-3 py-2 sm:gap-x-4 sm:px-5 sm:py-2.5">
        <div className="flex shrink-0 items-center gap-0.5">
          {SCOPE_OPTIONS.map(s => (
            <Chip key={s.value} active={scope === s.value} onClick={() => onScopeChange(s.value)}>{s.label}</Chip>
          ))}
        </div>
        <div className="hidden md:flex items-center gap-0.5">
          <div className="mx-1.5 h-3 w-px bg-line-soft" />
          <Chip active={symbol.kind === 'all'} onClick={() => onSymbolChange({ kind: 'all' })}>All</Chip>
          {symbols.length > 0 && <div className="mx-1.5 h-3 w-px bg-line-soft" />}
          {symbols.map(t => (
            <Chip
              key={t}
              active={symbol.kind === 'ticker' && symbol.value === t}
              onClick={() => onSymbolChange({ kind: 'ticker', value: t })}
            >
              {t}
            </Chip>
          ))}
        </div>
        <div className="ml-auto flex shrink-0 items-center gap-1.5 sm:gap-4">
          {!leftPanelOpen && (
            <ThesisNavigator
              thesis={navigatorThesis}
              index={navigatorIndex}
              total={navigatorTotal}
              onPrev={onPreviousThesis}
              onNext={onNextThesis}
              onOpenList={onToggleLeftPanel}
            />
          )}
          <div className="hidden items-center gap-1 md:flex">
            <Button
              onClick={onToggleLeftPanel}
              className="px-1.5"
              title={leftPanelOpen ? 'Hide thesis list' : 'Show thesis list'}
            >
              {leftPanelOpen ? <PanelLeftClose className="size-3" /> : <PanelLeftOpen className="size-3" />}
            </Button>
            {canShowRightPanel && (
              <Button
                onClick={onToggleRightPanel}
                className="px-1.5"
                title={rightPanelOpen ? 'Hide context panel' : 'Show context panel'}
              >
                {rightPanelOpen ? <PanelRightClose className="size-3" /> : <PanelRightOpen className="size-3" />}
              </Button>
            )}
          </div>
          {scope === 'theses' && (
            <div className="hidden sm:block">
              <Select
                value={status}
                options={STATUS_OPTIONS}
                onChange={onStatusChange}
                placeholder="All"
                width={130}
              />
            </div>
          )}
          <Button onClick={onRefresh} className="px-1.5 sm:px-2" title="Refresh">
            <RefreshCw className={cn('size-3', loading && 'animate-spin')} />
            <span className="label-xs hidden sm:inline">Refresh</span>
          </Button>
          <Button onClick={onExplore} active>
            <Plus className="size-3" />
            <span className="label-xs">Explore</span>
          </Button>
          <span className="label-xs hidden sm:inline tabular text-faint">{countLabel}</span>
        </div>
      </div>
    </div>
  )
}

function ThesisRow({
  thesis,
  active,
  onClick,
}: {
  thesis: ThesisDto
  active: boolean
  onClick: () => void
}) {
  const pending = thesis.pendingEvidenceCount ?? 0
  return (
    <li>
      <button
        onClick={onClick}
        className={cn(
          'w-full border-b border-line-soft px-4 py-3 text-left transition cursor-pointer',
          'hover:bg-surface/70 active:bg-surface-2',
          active && 'bg-surface-2',
        )}
      >
        <div className="min-w-0">
          <div className="flex min-w-0 items-center gap-2">
            {thesis.symbol && (
              <span className="shrink-0 text-[12px] font-medium tabular text-fg">{thesis.symbol}</span>
            )}
            <StatusBadge status={thesis.status} />
            {pending > 0 && (
              <span className="label-xs tabular text-accent">{pending} pending</span>
            )}
            <span className="ml-auto shrink-0 text-[10.5px] tabular text-faint">{age(thesis.lastEvidenceAt ?? thesis.updatedAt)}</span>
            <span className="shrink-0 text-[11px] tabular text-muted">{score(thesis.confidence ?? thesis.conviction)}</span>
          </div>
          <div className="mt-1.5 break-words text-[13px] font-medium leading-snug text-fg">{thesis.title}</div>
          {(thesis.summary ?? thesis.statement) && (
            <div className="mt-1 line-clamp-2 text-[11.5px] leading-snug text-dim">
              {thesis.summary ?? thesis.statement}
            </div>
          )}
          <div className="mt-2 flex flex-wrap items-center gap-x-3 gap-y-1 text-[10.5px] tabular text-faint">
            <span>{fmtCount(thesis.evidenceCount ?? 0)} evidence</span>
            <span>{fmtCount(thesis.supportedCount ?? 0)} support</span>
            <span>{fmtCount(thesis.contradictedCount ?? 0)} contra</span>
          </div>
        </div>
      </button>
    </li>
  )
}

function ThesisList({
  theses,
  selectedId,
  onSelect,
}: {
  theses: ThesisDto[]
  selectedId: string | null
  onSelect: (thesis: ThesisDto) => void
}) {
  if (theses.length === 0) {
    return (
      <div className="flex flex-col items-center justify-center gap-2 py-16 text-faint">
        <Inbox className="size-5" />
        <span className="text-[12px]">No theses match these filters</span>
      </div>
    )
  }

  return (
    <ul>
      {theses.map(t => (
        <ThesisRow key={t.id} thesis={t} active={selectedId === t.id} onClick={() => onSelect(t)} />
      ))}
    </ul>
  )
}

function EvidenceReviewControls({
  thesisId,
  evidence,
  busy,
  noteOpen,
  onReview,
  onPin,
  onToggleNote,
}: {
  thesisId: string
  evidence: ThesisEvidenceDto
  busy: boolean
  noteOpen: boolean
  onReview: (thesisId: string, evidenceId: string, reviewStatus: ThesisEvidenceReviewStatus) => void
  onPin: (thesisId: string, evidenceId: string, isPinned: boolean) => void
  onToggleNote: () => void
}) {
  const pinned = !!evidence.isPinned
  const hasNote = !!evidence.reviewerNote
  return (
    <div className="flex shrink-0 items-center gap-1.5">
      <Button
        active={evidence.reviewStatus === 'accepted'}
        disabled={busy}
        onClick={e => { e.stopPropagation(); onReview(thesisId, evidence.id, 'accepted') }}
        title="Accept evidence"
      >
        <Check className="size-3" />
        <span className="label-xs">Accept</span>
      </Button>
      <Button
        active={evidence.reviewStatus === 'rejected'}
        disabled={busy}
        onClick={e => { e.stopPropagation(); onReview(thesisId, evidence.id, 'rejected') }}
        title="Reject evidence"
      >
        <X className="size-3" />
        <span className="label-xs">Reject</span>
      </Button>
      <Button
        active={evidence.reviewStatus === 'needs_review'}
        disabled={busy}
        onClick={e => { e.stopPropagation(); onReview(thesisId, evidence.id, 'needs_review') }}
        title="Needs review"
      >
        <RotateCcw className="size-3" />
        <span className="label-xs">Later</span>
      </Button>
      <Button
        active={pinned}
        disabled={busy}
        onClick={e => { e.stopPropagation(); onPin(thesisId, evidence.id, !pinned) }}
        title={pinned ? 'Unpin' : 'Pin'}
        className="px-1.5"
      >
        <Pin className={cn('size-3', pinned && 'fill-current')} />
      </Button>
      <Button
        active={noteOpen || hasNote}
        disabled={busy}
        onClick={e => { e.stopPropagation(); onToggleNote() }}
        title={hasNote ? 'Edit note' : 'Add note'}
        className="px-1.5"
      >
        {hasNote
          ? <MessageSquareText className="size-3" />
          : <MessageSquarePlus className="size-3" />}
      </Button>
    </div>
  )
}

function EvidenceRow({
  thesisId,
  evidence,
  busy,
  focused,
  onReview,
  onPin,
  onSetNote,
  onFocus,
}: {
  thesisId: string
  evidence: ThesisEvidenceDto
  busy: boolean
  focused: boolean
  onReview: (thesisId: string, evidenceId: string, reviewStatus: ThesisEvidenceReviewStatus) => void
  onPin: (thesisId: string, evidenceId: string, isPinned: boolean) => void
  onSetNote: (thesisId: string, evidenceId: string, note: string | null) => void
  onFocus: (evidenceId: string) => void
}) {
  const sourceText = evidence.source
    ? sourceLabel(evidence.source)
    : evidence.publisher ?? null
  const timestamp = evidence.publishedAt ?? evidence.observedAt ?? evidence.createdAt
  const [noteOpen, setNoteOpen] = useState(false)
  const [noteDraft, setNoteDraft] = useState(evidence.reviewerNote ?? '')

  useEffect(() => {
    setNoteDraft(evidence.reviewerNote ?? '')
  }, [evidence.reviewerNote])

  const commitNote = () => {
    const next = noteDraft.trim()
    const current = (evidence.reviewerNote ?? '').trim()
    if (next === current) {
      setNoteOpen(false)
      return
    }
    onSetNote(thesisId, evidence.id, next.length > 0 ? next : null)
    setNoteOpen(false)
  }

  return (
    <li
      onClick={() => onFocus(evidence.id)}
      className={cn(
        'relative cursor-pointer border-b border-line-soft px-5 py-3 transition last:border-b-0',
        focused ? 'bg-surface-2/60' : 'hover:bg-surface/40',
        evidence.isPinned && 'before:absolute before:left-0 before:top-0 before:bottom-0 before:w-[3px] before:bg-accent',
      )}
    >
      <div className="flex items-start gap-3">
        <div className="pt-1.5">
          <SourceTierDot tier={evidence.sourceTier ?? 'aggregator'} />
        </div>
        <div className="min-w-0 flex-1">
          <div className="flex flex-wrap items-center gap-2">
            {sourceText && (
              <span className="text-[10px] font-medium tracking-[0.085em] uppercase text-muted">{sourceText}</span>
            )}
            {evidence.sourceTier && <SourceTierLabel tier={evidence.sourceTier} />}
            {evidence.eventType && <EventTypeBadge type={evidence.eventType} />}
            <StanceBadge stance={evidence.stance} confidence={evidence.stanceConfidence} />
            <ReviewBadge status={evidence.reviewStatus} />
            <span className="ml-auto text-[10.5px] tabular text-faint">{age(timestamp)}</span>
          </div>
          {evidence.url ? (
            <a
              href={evidence.url}
              target="_blank"
              rel="noreferrer"
              onClick={e => e.stopPropagation()}
              className="mt-1 flex items-start gap-1 text-[12.5px] leading-snug text-fg hover:underline underline-offset-2"
            >
              <span className="line-clamp-2">{evidence.headline ?? evidence.summary ?? evidence.id}</span>
              <ExternalLink className="mt-0.5 size-3 shrink-0 opacity-50" />
            </a>
          ) : (
            <div className="mt-1 line-clamp-2 text-[12.5px] leading-snug text-fg">
              {evidence.headline ?? evidence.summary ?? evidence.id}
            </div>
          )}
          {evidence.summary && evidence.headline && (
            <div className="mt-1 line-clamp-2 text-[11.5px] leading-snug text-dim">{evidence.summary}</div>
          )}
          {evidence.stanceRationale && (
            <div className="mt-1.5 rounded-sm border-l-2 border-line-soft bg-surface/30 px-2 py-1 text-[11px] leading-snug text-muted">
              <span className="label-xs text-dim">why </span>
              {evidence.stanceRationale}
            </div>
          )}
          {evidence.matchReason && (
            <div className="mt-1.5 text-[10.5px] leading-snug text-faint">
              matched on <span className="text-dim">{evidence.matchReason}</span>
            </div>
          )}
          <div className="mt-2 flex flex-wrap items-center gap-3 text-[10.5px] tabular text-faint">
            <span>sim {score(evidence.similarity)}</span>
            <span>conf {score(evidence.confidence)}</span>
            <span>imp {score(evidence.importance)}</span>
            {evidence.sentiment != null && (
              <span className="inline-flex items-center gap-1">
                <SentimentDot value={evidence.sentiment} size={5} />
                {fmtSigned(evidence.sentiment)}
              </span>
            )}
            {evidence.clusterId && <span>cluster {evidence.clusterId.slice(0, 8)}</span>}
          </div>
          {(noteOpen || evidence.reviewerNote) && (
            <div className="mt-2" onClick={e => e.stopPropagation()}>
              {noteOpen ? (
                <textarea
                  autoFocus
                  value={noteDraft}
                  onChange={e => setNoteDraft(e.target.value)}
                  onBlur={commitNote}
                  onKeyDown={e => {
                    if (e.key === 'Escape') {
                      setNoteDraft(evidence.reviewerNote ?? '')
                      setNoteOpen(false)
                    }
                    if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) commitNote()
                  }}
                  placeholder="Why this matters / why you accepted or rejected"
                  rows={2}
                  className="w-full resize-none rounded-sm border border-line bg-canvas px-2 py-1.5 text-[11.5px] leading-relaxed text-fg outline-none focus:border-accent/70"
                />
              ) : (
                <button
                  onClick={() => setNoteOpen(true)}
                  className="text-left text-[11px] leading-snug text-dim hover:text-fg"
                >
                  {evidence.reviewerNote}
                </button>
              )}
            </div>
          )}
        </div>
        <EvidenceReviewControls
          thesisId={thesisId}
          evidence={evidence}
          busy={busy}
          noteOpen={noteOpen}
          onReview={onReview}
          onPin={onPin}
          onToggleNote={() => setNoteOpen(o => !o)}
        />
      </div>
    </li>
  )
}

function StatusTile({
  label,
  value,
  active,
  onClick,
}: {
  label: string
  value: number
  active: boolean
  onClick: () => void
}) {
  return (
    <button
      onClick={onClick}
      className={cn(
        'cursor-pointer border-r border-line-soft px-4 py-3 text-left transition last:border-r-0',
        active ? 'bg-surface-2' : 'hover:bg-surface/70 active:bg-surface-2',
      )}
    >
      <div className="label-xs text-dim">{label}</div>
      <div className="mt-1 text-[20px] font-medium tabular leading-none text-fg">{fmtCount(value)}</div>
    </button>
  )
}

function filterEvidence(items: ThesisEvidenceDto[], filter: EvidenceFilter): ThesisEvidenceDto[] {
  if (filter === 'queue') {
    return items.filter(e => {
      const status = e.reviewStatus ?? 'pending'
      return status === 'pending' || status === 'needs_review'
    })
  }
  if (filter === 'accepted') return items.filter(e => e.reviewStatus === 'accepted')
  if (filter === 'needs_review') return items.filter(e => e.reviewStatus === 'needs_review')
  if (filter === 'rejected') return items.filter(e => e.reviewStatus === 'rejected')
  return items
}

function DetailSection({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="border-b border-line-soft last:border-b-0">
      <header className="border-b border-line-soft px-5 py-2.5">
        <h3 className="label-xs text-dim">{title}</h3>
      </header>
      {children}
    </section>
  )
}

function KeywordChip({ kind, children }: { kind: 'asset' | 'concept' | 'exclude'; children: ReactNode }) {
  const cls =
    kind === 'asset'   ? 'border-line text-fg' :
    kind === 'concept' ? 'border-line-soft text-muted' :
                         'border-negative/30 text-faint line-through'
  return (
    <span className={cn(
      'inline-flex h-5 items-center rounded-sm border bg-surface-2/60 px-1.5 text-[10.5px] tabular',
      cls,
    )}>
      {children}
    </span>
  )
}

function ScopeStrip({
  detail,
  onEditRule,
  onAddRule,
}: {
  detail: ThesisDetailDto | null
  onEditRule: (rule: ThesisRuleDto) => void
  onAddRule: () => void
}) {
  const hasRules = !!detail?.rules && detail.rules.length > 0
  const hasAssets = !!detail?.assets && detail.assets.length > 0
  if (!hasRules && !hasAssets) return null
  return (
    <div className="border-b border-line-soft bg-surface/30 px-5 py-3">
      {hasAssets && (
        <div className="mb-2 flex flex-wrap items-center gap-1.5">
          <span className="label-xs text-dim mr-1">Assets</span>
          {detail!.assets!.map(a => (
            <span
              key={a.assetId}
              className="inline-flex h-5 items-center gap-1 rounded-sm border border-line bg-surface-2/60 px-1.5 text-[10.5px] text-fg"
              title={a.kind ? `${a.kind} · ${a.role}` : a.role}
            >
              {a.symbol && <span className="font-medium tabular">{a.symbol}</span>}
              <span className="text-muted">{a.name ?? '—'}</span>
            </span>
          ))}
        </div>
      )}
      <div className="space-y-2">
        {detail?.rules?.map(r => {
          const allEmpty =
            r.assetKeywords.length === 0 &&
            r.conceptKeywords.length === 0 &&
            r.excludeTerms.length === 0 &&
            r.eventTypes.length === 0 &&
            r.sourceNames.length === 0 &&
            r.sourceTiers.length === 0
          return (
            <div key={r.id} className="space-y-1">
              <div className="flex items-center gap-2">
                <span className={cn(
                  'label-xs',
                  r.isEnabled ? 'text-dim' : 'text-faint line-through',
                )}>{r.name}</span>
                {r.minArticleSimilarity != null && (
                  <span className="text-[10px] tabular text-faint">sim ≥ {r.minArticleSimilarity.toFixed(2)}</span>
                )}
                {r.eventTypes.length > 0 && (
                  <span className="text-[10px] tabular text-faint">
                    {r.eventTypes.length} event {r.eventTypes.length === 1 ? 'type' : 'types'}
                  </span>
                )}
                {r.sourceNames.length > 0 && (
                  <span className="text-[10px] tabular text-faint">
                    {r.sourceNames.length} {r.sourceNames.length === 1 ? 'source' : 'sources'}
                  </span>
                )}
                {r.sourceTiers.length > 0 && (
                  <span className="text-[10px] tabular text-faint">
                    {r.sourceTiers.length} {r.sourceTiers.length === 1 ? 'tier' : 'tiers'}
                  </span>
                )}
                <button
                  onClick={() => onEditRule(r)}
                  title="Edit rule"
                  className="ml-auto rounded-sm p-1 text-dim transition hover:bg-surface-2 hover:text-fg"
                >
                  <Pencil className="size-3" />
                </button>
              </div>
              <div className="flex flex-wrap gap-1">
                {r.assetKeywords.map(k => <KeywordChip key={`a-${k}`} kind="asset">{k}</KeywordChip>)}
                {r.conceptKeywords.map(k => <KeywordChip key={`c-${k}`} kind="concept">{k}</KeywordChip>)}
                {r.excludeTerms.map(k => <KeywordChip key={`x-${k}`} kind="exclude">{k}</KeywordChip>)}
                {allEmpty && <span className="text-[10.5px] text-faint">no filters configured</span>}
              </div>
            </div>
          )
        })}
        <button
          onClick={onAddRule}
          className="inline-flex h-6 items-center gap-1 rounded-sm border border-dashed border-line px-2 text-[10.5px] text-muted transition hover:border-line/80 hover:bg-surface-2 hover:text-fg"
        >
          <Plus className="size-3" />
          Add rule
        </button>
      </div>
    </div>
  )
}

function ThesisDetail({
  thesis,
  detail,
  evidence,
  loading,
  detailError,
  evidenceError,
  actionError,
  scanStatus,
  scanning,
  busyEvidenceId,
  focusedEvidenceId,
  onReview,
  onPin,
  onSetNote,
  onScan,
  onFocusEvidence,
  onBackToBriefing,
}: {
  thesis: ThesisDto | null
  detail: ThesisDetailDto | null
  evidence: ThesisEvidenceDto[]
  loading: boolean
  detailError: string | null
  evidenceError: string | null
  actionError: string | null
  scanStatus: string | null
  scanning: boolean
  busyEvidenceId: string | null
  focusedEvidenceId: string | null
  onReview: (thesisId: string, evidenceId: string, reviewStatus: ThesisEvidenceReviewStatus) => void
  onPin: (thesisId: string, evidenceId: string, isPinned: boolean) => void
  onSetNote: (thesisId: string, evidenceId: string, note: string | null) => void
  onScan: (thesisId: string) => void
  onFocusEvidence: (evidenceId: string | null) => void
  onBackToBriefing: () => void
}) {
  const active = detail ?? thesis
  const mergedEvidence = useMemo(
    () => (evidence.length > 0 ? evidence : detail?.evidence ?? []),
    [detail?.evidence, evidence],
  )
  const [filter, setFilter] = useState<EvidenceFilter>('queue')
  const counts = useMemo(() => {
    const pending = mergedEvidence.filter(e => (e.reviewStatus ?? 'pending') === 'pending').length
    const needsReview = mergedEvidence.filter(e => e.reviewStatus === 'needs_review').length
    const accepted = mergedEvidence.filter(e => e.reviewStatus === 'accepted').length
    const rejected = mergedEvidence.filter(e => e.reviewStatus === 'rejected').length
    return {
      queue: pending + needsReview,
      pending,
      needsReview,
      accepted,
      rejected,
      all: mergedEvidence.length,
    }
  }, [mergedEvidence])
  const visibleEvidence = useMemo(() => {
    const stanceRank = (s?: string | null) => {
      switch (s) {
        case 'supports': return 0
        case 'contradicts': return 1
        case 'neutral': return 2
        default: return 3
      }
    }
    const filtered = filterEvidence(mergedEvidence, filter)
    return [...filtered].sort((a, b) => {
      if (!!a.isPinned !== !!b.isPinned) return a.isPinned ? -1 : 1
      const sr = stanceRank(a.stance) - stanceRank(b.stance)
      if (sr !== 0) return sr
      const ac = a.stanceConfidence ?? 0
      const bc = b.stanceConfidence ?? 0
      if (bc !== ac) return bc - ac
      const ai = a.importance ?? 0
      const bi = b.importance ?? 0
      return bi - ai
    })
  }, [filter, mergedEvidence])

  if (!active) {
    return (
      <div className="flex h-full items-center justify-center text-[12px] text-faint">
        Select a thesis
      </div>
    )
  }

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <header className="sticky top-0 z-10 border-b border-line bg-surface px-5 py-4">
        <div className="flex items-center gap-2">
          <button
            onClick={onBackToBriefing}
            className="inline-flex h-6 items-center gap-1 rounded-sm px-1.5 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
          >
            ← Briefing
          </button>
          {active.symbol && (
            <span className="text-[12px] font-medium tabular text-muted">{active.symbol}</span>
          )}
          <StatusBadge status={active.status} />
          <span className="ml-auto inline-flex items-center gap-1 text-[10.5px] tabular text-faint">
            <span className="size-1.5 rounded-full bg-positive pulse-soft" />
            auto-scan on
          </span>
          <Button
            disabled={scanning}
            onClick={() => onScan(active.id)}
            title="Force a rescan over the last 30 days. Auto-scan runs every 60s in the background regardless."
          >
            <RefreshCw className={cn('size-3', scanning && 'animate-spin')} />
            <span className="label-xs">Rescan 30d</span>
          </Button>
        </div>
        <h2 className="mt-2 text-[14px] font-medium leading-snug text-fg">{active.title}</h2>
      </header>

      <div className="grid grid-cols-4 border-b border-line-soft bg-surface/50">
        <Metric label="Total evidence" value={fmtCount(active.evidenceCount ?? counts.all)} />
        <Metric label="Review queue" value={fmtCount(counts.queue)} accent={counts.queue > 0} />
        <Metric label="Accepted" value={fmtCount(counts.accepted)} />
        <Metric label="Rejected" value={fmtCount(counts.rejected)} />
      </div>


      {(detailError || evidenceError || actionError) && (
        <div className="space-y-1 border-b border-line-soft px-5 py-3 text-[12px] text-accent">
          {detailError && <div>Detail endpoint: {detailError}</div>}
          {evidenceError && <div>Evidence endpoint: {evidenceError}</div>}
          {actionError && <div>Review endpoint: {actionError}</div>}
        </div>
      )}

      {scanStatus && (
        <div className="border-b border-line-soft px-5 py-2 text-[11.5px] text-muted">
          {scanStatus}
        </div>
      )}

      <section className="border-b border-line-soft">
        <header className="border-b border-line-soft px-5 py-3">
          <div className="flex items-center gap-2">
            <h3 className="label-xs text-dim">Evidence</h3>
            <span className="text-[10.5px] tabular text-faint">
              {visibleEvidence.length} shown from {mergedEvidence.length}
            </span>
          </div>
          <div className="mt-3 grid grid-cols-5 overflow-hidden rounded-sm border border-line-soft bg-canvas">
            <StatusTile
              label="Queue"
              value={counts.queue}
              active={filter === 'queue'}
              onClick={() => setFilter('queue')}
            />
            <StatusTile
              label="Accepted"
              value={counts.accepted}
              active={filter === 'accepted'}
              onClick={() => setFilter('accepted')}
            />
            <StatusTile
              label="Later"
              value={counts.needsReview}
              active={filter === 'needs_review'}
              onClick={() => setFilter('needs_review')}
            />
            <StatusTile
              label="Rejected"
              value={counts.rejected}
              active={filter === 'rejected'}
              onClick={() => setFilter('rejected')}
            />
            <StatusTile
              label="All"
              value={counts.all}
              active={filter === 'all'}
              onClick={() => setFilter('all')}
            />
          </div>
        </header>
        {loading && mergedEvidence.length === 0 && (
          <div className="px-5 py-8 text-[12px] text-faint pulse-soft">Loading…</div>
        )}
        {!loading && mergedEvidence.length === 0 && (
          <div className="px-5 py-8 text-[12px] text-faint">No evidence matched this thesis yet</div>
        )}
        {!loading && mergedEvidence.length > 0 && visibleEvidence.length === 0 && (
          <div className="px-5 py-8 text-[12px] text-faint">No evidence in this state</div>
        )}
        {visibleEvidence.length > 0 && (
          <ul>
            {visibleEvidence.map(ev => (
              <EvidenceRow
                key={ev.id}
                thesisId={active.id}
                evidence={ev}
                busy={busyEvidenceId === ev.id}
                focused={focusedEvidenceId === ev.id}
                onReview={onReview}
                onPin={onPin}
                onSetNote={onSetNote}
                onFocus={onFocusEvidence}
              />
            ))}
          </ul>
        )}
      </section>

      {active.tags && active.tags.length > 0 && (
        <DetailSection title="Tags">
          <div className="flex flex-wrap gap-1.5 px-5 py-3">
            {active.tags.map(tag => (
              <span key={tag} className="rounded-sm border border-line bg-surface-2 px-1.5 py-0.5 text-[10.5px] text-muted">
                {tag}
              </span>
            ))}
          </div>
        </DetailSection>
      )}
    </div>
  )
}

function ContextRail({
  thesis,
  focusedEvidence,
}: {
  thesis: ThesisDto | null
  focusedEvidence: ThesisEvidenceDto | null
}) {
  if (!thesis) {
    return (
      <div className="flex h-full items-center justify-center px-5 text-center text-[11.5px] text-faint">
        Select a thesis to inspect context
      </div>
    )
  }
  if (!focusedEvidence) {
    return (
      <div className="flex h-full items-center justify-center px-5 text-center text-[11.5px] text-faint">
        Focus an evidence row to inspect its cluster
      </div>
    )
  }
  if (!focusedEvidence.clusterId) {
    return (
      <div className="flex h-full items-center justify-center px-5 text-center text-[11.5px] text-faint">
        This evidence is article-only, no cluster
      </div>
    )
  }
  return <ClusterDrill clusterId={focusedEvidence.clusterId} />
}

function ResearchContextPanel({
  thesis,
  focusedEvidence,
  onClose,
}: {
  thesis: ThesisDto | null
  focusedEvidence: ThesisEvidenceDto | null
  onClose: () => void
}) {
  return (
    <div className="flex h-full flex-col">
      <header className="flex h-9 shrink-0 items-center gap-2 border-b border-line-soft px-3">
        <span className="text-[10px] tracking-[0.1em] uppercase text-dim">Context</span>
        <button
          onClick={onClose}
          className="ml-auto inline-flex size-6 items-center justify-center rounded-sm text-dim transition hover:bg-surface-2 hover:text-fg"
          title="Hide context panel"
          aria-label="Hide context panel"
        >
          <PanelRightClose className="size-3.5" />
        </button>
      </header>
      <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
        <ContextRail thesis={thesis} focusedEvidence={focusedEvidence} />
      </div>
    </div>
  )
}

function fmtTimestamp(seconds: number): string {
  const m = Math.floor(seconds / 60)
  const s = Math.floor(seconds % 60)
  return `${m}:${s.toString().padStart(2, '0')}`
}

function EvidenceDrillPanel({
  evidence,
  subTrackName,
  thesisTitle,
  onClose,
}: {
  evidence: ThesisEvidenceDto
  subTrackName: string | null
  thesisTitle: string | null
  onClose: () => void
}) {
  const isSegment = evidence.evidenceKind === 'segment'
  const headline = evidence.headline ?? evidence.summary ?? null
  const sourceText = evidence.source ? sourceLabel(evidence.source) : evidence.publisher ?? null
  const timestamp = evidence.publishedAt ?? evidence.observedAt ?? evidence.createdAt

  const audioHref = evidence.audioUrl != null && evidence.startSeconds != null
    ? `${evidence.audioUrl}#t=${Math.floor(evidence.startSeconds)}`
    : evidence.audioUrl ?? undefined

  const stanceCls =
    evidence.stance === 'supports'    ? 'text-positive' :
    evidence.stance === 'contradicts' ? 'text-negative' :
    evidence.stance === 'neutral'     ? 'text-muted' :
                                        'text-faint'

  return (
    <div className="flex h-full flex-col overflow-y-auto">
      <header className="sticky top-0 z-10 border-b border-line bg-surface px-4 py-3">
        <div className="flex items-center gap-2">
          <button
            onClick={onClose}
            className="inline-flex h-7 cursor-pointer items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg active:bg-surface-2"
          >
            <ArrowLeft className="size-3" />
            Back
          </button>
          <SheetClose className="ml-auto rounded-sm p-1 text-dim hover:bg-surface-2 hover:text-fg transition cursor-pointer">
            <X className="size-3.5" />
          </SheetClose>
        </div>
        <div className="mt-2 flex flex-wrap items-center gap-1 text-[10px] text-faint">
          <span>Research</span>
          {thesisTitle && (
            <>
              <span>/</span>
              <span className="text-dim">{thesisTitle}</span>
            </>
          )}
          {subTrackName && (
            <>
              <span>/</span>
              <span className="text-dim">{subTrackName}</span>
            </>
          )}
        </div>
      </header>

      <div className="border-b border-line-soft px-4 py-4">
        <div className="flex flex-wrap items-center gap-2">
          {sourceText && (
            <span className="text-[10px] font-medium tracking-[0.085em] uppercase text-muted">{sourceText}</span>
          )}
          {evidence.sourceTier && <SourceTierLabel tier={evidence.sourceTier} />}
          {evidence.eventType && <EventTypeBadge type={evidence.eventType} />}
          {evidence.stance && (
            <span className={cn('text-[10.5px] font-medium uppercase tracking-[0.06em]', stanceCls)}>
              {evidence.stance}
            </span>
          )}
          {timestamp && (
            <span className="ml-auto text-[10.5px] tabular text-faint">{fmtAge(timestamp)}</span>
          )}
        </div>

        {isSegment ? (
          <div className="mt-2">
            <p className="text-[13px] leading-relaxed text-fg">{evidence.segmentText ?? '(no text)'}</p>
            <div className="mt-2 flex items-center gap-3">
              {evidence.startSeconds != null && (
                <span className="font-mono text-[11px] tabular text-trust">{fmtTimestamp(evidence.startSeconds)}</span>
              )}
              {evidence.speaker && (
                <span className="text-[11px] text-muted">{evidence.speaker}</span>
              )}
              {audioHref && (
                <a
                  href={audioHref}
                  target="_blank"
                  rel="noopener noreferrer"
                  className="inline-flex items-center gap-1 text-[11px] text-trust hover:text-fg"
                >
                  <ExternalLink className="size-3" />
                  Open audio
                </a>
              )}
            </div>
          </div>
        ) : (
          <div className="mt-2">
            {headline && (
              evidence.url ? (
                <a
                  href={evidence.url}
                  target="_blank"
                  rel="noreferrer"
                  className="flex items-start gap-1 text-[14px] font-medium leading-snug text-fg hover:underline underline-offset-2"
                >
                  <span>{headline}</span>
                  <ExternalLink className="mt-0.5 size-3.5 shrink-0 opacity-50" />
                </a>
              ) : (
                <p className="text-[14px] font-medium leading-snug text-fg">{headline}</p>
              )
            )}
            {evidence.summary && evidence.headline && (
              <p className="mt-2 text-[12px] leading-relaxed text-dim">{evidence.summary}</p>
            )}
          </div>
        )}

        {evidence.stanceRationale && (
          <div className="mt-3 rounded-sm border-l-2 border-line-soft bg-surface/30 px-2 py-1.5 text-[11.5px] leading-snug text-muted">
            <span className="label-xs text-dim">why </span>
            {evidence.stanceRationale}
          </div>
        )}
      </div>

      {!isSegment && evidence.clusterId && (
        <div className="flex-1">
          <div className="border-b border-line-soft px-4 py-2">
            <span className="text-[10px] tracking-[0.1em] uppercase text-dim">Cluster context</span>
          </div>
          <ClusterDrill clusterId={evidence.clusterId} />
        </div>
      )}
    </div>
  )
}

function thesisIdFromPath(): string | null {
  const match = window.location.pathname.match(/^\/research\/([0-9a-f-]{36})$/i)
  return match ? match[1] : null
}

export function ResearchView() {
  const [symbol, setSymbol] = useState<SymbolScope>({ kind: 'all' })
  const [status, setStatus] = useState<string | null>(null)
  const [scope, setScope] = useState<ScopeMode>('theses')
  const [exploring, setExploring] = useState(false)
  const [theses, setTheses] = useState<ThesisDto[] | null>(null)
  const [selected, setSelectedRaw] = useState<ThesisDto | null>(null)
  const [pendingThesisId] = useState<string | null>(() => thesisIdFromPath())
  const [detail, setDetail] = useState<ThesisDetailDto | null>(null)
  const [evidence, setEvidence] = useState<ThesisEvidenceDto[]>([])
  const [focusedEvidenceId, setFocusedEvidenceId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [detailError, setDetailError] = useState<string | null>(null)
  const [evidenceError, setEvidenceError] = useState<string | null>(null)
  const [actionError, setActionError] = useState<string | null>(null)
  const [scanStatus, setScanStatus] = useState<string | null>(null)
  const [loadingList, setLoadingList] = useState(false)
  const [loadingDetail, setLoadingDetail] = useState(false)
  const [scanningThesisId, setScanningThesisId] = useState<string | null>(null)
  const [bootstrappingThesisId, setBootstrappingThesisId] = useState<string | null>(null)
  const [busyEvidenceId, setBusyEvidenceId] = useState<string | null>(null)
  const [stamp, setStamp] = useState(0)
  const [detailStamp, setDetailStamp] = useState(0)
  const [ruleEditorOpen, setRuleEditorOpen] = useState(false)
  const [editingRule, setEditingRule] = useState<ThesisRuleDto | null>(null)
  const [viewMode, setViewMode] = useState<'briefing' | 'evidence'>('briefing')
  const [configureOpen, setConfigureOpen] = useState(false)
  const [evidenceDrillId, setEvidenceDrillId] = useState<string | null>(null)
  const [evidenceDrillSubTrack, setEvidenceDrillSubTrack] = useState<string | null>(null)
  const [leftPanelOpen, setLeftPanelOpen] = useState(true)
  const [rightPanelOpen, setRightPanelOpen] = useState(true)
  const isMobile = !useMediaQuery('(min-width: 768px)')
  const showRailInline = useMediaQuery('(min-width: 1280px)')
  const focusedEvidence = focusedEvidenceId ? evidence.find(e => e.id === focusedEvidenceId) ?? null : null
  const useDrillSheet = !showRailInline && !!focusedEvidence
  const showLeftPanel = isMobile || leftPanelOpen
  const showRightPanel = showRailInline && rightPanelOpen
  const researchGridColumns = isMobile
    ? undefined
    : [
        showLeftPanel ? '300px' : null,
        'minmax(0, 1fr)',
        showRightPanel ? '340px' : null,
      ].filter(Boolean).join(' ')

  const setSelected = useCallback((thesis: ThesisDto | null) => {
    const nextPath = thesis ? `/research/${thesis.id}` : '/research'
    if (window.location.pathname !== nextPath) {
      window.history.pushState(null, '', nextPath)
    }
    setSelectedRaw(thesis)
  }, [])

  useEffect(() => {
    const onPopState = () => {
      const id = thesisIdFromPath()
      if (!id) {
        setSelectedRaw(null)
      } else {
        setSelectedRaw(current => {
          if (current?.id === id) return current
          const found = theses?.find(t => t.id === id)
          return found ?? { id, symbol: null, title: '' } as ThesisDto
        })
      }
    }
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [theses])

  useEffect(() => {
    const ac = new AbortController()
    setLoadingList(true)
    setError(null)
    const querySymbol = symbol.kind === 'ticker' ? symbol.value : undefined
    const effectiveStatus = scope === 'explorations' ? 'exploration' : (status ?? undefined)
    fetchTheses({ symbol: querySymbol, status: effectiveStatus, take: 200 }, ac.signal)
      .then(res => {
        const filtered = scope === 'theses'
          ? res.filter(t => t.status !== 'exploration')
          : res
        setTheses(filtered)
        setLoadingList(false)
        setSelectedRaw(current => {
          if (current && filtered.some(t => t.id === current.id)) return current
          const urlId = thesisIdFromPath() ?? pendingThesisId
          if (urlId) {
            const fromUrl = filtered.find(t => t.id === urlId)
            if (fromUrl) return fromUrl
            return { id: urlId, symbol: null, title: '' } as ThesisDto
          }
          return isMobile ? null : (filtered[0] ?? null)
        })
      })
      .catch(err => {
        if (isAbort(err)) return
        setError(err instanceof Error ? err.message : 'Request failed')
        setTheses([])
        setSelected(null)
        setLoadingList(false)
      })
    return () => ac.abort()
  }, [symbol, status, scope, stamp, isMobile, pendingThesisId, setSelected])

  useEffect(() => {
    if (!selected) {
      setDetail(null)
      setEvidence([])
      setDetailError(null)
      setEvidenceError(null)
      return
    }

    const ac = new AbortController()
    setLoadingDetail(true)
    setDetail(null)
    setEvidence([])
    setFocusedEvidenceId(null)
    setViewMode('briefing')
    setDetailError(null)
    setEvidenceError(null)
    setActionError(null)
    setScanStatus(null)

    Promise.allSettled([
      fetchThesis(selected.id, ac.signal),
      fetchThesisEvidence(selected.id, ac.signal),
    ]).then(results => {
      if (ac.signal.aborted) return
      const [detailResult, evidenceResult] = results
      if (detailResult.status === 'fulfilled') {
        setDetail(detailResult.value)
      } else if (!isAbort(detailResult.reason)) {
        setDetailError(detailResult.reason instanceof Error ? detailResult.reason.message : 'Request failed')
      }

      if (evidenceResult.status === 'fulfilled') {
        setEvidence(evidenceResult.value)
      } else if (!isAbort(evidenceResult.reason)) {
        setEvidenceError(evidenceResult.reason instanceof Error ? evidenceResult.reason.message : 'Request failed')
      }
      setLoadingDetail(false)
    })

    return () => ac.abort()
  }, [selected, detailStamp])

  const symbols = useMemo(() => {
    if (!theses) return []
    const seen = new Set<string>()
    const out: string[] = []
    for (const t of theses) {
      if (t.symbol && !seen.has(t.symbol)) {
        seen.add(t.symbol)
        out.push(t.symbol)
      }
    }
    return out
  }, [theses])

  const selectedIndex = useMemo(() => {
    if (!theses || !selected) return -1
    return theses.findIndex(t => t.id === selected.id)
  }, [theses, selected])

  const selectThesisByOffset = useCallback((offset: number) => {
    if (!theses || theses.length === 0) return
    const currentIndex = selected ? theses.findIndex(t => t.id === selected.id) : -1
    const baseIndex = currentIndex >= 0 ? currentIndex : (offset > 0 ? -1 : 0)
    const nextIndex = (baseIndex + offset + theses.length) % theses.length
    setSelected(theses[nextIndex])
  }, [selected, theses, setSelected])

  useEffect(() => {
    if (isMobile || leftPanelOpen) return

    const isEditableTarget = (target: EventTarget | null) => {
      if (!(target instanceof HTMLElement)) return false
      const tag = target.tagName.toLowerCase()
      return tag === 'input' ||
        tag === 'textarea' ||
        tag === 'select' ||
        target.isContentEditable ||
        !!target.closest('[role="textbox"], [contenteditable="true"]')
    }

    const onKeyDown = (e: KeyboardEvent) => {
      if (e.altKey || e.ctrlKey || e.metaKey || e.shiftKey || isEditableTarget(e.target)) return
      if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') {
        e.preventDefault()
        selectThesisByOffset(-1)
      } else if (e.key === 'ArrowRight' || e.key === 'ArrowDown') {
        e.preventDefault()
        selectThesisByOffset(1)
      }
    }

    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [isMobile, leftPanelOpen, selectThesisByOffset])

  const applyEvidenceUpdate = (evidenceId: string, updated: Partial<ThesisEvidenceDto>) => {
    setEvidence(current => current.map(ev => ev.id === evidenceId ? { ...ev, ...updated } : ev))
    setDetail(current => current?.evidence
      ? { ...current, evidence: current.evidence.map(ev => ev.id === evidenceId ? { ...ev, ...updated } : ev) }
      : current)
  }

  const sendReview = (
    thesisId: string,
    evidenceId: string,
    command: ReviewThesisEvidenceCommand,
  ) => {
    setBusyEvidenceId(evidenceId)
    setActionError(null)
    reviewThesisEvidence(thesisId, evidenceId, command)
      .then(updated => {
        applyEvidenceUpdate(evidenceId, updated)
        setBusyEvidenceId(null)
      })
      .catch(err => {
        if (isAbort(err)) return
        setActionError(err instanceof Error ? err.message : 'Request failed')
        setBusyEvidenceId(null)
      })
  }

  const handleReview = (thesisId: string, evidenceId: string, reviewStatus: ThesisEvidenceReviewStatus) => {
    sendReview(thesisId, evidenceId, { reviewStatus })
  }

  const handlePin = (thesisId: string, evidenceId: string, isPinned: boolean) => {
    const ev = evidence.find(e => e.id === evidenceId)
    sendReview(thesisId, evidenceId, {
      reviewStatus: (ev?.reviewStatus as ThesisEvidenceReviewStatus) ?? 'pending',
      isPinned,
    })
  }

  const handleSetNote = (thesisId: string, evidenceId: string, note: string | null) => {
    const ev = evidence.find(e => e.id === evidenceId)
    sendReview(thesisId, evidenceId, {
      reviewStatus: (ev?.reviewStatus as ThesisEvidenceReviewStatus) ?? 'pending',
      reviewerNote: note ?? '',
    })
  }

  const handleBootstrap = (thesisId: string) => {
    setBootstrappingThesisId(thesisId)
    setActionError(null)
    bootstrapThesis(thesisId)
      .then(result => {
        setBootstrappingThesisId(null)
        if (result.error) {
          setActionError(`Bootstrap failed: ${result.error}`)
        } else {
          setDetailStamp(s => s + 1)
          setStamp(s => s + 1)
        }
      })
      .catch(err => {
        if (isAbort(err)) return
        setActionError(err instanceof Error ? err.message : 'Bootstrap failed')
        setBootstrappingThesisId(null)
      })
  }

  useEffect(() => {
    if (!selected) return
    const planReady = detail?.plan != null || selected.planStatus === 'ready'
    if (planReady) return
    const interval = setInterval(() => {
      setDetailStamp(s => s + 1)
    }, 8000)
    return () => clearInterval(interval)
  }, [selected, detail?.plan, selected?.planStatus])

  const handleScan = (thesisId: string) => {
    setScanningThesisId(thesisId)
    setActionError(null)
    setScanStatus(null)
    scanThesis(thesisId)
      .then(result => {
        setScanStatus(
          `Scanned ${result.articlesScanned} articles and ${result.eventsScanned} events; added ${result.evidenceAdded} evidence items.`,
        )
        return fetchThesisEvidence(thesisId)
      })
      .then(res => {
        setEvidence(res)
        setScanningThesisId(null)
        setStamp(s => s + 1)
      })
      .catch(err => {
        setActionError(err instanceof Error ? err.message : 'Request failed')
        setScanningThesisId(null)
      })
  }

  return (
    <div className="flex h-full flex-col">
      <ResearchFilterBar
        symbol={symbol}
        status={status}
        count={theses?.length ?? 0}
        loading={loadingList}
        symbols={symbols}
        scope={scope}
        leftPanelOpen={leftPanelOpen}
        rightPanelOpen={rightPanelOpen}
        canShowRightPanel={showRailInline}
        navigatorThesis={selected}
        navigatorIndex={selectedIndex}
        navigatorTotal={theses?.length ?? 0}
        onSymbolChange={setSymbol}
        onStatusChange={setStatus}
        onScopeChange={setScope}
        onToggleLeftPanel={() => setLeftPanelOpen(open => !open)}
        onToggleRightPanel={() => setRightPanelOpen(open => !open)}
        onPreviousThesis={() => selectThesisByOffset(-1)}
        onNextThesis={() => selectThesisByOffset(1)}
        onRefresh={() => setStamp(s => s + 1)}
        onExplore={() => setExploring(true)}
      />

      {error ? (
        <div className="flex items-center gap-2 px-5 py-8 text-[13px] text-negative">
          <AlertCircle className="size-4" />
          <span>Theses endpoint: {error}</span>
        </div>
      ) : !theses ? (
        <div className="px-5 py-8 text-[12px] text-faint pulse-soft">Loading…</div>
      ) : (
        <div
          className="grid min-h-0 flex-1 grid-cols-1"
          style={researchGridColumns ? { gridTemplateColumns: researchGridColumns } : undefined}
        >
          {showLeftPanel && (
            <aside
              className={cn(
                'min-h-0 overflow-y-auto overflow-x-hidden border-r border-line-soft md:block',
                isMobile && selected ? 'hidden' : 'block',
              )}
            >
              <ThesisList theses={theses} selectedId={selected?.id ?? null} onSelect={setSelected} />
            </aside>
          )}
          <main
            className={cn(
              'min-h-0 bg-surface',
              isMobile && !selected ? 'hidden' : 'block',
            )}
          >
            {viewMode === 'briefing' ? (
              <ThesisBriefing
                thesis={selected}
                detail={detail}
                evidence={evidence}
                loading={loadingDetail}
                onOpenEvidence={(id, subTrackName) => {
                  setEvidenceDrillId(id)
                  setEvidenceDrillSubTrack(subTrackName)
                }}
                onOpenAllEvidence={() => setViewMode('evidence')}
                onOpenConfigure={() => setConfigureOpen(true)}
                onBootstrap={() => selected && handleBootstrap(selected.id)}
                bootstrapping={bootstrappingThesisId === selected?.id}
                onBackToList={isMobile ? () => setSelected(null) : undefined}
              />
            ) : (
              <ThesisDetail
                thesis={selected}
                detail={detail}
                evidence={evidence}
                loading={loadingDetail}
                detailError={detailError}
                evidenceError={evidenceError}
                actionError={actionError}
                scanStatus={scanStatus}
                scanning={scanningThesisId === selected?.id}
                busyEvidenceId={busyEvidenceId}
                focusedEvidenceId={focusedEvidenceId}
                onReview={handleReview}
                onPin={handlePin}
                onSetNote={handleSetNote}
                onScan={handleScan}
                onFocusEvidence={setFocusedEvidenceId}
                onBackToBriefing={() => setViewMode('briefing')}
              />
            )}
          </main>
          {showRightPanel && (
            <aside className="hidden min-h-0 border-l border-line-soft bg-canvas xl:block">
              <ResearchContextPanel
                thesis={selected}
                focusedEvidence={focusedEvidenceId ? evidence.find(e => e.id === focusedEvidenceId) ?? null : null}
                onClose={() => setRightPanelOpen(false)}
              />
            </aside>
          )}
        </div>
      )}

      <Sheet
        open={!!evidenceDrillId && !!evidence.find(e => e.id === evidenceDrillId)}
        onOpenChange={open => { if (!open) { setEvidenceDrillId(null); setEvidenceDrillSubTrack(null) } }}
        width={520}
      >
        {evidenceDrillId && (() => {
          const ev = evidence.find(e => e.id === evidenceDrillId)
          if (!ev) return null
          return (
            <EvidenceDrillPanel
              evidence={ev}
              subTrackName={evidenceDrillSubTrack}
              thesisTitle={selected?.title ?? null}
              onClose={() => { setEvidenceDrillId(null); setEvidenceDrillSubTrack(null) }}
            />
          )
        })()}
      </Sheet>

      <Sheet
        open={exploring}
        onOpenChange={open => { if (!open) setExploring(false) }}
        width={620}
      >
        <ExplorationScreen
          onCancel={() => setExploring(false)}
          onPromoted={(thesisId) => {
            setExploring(false)
            setScope('theses')
            setStamp(s => s + 1)
            setSelected({ id: thesisId, symbol: null, title: '' } as ThesisDto)
            setDetailStamp(s => s + 1)
          }}
        />
      </Sheet>

      <Sheet
        open={useDrillSheet}
        onOpenChange={open => { if (!open) setFocusedEvidenceId(null) }}
        width={420}
      >
        <div className="flex h-full flex-col">
          <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-line bg-surface px-4 py-3">
            <h3 className="text-[12px] font-medium">Cluster</h3>
            <SheetClose className="ml-auto rounded-sm p-1 text-dim hover:bg-surface-2 hover:text-fg transition">
              <X className="size-3.5" />
            </SheetClose>
          </header>
          <div className="flex-1 overflow-y-auto">
            <ContextRail thesis={selected} focusedEvidence={focusedEvidence} />
          </div>
        </div>
      </Sheet>

      {selected && (
        <RuleEditor
          open={ruleEditorOpen}
          thesisId={selected.id}
          rule={editingRule}
          onClose={() => setRuleEditorOpen(false)}
          onSaved={() => { setDetailStamp(s => s + 1); setStamp(s => s + 1) }}
        />
      )}

      {selected && (
        <Sheet open={configureOpen} onOpenChange={setConfigureOpen} width={560}>
          <div className="flex h-full flex-col">
            <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-line bg-surface px-5 py-3">
              <h3 className="text-[13px] font-medium">Configure thesis</h3>
              <span className="text-[11px] text-faint">{selected.title}</span>
              <SheetClose className="ml-auto rounded-sm p-1 text-dim hover:bg-surface-2 hover:text-fg transition">
                <X className="size-3.5" />
              </SheetClose>
            </header>
            <div className="flex-1 overflow-y-auto">
              <ScopeStrip
                detail={detail}
                onEditRule={r => { setEditingRule(r); setRuleEditorOpen(true) }}
                onAddRule={() => { setEditingRule(null); setRuleEditorOpen(true) }}
              />
            </div>
            <footer className="sticky bottom-0 flex items-center justify-between border-t border-line bg-surface px-5 py-3">
              <button
                onClick={() => handleScan(selected.id)}
                disabled={scanningThesisId === selected.id}
                className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
              >
                <RefreshCw className={cn('size-3', scanningThesisId === selected.id && 'animate-spin')} />
                Rescan 30d
              </button>
              {scanStatus && <span className="text-[10.5px] text-faint">{scanStatus}</span>}
            </footer>
          </div>
        </Sheet>
      )}
    </div>
  )
}
