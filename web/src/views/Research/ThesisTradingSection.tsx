import { useEffect, useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { fetchCatalysts, fetchKillCriteria, fetchPositions } from '../../lib/api'
import type { Catalyst, KillCriterion, Position } from '../../lib/api'
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

export function ThesisTradingSection({ thesisId }: { thesisId: string }) {
  const [expanded, setExpanded] = useState(true)
  const [positions, setPositions] = useState<Position[]>([])
  const [catalysts, setCatalysts] = useState<Catalyst[]>([])
  const [killCriteria, setKillCriteria] = useState<KillCriterion[]>([])
  const [loaded, setLoaded] = useState(false)

  useEffect(() => {
    const ac = new AbortController()
    Promise.allSettled([
      fetchPositions(ac.signal),
      fetchCatalysts(ac.signal),
      fetchKillCriteria(ac.signal),
    ]).then(([posResult, catResult, kcResult]) => {
      if (ac.signal.aborted) return
      const allPositions = posResult.status === 'fulfilled' ? posResult.value : []
      const allCatalysts = catResult.status === 'fulfilled' ? catResult.value : []
      const allKc = kcResult.status === 'fulfilled' ? kcResult.value : []
      setPositions(allPositions.filter((p: Position) => p.thesisId === thesisId))
      setCatalysts(allCatalysts.filter((c: Catalyst) => c.thesisId === thesisId))
      setKillCriteria(allKc.filter((k: KillCriterion) => k.thesisId === thesisId))
      setLoaded(true)
    })
    return () => ac.abort()
  }, [thesisId])

  const hasContent = positions.length > 0 || catalysts.length > 0 || killCriteria.length > 0

  if (loaded && !hasContent) return null

  const activeCatalysts = catalysts.filter(c => !c.resolved).sort((a, b) =>
    new Date(a.catalystDate).getTime() - new Date(b.catalystDate).getTime()
  )
  const sortedKc = [...killCriteria].sort((a, b) => {
    const order: Record<string, number> = { critical: 0, elevated: 1, watching: 2, dormant: 3 }
    return (order[a.threatLevel] ?? 4) - (order[b.threatLevel] ?? 4)
  })

  return (
    <section className="border-b border-line-soft last:border-b-0 px-3 py-2 sm:px-5 sm:py-3">
      <button
        onClick={() => setExpanded(e => !e)}
        className="flex w-full cursor-pointer items-center gap-2 text-left"
      >
        {expanded ? <ChevronDown className="size-3 text-dim" /> : <ChevronRight className="size-3 text-dim" />}
        <h3 className="text-[10px] tracking-[0.1em] uppercase text-dim">Trading</h3>
        {!expanded && hasContent && (
          <span className="text-[10px] tabular text-faint">
            {positions.length > 0 && `${positions.length} pos`}
            {positions.length > 0 && activeCatalysts.length > 0 && ' · '}
            {activeCatalysts.length > 0 && `${activeCatalysts.length} cat`}
            {(positions.length > 0 || activeCatalysts.length > 0) && killCriteria.length > 0 && ' · '}
            {killCriteria.length > 0 && `${killCriteria.length} kc`}
          </span>
        )}
      </button>

      {expanded && !loaded && (
        <div className="mt-2 text-[11px] text-faint pulse-soft">Loading...</div>
      )}

      {expanded && loaded && hasContent && (
        <div className="mt-2 space-y-3">
          {positions.length > 0 && (
            <div>
              <div className="mb-1 text-[10px] tracking-[0.06em] uppercase text-faint">Positions</div>
              <div className="space-y-1">
                {positions.map(pos => (
                  <div key={pos.id} className="flex items-center gap-2 rounded-sm px-1.5 py-1 hover:bg-surface-2/50">
                    <span className="font-mono text-[12px] font-medium text-fg w-14 shrink-0">{pos.symbol}</span>
                    <span className={cn('size-2 rounded-full shrink-0', metalDot(pos.metal))} />
                    <span className="text-[11px] tabular text-muted flex-1">
                      {pos.deployedPct.toFixed(1)}% / {pos.targetAllocationPct.toFixed(0)}%
                    </span>
                    {pos.entryPrice != null && (
                      <span className="text-[11px] tabular text-dim">${pos.entryPrice.toFixed(2)}</span>
                    )}
                    <span className={cn('text-[10px] font-medium shrink-0', statusColor(pos.status))}>
                      {statusLabel(pos.status)}
                    </span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {activeCatalysts.length > 0 && (
            <div>
              <div className="mb-1 text-[10px] tracking-[0.06em] uppercase text-faint">Catalysts</div>
              <div className="space-y-0.5">
                {activeCatalysts.map(c => (
                  <div key={c.id} className="flex items-center gap-2 rounded-sm px-1.5 py-1 hover:bg-surface-2/50">
                    <span className={cn('text-[11px] tabular font-medium w-12 shrink-0', catalystProximityColor(c.catalystDate))}>
                      {fmtDate(c.catalystDate)}
                    </span>
                    <span className={cn('size-2 rounded-full shrink-0', metalDot(c.metal))} />
                    <span className="text-[12px] text-fg truncate">{c.title}</span>
                  </div>
                ))}
              </div>
            </div>
          )}

          {sortedKc.length > 0 && (
            <div>
              <div className="mb-1 text-[10px] tracking-[0.06em] uppercase text-faint">Kill Criteria</div>
              <div className="space-y-1">
                {sortedKc.map(k => (
                  <div key={k.id} className="flex items-start gap-2 rounded-sm px-1.5 py-1 hover:bg-surface-2/50">
                    <span className={cn('mt-0.5 shrink-0 rounded-sm border px-1.5 py-0.5 text-[9px] font-medium', threatColor(k.threatLevel))}>
                      {threatLabel(k.threatLevel)}
                    </span>
                    <span className="text-[11.5px] text-fg leading-snug">{k.scenario}</span>
                  </div>
                ))}
              </div>
            </div>
          )}
        </div>
      )}
    </section>
  )
}
