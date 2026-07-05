import { useCallback, useEffect, useMemo, useState } from 'react'
import { AlertCircle, RefreshCw } from 'lucide-react'
import {
  fetchForwardIdeas,
  fetchForwardPipelines,
  type ForwardIdeaItemDto,
  type ForwardIdeasDto,
  type ForwardPipelineCatalogDto,
} from '../../lib/api'
import { cn } from '../../lib/cn'
import { fmtCount } from '../../lib/format'
import { Select } from '../../components/Select'

const STORAGE_PREFIX = 'ml-stack-'

function usePersistedState<T>(key: string, fallback: T): [T, (value: T | ((prev: T) => T)) => void] {
  const storageKey = STORAGE_PREFIX + key
  const [value, setValueRaw] = useState<T>(() => {
    try {
      const stored = localStorage.getItem(storageKey)
      if (stored === null) return fallback
      return JSON.parse(stored) as T
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

type ReturnPeriod = '7d' | '30d' | '90d'

const WINDOW_OPTIONS = [7, 14, 30, 60]

function windowToReturnPeriod(windowDays: number): ReturnPeriod {
  if (windowDays <= 7) return '7d'
  if (windowDays <= 30) return '30d'
  return '90d'
}

function returnForPeriod(price: { return7d: number | null; return30d: number | null; return90d: number | null }, period: ReturnPeriod): number | null {
  if (period === '7d') return price.return7d
  if (period === '30d') return price.return30d
  return price.return90d
}

function returnHeatColor(ret: number | null, period: ReturnPeriod): string {
  if (ret == null) return 'bg-surface-2 text-dim'
  const scale = period === '7d' ? 15 : period === '30d' ? 30 : 60
  const intensity = Math.min(1, Math.abs(ret) / scale)
  if (ret > 0) {
    if (intensity > 0.66) return 'bg-positive/25 text-positive'
    if (intensity > 0.33) return 'bg-positive/15 text-positive'
    return 'bg-positive/8 text-positive/80'
  }
  if (ret < 0) {
    if (intensity > 0.66) return 'bg-negative/25 text-negative'
    if (intensity > 0.33) return 'bg-negative/15 text-negative'
    return 'bg-negative/8 text-negative/80'
  }
  return 'bg-surface-2 text-muted'
}

function navigateToChart(symbol: string) {
  window.history.pushState(null, '', `/chart?symbol=${encodeURIComponent(symbol)}`)
  window.dispatchEvent(new PopStateEvent('popstate'))
}

function TickerTile({ symbol, ret, returnPeriod }: { symbol: string; ret: number | null; returnPeriod: ReturnPeriod }) {
  return (
    <button
      type="button"
      onClick={() => navigateToChart(symbol)}
      className={cn(
        'flex min-w-[88px] flex-col items-center rounded-sm border border-line-soft px-3 py-2.5 transition hover:border-accent/50 hover:ring-1 hover:ring-accent/30 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/40',
        returnHeatColor(ret, returnPeriod),
      )}
    >
      <div className="font-mono text-[13px] font-semibold">{symbol}</div>
      <div className="mt-1 text-[15px] font-medium tabular">
        {ret != null ? `${ret >= 0 ? '+' : ''}${ret.toFixed(1)}%` : '--'}
      </div>
    </button>
  )
}

export function StackView() {
  const [windowDays, setWindowDays] = usePersistedState('windowDays', 14)
  const [catalog, setCatalog] = useState<ForwardPipelineCatalogDto | null>(null)
  const [selectedKey, setSelectedKey] = usePersistedState<string | null>('pipelineKey', null)
  const [data, setData] = useState<ForwardIdeasDto | null>(null)
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const ac = new AbortController()
    fetchForwardPipelines(ac.signal)
      .then(res => {
        setCatalog(res)
        setSelectedKey(current => current ?? res.defaultPipelineKey ?? res.items[0]?.key ?? null)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
      })
    return () => ac.abort()
  }, [setSelectedKey])

  useEffect(() => {
    if (!selectedKey) return
    const ac = new AbortController()
    setLoading(true)
    setError(null)
    fetchForwardIdeas({ thesis: selectedKey, windowDays, take: 200, includeCrowded: true }, ac.signal)
      .then(res => { setData(res); setLoading(false) })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setLoading(false)
      })
    return () => ac.abort()
  }, [selectedKey, windowDays])

  const returnPeriod = windowToReturnPeriod(windowDays)

  const itemsBySymbol = useMemo(() => {
    if (!data) return new Map<string, ForwardIdeaItemDto>()
    const map = new Map<string, ForwardIdeaItemDto>()
    for (const item of data.items) map.set(item.symbol, item)
    return map
  }, [data])

  const groupStats = useMemo(() => {
    if (!data) return []
    return data.thesis.groups.map(group => {
      const members = group.symbols.map(symbol => {
        const item = itemsBySymbol.get(symbol)
        const price = item?.current.price ?? { return7d: null, return30d: null, return90d: null }
        return { symbol, ret: returnForPeriod(price, returnPeriod) }
      })
      const withReturn = members.filter(m => m.ret != null)
      const avgReturn = withReturn.length > 0
        ? withReturn.reduce((sum, m) => sum + m.ret!, 0) / withReturn.length
        : null
      return { group, members, avgReturn }
    })
  }, [data, itemsBySymbol, returnPeriod])

  const sorted = useMemo(
    () => [...groupStats].sort((a, b) => (b.avgReturn ?? -999) - (a.avgReturn ?? -999)),
    [groupStats],
  )

  return (
    <div className="h-full overflow-y-auto">
      <div className="sticky top-0 z-10 flex min-h-12 flex-wrap items-center gap-3 border-b border-line-soft bg-canvas/90 px-3 py-2.5 backdrop-blur sm:px-5">
        {catalog && (
          <Select
            value={selectedKey}
            options={catalog.items.map(item => ({ value: item.key, label: item.label }))}
            onChange={setSelectedKey}
            width={220}
            className="shrink-0"
          />
        )}
        <Select
          value={String(windowDays)}
          options={WINDOW_OPTIONS.map(d => ({ value: String(d), label: `${d}d` }))}
          onChange={(v) => { if (v) setWindowDays(Number(v)) }}
          width={70}
          className="shrink-0"
        />
        {data && (
          <div className="hidden items-center gap-3 text-[10.5px] text-dim sm:flex">
            <span className="tabular">{fmtCount(data.universe.candidates)} universe</span>
            <span className="tabular">{data.thesis.groups.reduce((n, g) => n + g.symbols.length, 0)} mapped</span>
          </div>
        )}
        {loading && <RefreshCw className="size-3.5 animate-spin text-dim" />}
      </div>

      <div className="p-3 sm:p-5">
        {error && (
          <div className="mb-3 flex items-center gap-2 border border-negative/35 bg-negative/10 px-3 py-2 text-[13px] text-negative">
            <AlertCircle className="size-4" />
            <span>{error}</span>
          </div>
        )}

        {!data && !error && (
          <div className="text-[12px] text-faint pulse-soft">Loading...</div>
        )}

        {data && (
          <div className="grid gap-4">
            <div>
              <div className="text-[13px] font-medium text-fg">{data.thesis.label}</div>
              <p className="mt-1 text-[11.5px] leading-relaxed text-muted">{data.thesis.description}</p>
            </div>

            {sorted.map(({ group, members, avgReturn }) => (
              <section key={group.key} className="border border-line bg-surface">
                <header className="flex items-center justify-between gap-3 border-b border-line-soft px-3 py-2.5">
                  <div className="min-w-0">
                    <div className="text-[12px] font-medium text-fg">{group.label}</div>
                    <div className="mt-0.5 text-[10.5px] text-dim">{group.setupType}</div>
                  </div>
                  <div className="flex items-center gap-3 text-right">
                    {group.benchmarks && group.benchmarks.length > 0 && (
                      <div className="flex items-center gap-1.5">
                        {group.benchmarks.map(bm => (
                          <button
                            key={bm}
                            type="button"
                            onClick={() => navigateToChart(bm)}
                            className="rounded-sm border border-dashed border-line-soft bg-canvas px-2 py-1 text-[10px] tabular text-dim transition hover:border-accent/50 hover:text-accent"
                          >
                            {bm}
                          </button>
                        ))}
                      </div>
                    )}
                    <div>
                      <div className="label-xs text-dim">Avg {returnPeriod}</div>
                      <div className={cn('mt-0.5 text-[16px] font-medium tabular', avgReturn != null && avgReturn > 0 ? 'text-positive' : avgReturn != null && avgReturn < 0 ? 'text-negative' : 'text-dim')}>
                        {avgReturn != null ? `${avgReturn >= 0 ? '+' : ''}${avgReturn.toFixed(1)}%` : '--'}
                      </div>
                    </div>
                    <div>
                      <div className="label-xs text-dim">Tickers</div>
                      <div className="mt-0.5 text-[16px] font-medium tabular text-muted">{members.length}</div>
                    </div>
                  </div>
                </header>
                <div className="p-3">
                  {group.subcategories && group.subcategories.length > 0 ? (
                    <div className="grid gap-3">
                      {group.subcategories.map(sc => {
                        const scMembers = sc.symbols
                          .map(s => members.find(m => m.symbol === s))
                          .filter((m): m is typeof members[number] => m != null)
                        return (
                          <div key={sc.label}>
                            <div className="mb-1.5 text-[10.5px] font-medium uppercase tracking-[0.06em] text-dim">{sc.label}</div>
                            <div className="flex flex-wrap gap-1.5">
                              {scMembers.map(({ symbol, ret }) => (
                                <TickerTile key={symbol} symbol={symbol} ret={ret} returnPeriod={returnPeriod} />
                              ))}
                            </div>
                          </div>
                        )
                      })}
                    </div>
                  ) : (
                    <div className="flex flex-wrap gap-1.5">
                      {members.map(({ symbol, ret }) => (
                        <TickerTile key={symbol} symbol={symbol} ret={ret} returnPeriod={returnPeriod} />
                      ))}
                    </div>
                  )}
                </div>
              </section>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}
