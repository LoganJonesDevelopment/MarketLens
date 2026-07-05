import { useEffect, useMemo, useState, type ReactNode } from 'react'
import { AlertCircle, RefreshCw, RotateCcw, Trash2, XCircle } from 'lucide-react'
import {
  Bar, BarChart, Cell, ResponsiveContainer, XAxis, YAxis, Tooltip as RTooltip,
} from 'recharts'
import {
  cancelPipelineQueueItem,
  fetchPipelineRuns,
  fetchPipelineQueueAttempts,
  fetchPipelineQueueItems,
  fetchPipelineQueueSummary,
  fetchPipelineStages,
  fetchStats,
  purgeCompletedPipelineQueue,
  retryPipelineQueueItem,
  type PipelineQueueAttempt,
  type PipelineQueueItem,
  type PipelineQueueStatus,
  type PipelineQueueSummaryDto,
  type PipelineRunRow,
  type PipelineStageRow,
  type StatsDto,
} from '../../lib/api'
import { eventLabel, fmtAge, fmtCount, sourceLabel } from '../../lib/format'
import { cn } from '../../lib/cn'
import { Select, type SelectOption } from '../../components/Select'

function Stage({
  label, value, sub, accent,
}: { label: string; value: number; sub?: string; accent?: boolean }) {
  return (
    <div className="bg-surface px-3 py-3 sm:px-5 sm:py-4">
      <div className="label-xs text-dim">{label}</div>
      <div className={cn('mt-1 text-[20px] font-medium tabular leading-none sm:text-[26px]', accent && 'text-accent')}>
        {fmtCount(value)}
      </div>
      {sub && <div className="mt-1 text-[10.5px] tabular text-faint">{sub}</div>}
    </div>
  )
}

function Panel({
  title, action, children,
}: { title: string; action?: ReactNode; children: ReactNode }) {
  return (
    <section className="rounded-sm border border-line bg-surface">
      <header className="flex items-center justify-between border-b border-line-soft px-4 py-2.5">
        <h3 className="label-xs text-dim">{title}</h3>
        {action}
      </header>
      <div className="p-4">{children}</div>
    </section>
  )
}

const RTooltipStyles = {
  contentStyle: {
    background: 'var(--color-surface-2)',
    border: '1px solid var(--color-line)',
    borderRadius: 2,
    fontSize: 11,
    padding: '6px 8px',
    color: 'var(--color-fg)',
  },
  itemStyle: { color: 'var(--color-fg)' },
  labelStyle: { color: 'var(--color-dim)', fontSize: 10, textTransform: 'uppercase' as const, letterSpacing: '0.085em' },
  cursor: { fill: 'var(--color-line-soft)', opacity: 0.4 },
}

function EventTypeChart({ data }: { data: StatsDto['byEventType'] }) {
  const top = data
    .slice()
    .sort((a, b) => b.count - a.count)
    .slice(0, 12)
    .map(d => ({ ...d, label: eventLabel(d.eventType) }))

  if (top.length === 0) {
    return <p className="label-xs text-faint">No events yet</p>
  }

  return (
    <ResponsiveContainer width="100%" height={Math.max(120, top.length * 26)}>
      <BarChart data={top} layout="vertical" margin={{ top: 4, right: 24, left: 8, bottom: 4 }}>
        <XAxis type="number" hide />
        <YAxis
          type="category"
          dataKey="label"
          width={84}
          tick={{ fontSize: 11, fill: 'var(--color-muted)' }}
          axisLine={false}
          tickLine={false}
        />
        <RTooltip
          {...RTooltipStyles}
          formatter={((v: unknown, _name: unknown, p: { payload?: { avgImportance?: number; avgSentiment?: number } }) => {
            const imp = p.payload?.avgImportance ?? 0
            const sent = p.payload?.avgSentiment ?? 0
            return [
              `${v} · imp ${imp.toFixed(2)} · sent ${sent >= 0 ? '+' : ''}${sent.toFixed(2)}`,
              'count',
            ]
          }) as never}
        />
        <Bar dataKey="count" radius={[0, 2, 2, 0]} barSize={14}>
          {top.map((d, i) => (
            <Cell
              key={i}
              fill={d.avgImportance >= 0.6 ? 'var(--color-accent)'
                : d.avgImportance >= 0.4 ? 'oklch(0.78 0.10 85)'
                : 'var(--color-line)'}
            />
          ))}
        </Bar>
      </BarChart>
    </ResponsiveContainer>
  )
}

function SourceMixChart({ data }: { data: StatsDto['bySource'] }) {
  const top = data.slice().sort((a, b) => b.count - a.count).map(d => ({ ...d, label: sourceLabel(d.source) }))

  if (top.length === 0) {
    return <p className="label-xs text-faint">No articles yet</p>
  }

  return (
    <ResponsiveContainer width="100%" height={Math.max(120, top.length * 26)}>
      <BarChart data={top} layout="vertical" margin={{ top: 4, right: 24, left: 8, bottom: 4 }}>
        <XAxis type="number" hide />
        <YAxis
          type="category"
          dataKey="label"
          width={84}
          tick={{ fontSize: 11, fill: 'var(--color-muted)' }}
          axisLine={false}
          tickLine={false}
        />
        <RTooltip {...RTooltipStyles} />
        <Bar dataKey="count" radius={[0, 2, 2, 0]} barSize={14} fill="var(--color-trust)" />
      </BarChart>
    </ResponsiveContainer>
  )
}

function statusTone(status: string) {
  if (status === 'completed') return 'text-positive border-positive/30 bg-positive/5'
  if (status === 'queued') return 'text-muted border-line bg-surface-2'
  if (status === 'succeeded') return 'text-positive border-positive/30 bg-positive/5'
  if (status === 'succeeded_with_errors') return 'text-accent border-accent/30 bg-accent/5'
  if (status === 'running') return 'text-accent border-accent/30 bg-accent/5'
  if (status === 'cancelled') return 'text-faint border-line bg-canvas'
  return 'text-negative border-negative/30 bg-negative/5'
}

function fmtDateTime(value: string | null) {
  if (!value) return '—'
  return new Date(value).toLocaleString([], {
    month: 'short',
    day: 'numeric',
    hour: '2-digit',
    minute: '2-digit',
  })
}

function fmtDuration(value: number | null) {
  if (value == null) return '—'
  if (value < 1000) return `${Math.round(value)}ms`
  return `${(value / 1000).toFixed(value < 10000 ? 1 : 0)}s`
}

function StageRuns({ stages }: { stages: PipelineStageRow[] }) {
  if (stages.length === 0) {
    return <p className="label-xs text-faint">No run records yet</p>
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[620px] text-left text-[12px]">
        <thead className="label-xs text-faint">
          <tr className="[&>th]:pb-2 [&>th]:font-medium">
            <th>Stage</th>
            <th>Status</th>
            <th className="text-right">In</th>
            <th className="text-right">Out</th>
            <th className="text-right">Errors</th>
            <th className="text-right">Latest</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line-soft">
          {stages.map(stage => (
            <tr key={stage.stage} className="[&>td]:py-2.5">
              <td className="font-medium text-fg">{stage.stage.replaceAll('_', ' ')}</td>
              <td>
                <span className={cn('inline-flex rounded-sm border px-1.5 py-0.5 text-[10.5px]', statusTone(stage.latestStatus))}>
                  {stage.latestStatus.replaceAll('_', ' ')}
                </span>
              </td>
              <td className="text-right tabular text-muted">{fmtCount(stage.inputCount)}</td>
              <td className="text-right tabular text-muted">{fmtCount(stage.outputCount)}</td>
              <td className="text-right tabular text-muted">{fmtCount(stage.errorCount)}</td>
              <td className="text-right tabular text-faint">{fmtDateTime(stage.latestStartedAt)}</td>
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function RecentRuns({ runs }: { runs: PipelineRunRow[] }) {
  if (runs.length === 0) {
    return <p className="label-xs text-faint">No recent runs</p>
  }

  return (
    <div className="space-y-2">
      {runs.map(run => (
        <div key={run.id} className="grid grid-cols-[1fr_auto] gap-3 border-b border-line-soft pb-2 last:border-b-0 last:pb-0">
          <div className="min-w-0">
            <div className="flex min-w-0 items-center gap-2">
              <span className="truncate text-[12px] font-medium text-fg">{run.stage.replaceAll('_', ' ')}</span>
              <span className={cn('shrink-0 rounded-sm border px-1.5 py-0.5 text-[10px]', statusTone(run.status))}>
                {run.status.replaceAll('_', ' ')}
              </span>
            </div>
            {run.errorMessage && <div className="mt-1 truncate text-[11px] text-negative">{run.errorMessage}</div>}
            <div className="mt-1 text-[10.5px] tabular text-faint">
              {fmtDateTime(run.startedAt)} · {fmtDuration(run.durationMs)}
            </div>
          </div>
          <div className="text-right text-[11px] tabular text-muted">
            <div>{fmtCount(run.inputCount)} in</div>
            <div>{fmtCount(run.outputCount)} out</div>
          </div>
        </div>
      ))}
    </div>
  )
}

function workLabel(value: string) {
  return value.replaceAll('_', ' ')
}

function statusLabel(value: string) {
  return value.replaceAll('_', ' ')
}

function QueueCounts({ summary }: { summary: PipelineQueueSummaryDto }) {
  const statuses = ['queued', 'running', 'dead_letter', 'cancelled', 'completed']
  const workTypes = Array.from(new Set(summary.counts.map(c => c.workType))).sort()

  if (workTypes.length === 0) {
    return <p className="label-xs text-faint">No queue records yet</p>
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[560px] text-left text-[12px]">
        <thead className="label-xs text-faint">
          <tr className="[&>th]:pb-2 [&>th]:font-medium">
            <th>Type</th>
            {statuses.map(status => (
              <th key={status} className="text-right">{statusLabel(status)}</th>
            ))}
          </tr>
        </thead>
        <tbody className="divide-y divide-line-soft">
          {workTypes.map(workType => (
            <tr key={workType} className="[&>td]:py-2">
              <td className="font-medium text-fg">{workLabel(workType)}</td>
              {statuses.map(status => {
                const count = summary.counts.find(c => c.workType === workType && c.status === status)?.count ?? 0
                return <td key={status} className="text-right tabular text-muted">{count > 0 ? fmtCount(count) : '—'}</td>
              })}
            </tr>
          ))}
        </tbody>
      </table>
    </div>
  )
}

function QueueProblems({
  summary,
  onRetry,
  actionId,
}: {
  summary: PipelineQueueSummaryDto
  onRetry: (id: string) => void
  actionId: string | null
}) {
  const problems = summary.recentDeadLetters

  return (
    <div className="space-y-3">
      <div>
        <div className="mb-2 label-xs text-faint">Dead letters</div>
        {problems.length === 0 ? (
          <p className="text-[12px] text-faint">None</p>
        ) : (
          <div className="space-y-2">
            {problems.map(item => (
              <div key={item.id} className="grid grid-cols-[1fr_auto] gap-3 border-b border-line-soft pb-2 last:border-b-0 last:pb-0">
                <div className="min-w-0">
                  <div className="truncate text-[12px] font-medium text-fg">{workLabel(item.workType)}</div>
                  <div className="truncate text-[11px] text-muted">{item.naturalKey}</div>
                  {item.lastError && <div className="mt-1 truncate text-[11px] text-negative">{item.lastError}</div>}
                </div>
                <button
                  onClick={() => onRetry(item.id)}
                  disabled={actionId === `retry:${item.id}`}
                  className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
                >
                  <RotateCcw className="size-3" />
                  Retry
                </button>
              </div>
            ))}
          </div>
        )}
      </div>

      <div>
        <div className="mb-2 label-xs text-faint">Recent errors</div>
        {summary.recentErrors.length === 0 ? (
          <p className="text-[12px] text-faint">None</p>
        ) : (
          <div className="space-y-2">
            {summary.recentErrors.map(attempt => (
              <div key={attempt.id} className="border-b border-line-soft pb-2 last:border-b-0 last:pb-0">
                <div className="flex items-center gap-2">
                  <span className="truncate text-[12px] font-medium text-fg">{workLabel(attempt.workType)}</span>
                  <span className={cn('shrink-0 rounded-sm border px-1.5 py-0.5 text-[10px]', statusTone(attempt.status))}>
                    {statusLabel(attempt.status)}
                  </span>
                </div>
                <div className="truncate text-[11px] text-muted">{attempt.naturalKey}</div>
                {attempt.errorMessage && <div className="mt-1 truncate text-[11px] text-negative">{attempt.errorMessage}</div>}
                <div className="mt-1 text-[10.5px] tabular text-faint">{fmtDateTime(attempt.finishedAt ?? attempt.startedAt)}</div>
              </div>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function QueueAttempts({ attempts }: { attempts: PipelineQueueAttempt[] }) {
  if (attempts.length === 0) {
    return <p className="label-xs text-faint">Select a queue item to inspect attempts</p>
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[520px] text-left text-[12px]">
        <thead className="label-xs text-faint">
          <tr className="[&>th]:pb-2 [&>th]:font-medium">
            <th>Attempt</th>
            <th>Status</th>
            <th>Worker</th>
            <th className="text-right">Started</th>
            <th className="text-right">Duration</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line-soft">
          {attempts.map(attempt => (
            <tr key={attempt.id} className="[&>td]:py-2">
              <td className="tabular text-fg">#{attempt.attemptNumber}</td>
              <td>
                <span className={cn('inline-flex rounded-sm border px-1.5 py-0.5 text-[10.5px]', statusTone(attempt.status))}>
                  {statusLabel(attempt.status)}
                </span>
              </td>
              <td className="max-w-[120px] truncate text-muted">{attempt.workerId}</td>
              <td className="text-right tabular text-faint">{fmtDateTime(attempt.startedAt)}</td>
              <td className="text-right tabular text-muted">{fmtDuration(attempt.durationMs)}</td>
            </tr>
          ))}
        </tbody>
      </table>
      {attempts[0]?.errorMessage && <div className="mt-2 truncate text-[11px] text-negative">{attempts[0].errorMessage}</div>}
    </div>
  )
}

function QueueItems({
  items,
  selectedId,
  actionId,
  onSelect,
  onRetry,
  onCancel,
}: {
  items: PipelineQueueItem[]
  selectedId: string | null
  actionId: string | null
  onSelect: (id: string) => void
  onRetry: (id: string) => void
  onCancel: (id: string) => void
}) {
  if (items.length === 0) {
    return <p className="label-xs text-faint">No queue items match the filters</p>
  }

  return (
    <div className="overflow-x-auto">
      <table className="w-full min-w-[820px] text-left text-[12px]">
        <thead className="label-xs text-faint">
          <tr className="[&>th]:pb-2 [&>th]:font-medium">
            <th>Type</th>
            <th>Key</th>
            <th>Status</th>
            <th className="text-right">Attempts</th>
            <th className="text-right">Available</th>
            <th className="text-right">Updated</th>
            <th className="text-right">Actions</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-line-soft">
          {items.map(item => {
            const canRetry = item.status === 'dead_letter' || item.status === 'cancelled'
            const canCancel = item.status === 'queued' || item.status === 'running'
            return (
              <tr
                key={item.id}
                onClick={() => onSelect(item.id)}
                className={cn(
                  'cursor-pointer [&>td]:py-2.5',
                  selectedId === item.id && 'bg-surface-2',
                )}
              >
                <td className="font-medium text-fg">{workLabel(item.workType)}</td>
                <td className="max-w-[220px] truncate text-muted">{item.naturalKey}</td>
                <td>
                  <span className={cn('inline-flex rounded-sm border px-1.5 py-0.5 text-[10.5px]', statusTone(item.status))}>
                    {statusLabel(item.status)}
                  </span>
                </td>
                <td className="text-right tabular text-muted">{item.attemptCount}/{item.maxAttempts}</td>
                <td className="text-right tabular text-faint">{fmtDateTime(item.availableAt)}</td>
                <td className="text-right tabular text-faint">{fmtAge(item.updatedAt)}</td>
                <td className="text-right">
                  <div className="inline-flex items-center gap-1">
                    {canRetry && (
                      <button
                        onClick={(event) => { event.stopPropagation(); onRetry(item.id) }}
                        disabled={actionId === `retry:${item.id}`}
                        className="inline-flex h-7 items-center justify-center rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
                        title="Retry"
                      >
                        <RotateCcw className="size-3" />
                      </button>
                    )}
                    {canCancel && (
                      <button
                        onClick={(event) => { event.stopPropagation(); onCancel(item.id) }}
                        disabled={actionId === `cancel:${item.id}`}
                        className="inline-flex h-7 items-center justify-center rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
                        title="Cancel"
                      >
                        <XCircle className="size-3" />
                      </button>
                    )}
                  </div>
                </td>
              </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}

export function PipelineView() {
  const [stats, setStats] = useState<StatsDto | null>(null)
  const [stages, setStages] = useState<PipelineStageRow[]>([])
  const [runs, setRuns] = useState<PipelineRunRow[]>([])
  const [queueSummary, setQueueSummary] = useState<PipelineQueueSummaryDto | null>(null)
  const [queueItems, setQueueItems] = useState<PipelineQueueItem[]>([])
  const [queueAttempts, setQueueAttempts] = useState<PipelineQueueAttempt[]>([])
  const [queueStatus, setQueueStatus] = useState<string | null>('queued')
  const [queueWorkType, setQueueWorkType] = useState<string | null>(null)
  const [queueSearch, setQueueSearch] = useState('')
  const [selectedQueueItemId, setSelectedQueueItemId] = useState<string | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [queueLoading, setQueueLoading] = useState(false)
  const [actionId, setActionId] = useState<string | null>(null)
  const [stamp, setStamp] = useState(0)

  useEffect(() => {
    const ac = new AbortController()
    setLoading(true)
    setError(null)
    Promise.all([
      fetchStats(ac.signal),
      fetchPipelineStages(ac.signal),
      fetchPipelineRuns(ac.signal),
    ])
      .then(([statsRes, stagesRes, runsRes]) => {
        setStats(statsRes)
        setStages(stagesRes.stages)
        setRuns(runsRes.runs)
        setLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setLoading(false)
      })
    return () => ac.abort()
  }, [stamp])

  useEffect(() => {
    const ac = new AbortController()
    setQueueLoading(true)
    setError(null)
    Promise.all([
      fetchPipelineQueueSummary(ac.signal),
      fetchPipelineQueueItems({
        workType: queueWorkType,
        status: queueStatus,
        q: queueSearch.trim() || undefined,
        take: 40,
      }, ac.signal),
    ])
      .then(([summaryRes, itemsRes]) => {
        setQueueSummary(summaryRes)
        setQueueItems(itemsRes.items)
        setSelectedQueueItemId(current => {
          if (current && itemsRes.items.some(item => item.id === current)) return current
          return itemsRes.items[0]?.id ?? null
        })
        setQueueLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setQueueLoading(false)
      })
    return () => ac.abort()
  }, [stamp, queueStatus, queueWorkType, queueSearch])

  useEffect(() => {
    if (!selectedQueueItemId) {
      setQueueAttempts([])
      return
    }

    const ac = new AbortController()
    fetchPipelineQueueAttempts(selectedQueueItemId, ac.signal)
      .then(res => setQueueAttempts(res.attempts))
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setQueueAttempts([])
      })
    return () => ac.abort()
  }, [selectedQueueItemId, stamp])

  const queueWorkTypeOptions = useMemo<SelectOption[]>(() => {
    const workTypes = Array.from(new Set(queueSummary?.counts.map(c => c.workType) ?? [])).sort()
    return [
      { value: null, label: 'All types' },
      ...workTypes.map(workType => ({ value: workType, label: workLabel(workType) })),
    ]
  }, [queueSummary])

  const queueStatusOptions = useMemo<SelectOption[]>(() => {
    const statuses = Array.from(new Set([
      'queued',
      'running',
      'dead_letter',
      'cancelled',
      'completed',
      ...(queueSummary?.counts.map(c => c.status) ?? []),
    ])).sort()
    return [
      { value: null, label: 'All statuses' },
      ...statuses.map(status => ({ value: status, label: statusLabel(status) })),
    ]
  }, [queueSummary])

  const queueTotals = useMemo(() => {
    const counts = queueSummary?.counts ?? []
    const total = (status: PipelineQueueStatus) =>
      counts.filter(c => c.status === status).reduce((sum, c) => sum + c.count, 0)
    return {
      queued: total('queued'),
      running: total('running'),
      deadLetter: total('dead_letter'),
      completed: total('completed'),
    }
  }, [queueSummary])

  async function runQueueAction(kind: 'retry' | 'cancel', id: string) {
    setActionId(`${kind}:${id}`)
    try {
      if (kind === 'retry') await retryPipelineQueueItem(id)
      else await cancelPipelineQueueItem(id)
      setStamp(s => s + 1)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Queue action failed')
    } finally {
      setActionId(null)
    }
  }

  async function purgeCompleted() {
    setActionId('purge')
    try {
      await purgeCompletedPipelineQueue(168, 1000)
      setStamp(s => s + 1)
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Queue purge failed')
    } finally {
      setActionId(null)
    }
  }

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <div className="sticky top-0 z-10 flex items-center gap-3 border-b border-line-soft bg-canvas/90 px-3 py-2.5 backdrop-blur sm:px-5">
        <h2 className="label-xs text-dim">Pipeline</h2>
        <button
          onClick={() => setStamp(s => s + 1)}
          className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted hover:text-fg hover:bg-surface-2 transition"
        >
          <RefreshCw className={cn('size-3', loading && 'animate-spin')} />
          <span className="label-xs hidden sm:inline">Refresh</span>
        </button>
      </div>

      <div className="p-3 sm:p-6">
        {error && (
          <div className="flex items-center gap-2 text-[13px] text-negative">
            <AlertCircle className="size-4" />
            <span>API error: {error}</span>
          </div>
        )}

        {!error && !stats && (
          <div className="text-[12px] text-faint pulse-soft">Loading…</div>
        )}

        {stats && (
          <div className="space-y-6">
            <div className="grid grid-cols-2 sm:grid-cols-3 md:grid-cols-5 gap-px overflow-hidden rounded-sm border border-line bg-line-soft">
              <Stage label="Articles" value={stats.articles} />
              <Stage
                label="Clusters"
                value={stats.clusters}
                sub={
                  stats.articles > 0
                    ? `compression ${(stats.clusterCompressionRatio * 100).toFixed(0)}%`
                    : undefined
                }
              />
              <Stage label="Triaged" value={stats.triaged} />
              <Stage
                label="Pending"
                value={stats.pendingExtraction}
                accent={stats.pendingExtraction > 0}
              />
              <Stage label="Events" value={stats.events} />
            </div>

            <div className="grid grid-cols-1 gap-6 xl:grid-cols-2">
              <div className="xl:col-span-2">
                <Panel
                  title="Local queue"
                  action={(
                    <div className="flex items-center gap-2">
                      {queueLoading && <RefreshCw className="size-3 animate-spin text-faint" />}
                      <button
                        onClick={purgeCompleted}
                        disabled={actionId === 'purge'}
                        className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
                      >
                        <Trash2 className="size-3" />
                        <span className="hidden sm:inline">Purge 7d completed</span>
                      </button>
                    </div>
                  )}
                >
                  {queueSummary ? (
                    <div className="space-y-4">
                      <div className="grid grid-cols-2 gap-px overflow-hidden rounded-sm border border-line bg-line-soft md:grid-cols-5">
                        <Stage label="Queued" value={queueTotals.queued} accent={queueTotals.queued > 0} />
                        <Stage label="Running" value={queueTotals.running} accent={queueTotals.running > 0} />
                        <Stage label="Dead letters" value={queueTotals.deadLetter} accent={queueTotals.deadLetter > 0} />
                        <Stage label="Completed" value={queueTotals.completed} />
                        <div className="bg-surface px-3 py-3 sm:px-5 sm:py-4">
                          <div className="label-xs text-dim">Oldest pending</div>
                          <div className="mt-1 truncate text-[12px] font-medium text-fg">
                            {queueSummary.oldestPending ? workLabel(queueSummary.oldestPending.workType) : '—'}
                          </div>
                          <div className="mt-1 truncate text-[10.5px] tabular text-faint">
                            {queueSummary.oldestPending ? `${fmtAge(queueSummary.oldestPending.availableAt)} · ${queueSummary.oldestPending.naturalKey}` : 'No queued work'}
                          </div>
                        </div>
                      </div>

                      <div className="grid grid-cols-1 gap-4 2xl:grid-cols-[1fr_360px]">
                        <div className="space-y-4">
                          <div className="flex flex-wrap items-center gap-2">
                            <Select value={queueStatus} options={queueStatusOptions} onChange={setQueueStatus} width={150} />
                            <Select value={queueWorkType} options={queueWorkTypeOptions} onChange={setQueueWorkType} width={190} />
                            <input
                              value={queueSearch}
                              onChange={event => setQueueSearch(event.target.value)}
                              placeholder="Filter key"
                              className="h-7 w-44 rounded-sm border border-line bg-canvas px-2 text-[12px] text-fg outline-none transition placeholder:text-faint focus:border-accent/60"
                            />
                          </div>

                          <QueueCounts summary={queueSummary} />
                          <QueueItems
                            items={queueItems}
                            selectedId={selectedQueueItemId}
                            actionId={actionId}
                            onSelect={setSelectedQueueItemId}
                            onRetry={(id) => void runQueueAction('retry', id)}
                            onCancel={(id) => void runQueueAction('cancel', id)}
                          />
                        </div>

                        <div className="space-y-4">
                          <QueueProblems
                            summary={queueSummary}
                            actionId={actionId}
                            onRetry={(id) => void runQueueAction('retry', id)}
                          />
                          <div>
                            <div className="mb-2 label-xs text-faint">Selected attempts</div>
                            <QueueAttempts attempts={queueAttempts} />
                          </div>
                        </div>
                      </div>
                    </div>
                  ) : (
                    <p className="label-xs text-faint">Loading queue…</p>
                  )}
                </Panel>
              </div>
              <Panel title="Stage runs">
                <StageRuns stages={stages} />
              </Panel>
              <Panel title="Recent runs">
                <RecentRuns runs={runs} />
              </Panel>
              <Panel title="Events by type">
                <EventTypeChart data={stats.byEventType} />
              </Panel>
              <Panel title="Articles by source">
                <SourceMixChart data={stats.bySource} />
              </Panel>
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
