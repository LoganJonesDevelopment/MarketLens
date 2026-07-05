import { useEffect, useState } from 'react'
import { AlertCircle, ArrowDown, ArrowUp, RefreshCw } from 'lucide-react'
import { fetchSourcesHealth, type SourceHealthRow, type SourcesHealthDto } from '../../lib/api'
import { fmtAge, sourceLabel } from '../../lib/format'
import { SourceTierDot } from '../../components/SourceTier'
import { cn } from '../../lib/cn'

type SortKey = 'status' | 'name' | 'tier' | 'weight' | 'count1h' | 'count24h' | 'count7d' | 'lastIngestedAt' | 'lastPolledAt'

const STATUS_ORDER: Record<string, number> = { silent: 0, degraded: 1, stale: 2, healthy: 3 }

function statusOrder(s: string): number {
  return STATUS_ORDER[s] ?? 3
}

function StatusDot({ status }: { status: string }) {
  const cls =
    status === 'healthy' ? 'bg-positive' :
    status === 'degraded' ? 'bg-negative' :
    status === 'stale'   ? 'bg-accent' :
                           'bg-negative'
  const title =
    status === 'healthy' ? 'Healthy: recent article or successful poll' :
    status === 'degraded' ? 'Degraded: latest poll failed' :
    status === 'stale'   ? 'Stale: last article or successful poll 1-7d ago' :
                           'Silent: no article in 7d or never produced data'
  return <span className={cn('inline-block size-2 rounded-full shrink-0', cls)} title={title} />
}

function SortHeader({
  label, col, sort, dir, onSort,
}: {
  label: string
  col: SortKey
  sort: SortKey
  dir: 'asc' | 'desc'
  onSort: (col: SortKey) => void
}) {
  const active = sort === col
  return (
    <th
      className="cursor-pointer select-none whitespace-nowrap px-2 py-2 text-left label-xs text-dim hover:text-fg transition"
      onClick={() => onSort(col)}
    >
      <span className="inline-flex items-center gap-1">
        {label}
        {active
          ? dir === 'asc'
            ? <ArrowUp className="size-3 text-accent" />
            : <ArrowDown className="size-3 text-accent" />
          : <ArrowDown className="size-3 opacity-20" />}
      </span>
    </th>
  )
}

function sortRows(rows: SourceHealthRow[], sort: SortKey, dir: 'asc' | 'desc'): SourceHealthRow[] {
  const mul = dir === 'asc' ? 1 : -1
  return [...rows].sort((a, b) => {
    switch (sort) {
      case 'status':
        return mul * (statusOrder(a.status) - statusOrder(b.status))
      case 'name':
        return mul * a.name.localeCompare(b.name)
      case 'tier':
        return mul * a.tier.localeCompare(b.tier)
      case 'weight':
        return mul * (a.weight - b.weight)
      case 'count1h':
        return mul * (a.count1h - b.count1h)
      case 'count24h':
        return mul * (a.count24h - b.count24h)
      case 'count7d':
        return mul * (a.count7d - b.count7d)
      case 'lastIngestedAt': {
        const at = (r: SourceHealthRow) => r.lastIngestedAt ? new Date(r.lastIngestedAt).getTime() : 0
        return mul * (at(a) - at(b))
      }
      case 'lastPolledAt': {
        const at = (r: SourceHealthRow) => r.lastPolledAt ? new Date(r.lastPolledAt).getTime() : 0
        return mul * (at(a) - at(b))
      }
    }
  })
}

export function SourcesView() {
  const [data, setData]     = useState<SourcesHealthDto | null>(null)
  const [error, setError]   = useState<string | null>(null)
  const [loading, setLoading] = useState(false)
  const [stamp, setStamp]   = useState(0)
  const [sort, setSort]     = useState<SortKey>('status')
  const [dir, setDir]       = useState<'asc' | 'desc'>('asc')

  useEffect(() => {
    const ac = new AbortController()
    setLoading(true)
    setError(null)
    fetchSourcesHealth(ac.signal)
      .then(res => { setData(res); setLoading(false) })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setLoading(false)
      })
    return () => ac.abort()
  }, [stamp])

  const handleSort = (col: SortKey) => {
    if (col === sort) {
      setDir(d => d === 'asc' ? 'desc' : 'asc')
    } else {
      setSort(col)
      setDir(col === 'status' ? 'asc' : 'desc')
    }
  }

  const now = new Date()
  const rows = data ? sortRows(data.sources, sort, dir) : []

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden">
      <div className="sticky top-0 z-10 flex items-center gap-3 border-b border-line-soft bg-canvas/90 px-3 py-2.5 backdrop-blur sm:px-5">
        <h2 className="label-xs text-dim">Sources</h2>
        {data && (
          <div className="flex items-center gap-3">
            <span className="label-xs text-positive">{data.summary.healthy} healthy</span>
            {(data.summary.degraded ?? 0) > 0 && (
              <span className="label-xs text-negative">{data.summary.degraded} degraded</span>
            )}
            {data.summary.stale > 0 && (
              <span className="label-xs text-accent">{data.summary.stale} stale</span>
            )}
            {data.summary.silent > 0 && (
              <span className="label-xs text-negative">{data.summary.silent} silent</span>
            )}
            <span className="label-xs text-dim">{data.summary.total24h} articles/24h</span>
          </div>
        )}
        <button
          onClick={() => setStamp(s => s + 1)}
          className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted hover:text-fg hover:bg-surface-2 transition"
        >
          <RefreshCw className={cn('size-3', loading && 'animate-spin')} />
          <span className="label-xs hidden sm:inline">Refresh</span>
        </button>
      </div>

      <div className="p-3 sm:p-5">
        {error && (
          <div className="flex items-center gap-2 text-[13px] text-negative">
            <AlertCircle className="size-4" />
            <span>API error: {error}</span>
          </div>
        )}

        {!error && !data && (
          <div className="text-[12px] text-faint pulse-soft">Loading…</div>
        )}

        {data && (
          <div className="rounded-sm border border-line bg-surface overflow-x-auto">
            <table className="w-full text-[12px]">
              <thead>
                <tr className="border-b border-line-soft">
                  <SortHeader label="Status" col="status"      sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="Source" col="name"        sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="Tier"   col="tier"        sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="Wt"     col="weight"      sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="1h"     col="count1h"     sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="24h"    col="count24h"    sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="7d"     col="count7d"     sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="Last seen" col="lastIngestedAt" sort={sort} dir={dir} onSort={handleSort} />
                  <SortHeader label="Last poll" col="lastPolledAt" sort={sort} dir={dir} onSort={handleSort} />
                </tr>
              </thead>
              <tbody>
                {rows.map(row => (
                  <tr key={row.name} className="border-b border-line-soft last:border-0 hover:bg-surface-2 transition-colors">
                    <td className="px-2 py-2">
                      <StatusDot status={row.status} />
                    </td>
                    <td className="px-2 py-2 font-medium text-fg">
                      {sourceLabel(row.name)}
                    </td>
                    <td className="px-2 py-2">
                      <div className="flex items-center gap-1.5">
                        <SourceTierDot tier={row.tier} size={5} />
                        <span className="text-muted text-[10.5px] tracking-wide">{row.tier}</span>
                      </div>
                    </td>
                    <td className="px-2 py-2 tabular text-muted">{row.weight.toFixed(2)}</td>
                    <td className={cn('px-2 py-2 tabular text-right', row.count1h > 0 ? 'text-fg' : 'text-faint')}>
                      {row.count1h > 0 ? row.count1h : '—'}
                    </td>
                    <td className={cn('px-2 py-2 tabular text-right', row.count24h > 0 ? 'text-fg' : 'text-faint')}>
                      {row.count24h > 0 ? row.count24h : '—'}
                    </td>
                    <td className={cn('px-2 py-2 tabular text-right', row.count7d > 0 ? 'text-fg' : 'text-faint')}>
                      {row.count7d > 0 ? row.count7d : '—'}
                    </td>
                    <td className="px-2 py-2 tabular text-muted">
                      {row.lastIngestedAt ? fmtAge(row.lastIngestedAt, now) : <span className="text-faint">never</span>}
                    </td>
                    <td className="px-2 py-2 tabular text-muted" title={row.lastError ?? undefined}>
                      {row.lastPolledAt ? fmtAge(row.lastPolledAt, now) : <span className="text-faint">never</span>}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  )
}
