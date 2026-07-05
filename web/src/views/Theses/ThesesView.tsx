import { useEffect, useState } from 'react'
import { AlertCircle, CandlestickChart, RefreshCw } from 'lucide-react'
import { fetchCatalysts, fetchKillCriteria, fetchPositions, fetchPortfolio } from '../../lib/api'
import type { Catalyst, KillCriterion, Portfolio, Position } from '../../lib/api'
import { cn } from '../../lib/cn'
import {
  catalystProximityColor,
  fmtDate,
  metalDot,
  statusColor,
  statusLabel,
  threatColor,
  threatLabel,
} from '../../lib/trading'

function AllocationBar({ metal, target, deployed }: { metal: string; target: number; deployed: number }) {
  const pct = Math.min(deployed / Math.max(target, 0.01) * 100, 100)
  return (
    <div className="flex items-center gap-2">
      <div className="flex items-center gap-1.5 w-20">
        <span className={cn('size-2 rounded-full', metalDot(metal))} />
        <span className="text-[11px] text-muted capitalize">{metal}</span>
      </div>
      <div className="relative h-3 flex-1 rounded-sm bg-surface-2 border border-line-soft overflow-hidden">
        <div
          className="absolute inset-y-0 left-0 rounded-sm bg-accent/60"
          style={{ width: `${pct}%` }}
        />
        <div
          className="absolute inset-y-0 left-0 border-r-2 border-dashed border-fg/30"
          style={{ left: `${Math.min(target, 100)}%` }}
        />
      </div>
      <span className="w-16 text-right text-[10.5px] tabular text-dim">
        {deployed.toFixed(1)}% / {target.toFixed(0)}%
      </span>
    </div>
  )
}

function PortfolioSummary({ portfolio }: { portfolio: Portfolio }) {
  const deployedPct = portfolio.totalDeployedPct
  const reservedPct = portfolio.reservedPct
  return (
    <section className="border border-line bg-surface">
      <header className="border-b border-line-soft px-3 py-2">
        <div className="text-[11px] font-medium uppercase tracking-[0.06em] text-dim">Portfolio</div>
      </header>
      <div className="p-3 grid gap-3">
        <div className="flex items-center gap-4">
          <div>
            <div className="text-[10.5px] text-dim">Target</div>
            <div className="text-[15px] font-medium tabular text-fg">{portfolio.totalTargetPct.toFixed(0)}%</div>
          </div>
          <div>
            <div className="text-[10.5px] text-dim">Deployed</div>
            <div className="text-[15px] font-medium tabular text-positive">{deployedPct.toFixed(1)}%</div>
          </div>
          <div>
            <div className="text-[10.5px] text-dim">Reserved</div>
            <div className="text-[15px] font-medium tabular text-muted">{reservedPct.toFixed(1)}%</div>
          </div>
        </div>
        <div className="relative h-4 rounded-sm overflow-hidden bg-surface-2 border border-line-soft">
          <div
            className="absolute inset-y-0 left-0 bg-positive/40"
            style={{ width: `${deployedPct}%` }}
          />
          <div
            className="absolute inset-y-0 bg-surface-2"
            style={{ left: `${deployedPct}%`, width: `${reservedPct}%` }}
          />
        </div>
        {portfolio.metals.length > 0 && (
          <div className="grid gap-1.5 mt-1">
            {portfolio.metals.map(a => (
              <AllocationBar key={a.metal} metal={a.metal} target={a.totalTargetPct} deployed={a.totalDeployedPct} />
            ))}
          </div>
        )}
      </div>
    </section>
  )
}

function PositionsTable({ positions }: { positions: Position[] }) {
  const grouped = positions.reduce<Record<string, Position[]>>((acc, p) => {
    const key = p.metal || 'other'
    ;(acc[key] ??= []).push(p)
    return acc
  }, {})

  return (
    <section className="border border-line bg-surface">
      <header className="border-b border-line-soft px-3 py-2">
        <div className="text-[11px] font-medium uppercase tracking-[0.06em] text-dim">Positions</div>
      </header>
      <div className="overflow-x-auto">
        <table className="w-full text-left">
          <thead>
            <tr className="border-b border-line-soft bg-canvas/50">
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim">Symbol</th>
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim">Metal</th>
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim text-right">Target %</th>
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim text-right">Deployed %</th>
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim text-right">Entry</th>
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim">Scale-in</th>
              <th className="px-3 py-1.5 text-[10.5px] font-medium text-dim text-center">Status</th>
            </tr>
          </thead>
          <tbody>
            {Object.values(grouped).map(items => (
              items.map((pos, idx) => (
                <tr key={pos.symbol} className={cn('border-b border-line-soft/50 hover:bg-surface-2/50', idx === 0 && 'border-t border-line-soft')}>
                  <td className="px-3 py-1.5">
                    <span className="flex items-center gap-1.5">
                      <a
                        href={`/research/${pos.thesisId}`}
                        onClick={e => { e.preventDefault(); window.history.pushState(null, '', `/research/${pos.thesisId}`); window.dispatchEvent(new PopStateEvent('popstate')) }}
                        className="font-mono text-[13px] font-medium text-fg hover:text-accent transition"
                        title="Open thesis"
                      >
                        {pos.symbol}
                      </a>
                      <a
                        href={`/chart?symbol=${pos.symbol}`}
                        onClick={e => { e.preventDefault(); window.history.pushState(null, '', `/chart?symbol=${pos.symbol}`); window.dispatchEvent(new PopStateEvent('popstate')) }}
                        className="text-dim hover:text-accent transition"
                        title="1W chart"
                      >
                        <CandlestickChart className="size-3" />
                      </a>
                    </span>
                  </td>
                  <td className="px-3 py-1.5">
                    <span className="flex items-center gap-1.5">
                      <span className={cn('size-2 rounded-full', metalDot(pos.metal))} />
                      <span className="text-[11px] text-muted capitalize">{pos.metal}</span>
                    </span>
                  </td>
                  <td className="px-3 py-1.5 text-right text-[13px] tabular text-muted">{pos.targetAllocationPct.toFixed(1)}%</td>
                  <td className="px-3 py-1.5 text-right text-[13px] tabular text-fg">{pos.deployedPct.toFixed(1)}%</td>
                  <td className="px-3 py-1.5 text-right text-[13px] tabular text-muted">
                    {pos.entryPrice != null ? `$${pos.entryPrice.toFixed(2)}` : '--'}
                  </td>
                  <td className="px-3 py-1.5 text-[11px] text-dim max-w-[160px] truncate">
                    {pos.scaleInTriggerPrice != null ? `$${pos.scaleInTriggerPrice.toFixed(2)}` : '--'}
                  </td>
                  <td className="px-3 py-1.5 text-center">
                    <span className={cn('text-[11px] font-medium', statusColor(pos.status))}>
                      {statusLabel(pos.status)}
                    </span>
                  </td>
                </tr>
              ))
            ))}
          </tbody>
        </table>
        {positions.length === 0 && (
          <div className="px-3 py-4 text-center text-[12px] text-dim">No positions configured</div>
        )}
      </div>
    </section>
  )
}

function CatalystsList({ catalysts }: { catalysts: Catalyst[] }) {
  const active = catalysts.filter(c => !c.resolved).sort((a, b) => new Date(a.catalystDate).getTime() - new Date(b.catalystDate).getTime())
  const resolved = catalysts.filter(c => c.resolved)

  return (
    <section className="border border-line bg-surface min-h-0 flex flex-col">
      <header className="border-b border-line-soft px-3 py-2">
        <div className="text-[11px] font-medium uppercase tracking-[0.06em] text-dim">Upcoming Catalysts</div>
      </header>
      <div className="flex-1 overflow-y-auto p-2 grid content-start gap-1">
        {active.map(c => (
          <div key={c.id} className="flex items-center gap-2 px-2 py-1.5 rounded-sm hover:bg-surface-2/50">
            <span className={cn('text-[11px] tabular font-medium w-12 shrink-0', catalystProximityColor(c.catalystDate))}>
              {fmtDate(c.catalystDate)}
            </span>
            <span className={cn('size-2 rounded-full shrink-0', metalDot(c.metal))} />
            <span className="text-[12px] text-fg truncate">{c.title}</span>
          </div>
        ))}
        {active.length === 0 && (
          <div className="px-2 py-3 text-center text-[11px] text-dim">No upcoming catalysts</div>
        )}
        {resolved.length > 0 && (
          <>
            <div className="mt-2 border-t border-line-soft pt-2 px-2">
              <span className="text-[10px] uppercase tracking-wider text-dim">Resolved</span>
            </div>
            {resolved.map(c => (
              <div key={c.id} className="flex items-center gap-2 px-2 py-1 rounded-sm opacity-50">
                <span className="text-[11px] tabular w-12 shrink-0 text-dim line-through">{fmtDate(c.catalystDate)}</span>
                <span className={cn('size-2 rounded-full shrink-0', metalDot(c.metal))} />
                <span className="text-[12px] text-dim line-through truncate">{c.title}</span>
              </div>
            ))}
          </>
        )}
      </div>
    </section>
  )
}

function KillCriteriaList({ criteria }: { criteria: KillCriterion[] }) {
  const order: Record<string, number> = { critical: 0, elevated: 1, watching: 2, dormant: 3 }
  const sorted = [...criteria].sort((a, b) => (order[a.threatLevel] ?? 4) - (order[b.threatLevel] ?? 4))

  return (
    <section className="border border-line bg-surface min-h-0 flex flex-col">
      <header className="border-b border-line-soft px-3 py-2">
        <div className="text-[11px] font-medium uppercase tracking-[0.06em] text-dim">Kill Criteria</div>
      </header>
      <div className="flex-1 overflow-y-auto p-2 grid content-start gap-1.5">
        {sorted.map(k => (
          <div key={k.id} className="flex items-start gap-2 px-2 py-1.5 rounded-sm hover:bg-surface-2/50">
            <span className={cn('mt-0.5 shrink-0 rounded-sm border px-1.5 py-0.5 text-[10px] font-medium', threatColor(k.threatLevel))}>
              {threatLabel(k.threatLevel)}
            </span>
            <span className="text-[12px] text-fg leading-snug">{k.scenario}</span>
          </div>
        ))}
        {criteria.length === 0 && (
          <div className="px-2 py-3 text-center text-[11px] text-dim">No kill criteria defined</div>
        )}
      </div>
    </section>
  )
}

export function ThesesView() {
  const [positions, setPositions] = useState<Position[]>([])
  const [portfolio, setPortfolio] = useState<Portfolio | null>(null)
  const [catalysts, setCatalysts] = useState<Catalyst[]>([])
  const [killCriteria, setKillCriteria] = useState<KillCriterion[]>([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const ac = new AbortController()
    setLoading(true)
    setError(null)

    Promise.allSettled([
      fetchPositions(ac.signal),
      fetchPortfolio(ac.signal),
      fetchCatalysts(ac.signal),
      fetchKillCriteria(ac.signal),
    ]).then(([posRes, portRes, catRes, killRes]) => {
      if (ac.signal.aborted) return
      if (posRes.status === 'fulfilled') setPositions(posRes.value ?? [])
      if (portRes.status === 'fulfilled') setPortfolio(portRes.value ?? null)
      if (catRes.status === 'fulfilled') setCatalysts(catRes.value ?? [])
      if (killRes.status === 'fulfilled') setKillCriteria(killRes.value ?? [])

      const allFailed = [posRes, portRes, catRes, killRes].every(r => r.status === 'rejected')
      if (allFailed) {
        const firstErr = (posRes as PromiseRejectedResult).reason
        setError(firstErr instanceof Error ? firstErr.message : 'Failed to load data')
      }
      setLoading(false)
    })

    return () => ac.abort()
  }, [])

  return (
    <div className="h-full overflow-y-auto">
      <div className="sticky top-0 z-10 flex min-h-12 items-center gap-3 border-b border-line-soft bg-canvas/90 px-3 py-2.5 backdrop-blur sm:px-5">
        <div className="text-[13px] font-medium text-fg">Thesis Trading</div>
        {loading && <RefreshCw className="size-3.5 animate-spin text-dim" />}
      </div>

      <div className="p-3 sm:p-5">
        {error && (
          <div className="mb-3 flex items-center gap-2 border border-negative/35 bg-negative/10 px-3 py-2 text-[13px] text-negative">
            <AlertCircle className="size-4" />
            <span>{error}</span>
          </div>
        )}

        {loading && !error && (
          <div className="text-[12px] text-faint pulse-soft">Loading...</div>
        )}

        {!loading && (
          <div className="grid gap-4">
            {portfolio && <PortfolioSummary portfolio={portfolio} />}
            <PositionsTable positions={positions} />
            <div className="grid gap-4 lg:grid-cols-2">
              <CatalystsList catalysts={catalysts} />
              <KillCriteriaList criteria={killCriteria} />
            </div>
          </div>
        )}
      </div>
    </div>
  )
}
