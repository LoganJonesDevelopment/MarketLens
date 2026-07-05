import { useState } from 'react'
import { ChevronDown, ChevronRight } from 'lucide-react'
import { cn } from '../../lib/cn'

type Slots = Record<string, unknown>

function get(s: Slots, ...keys: string[]): string | null {
  for (const k of keys) {
    const v = s[k]
    if (v == null || v === '') continue
    if (typeof v === 'object') continue
    return String(v)
  }
  return null
}

function KV({ k, v, mono }: { k: string; v: string | null; mono?: boolean }) {
  return (
    <div className="grid grid-cols-[140px_1fr] items-baseline gap-3 py-1">
      <div className="label-xs text-dim">{k}</div>
      <div className={cn('text-[12.5px] text-fg', mono && 'font-mono text-[11.5px]', !v && 'text-faint')}>
        {v ?? '—'}
      </div>
    </div>
  )
}

function EarningsSlots({ s }: { s: Slots }) {
  return (
    <div>
      <KV k="Period" v={get(s, 'period', 'fiscal_period', 'quarter')} />
      <KV k="EPS actual" v={get(s, 'eps_actual', 'eps')} mono />
      <KV k="EPS estimate" v={get(s, 'eps_estimate', 'eps_consensus')} mono />
      <KV k="EPS surprise" v={get(s, 'eps_surprise')} mono />
      <KV k="Revenue actual" v={get(s, 'revenue_actual', 'revenue')} mono />
      <KV k="Revenue estimate" v={get(s, 'revenue_estimate', 'revenue_consensus')} mono />
      <KV k="Guidance" v={get(s, 'guidance', 'guidance_change', 'forward_guidance')} />
    </div>
  )
}

function MnaSlots({ s }: { s: Slots }) {
  return (
    <div>
      <KV k="Buyer" v={get(s, 'buyer', 'acquirer', 'parent')} />
      <KV k="Target" v={get(s, 'target', 'acquired', 'company')} />
      <KV k="Deal type" v={get(s, 'deal_type', 'transaction_type')} />
      <KV k="Deal value" v={get(s, 'deal_value', 'value', 'consideration')} mono />
      <KV k="Structure" v={get(s, 'structure', 'payment_structure')} />
      <KV k="Expected close" v={get(s, 'expected_close', 'close_date')} />
    </div>
  )
}

function AnalystSlots({ s }: { s: Slots }) {
  return (
    <div>
      <KV k="Firm" v={get(s, 'firm', 'analyst_firm', 'broker')} />
      <KV k="Action" v={get(s, 'action', 'change_type')} />
      <KV k="Old rating" v={get(s, 'rating_old', 'previous_rating')} />
      <KV k="New rating" v={get(s, 'rating_new', 'new_rating', 'rating')} />
      <KV k="PT old" v={get(s, 'price_target_old', 'pt_old')} mono />
      <KV k="PT new" v={get(s, 'price_target_new', 'pt_new', 'price_target')} mono />
    </div>
  )
}

function MacroSlots({ s }: { s: Slots }) {
  return (
    <div>
      <KV k="Series" v={get(s, 'series', 'indicator', 'series_id')} mono />
      <KV k="Period" v={get(s, 'period', 'release_period')} />
      <KV k="Value" v={get(s, 'value', 'actual')} mono />
      <KV k="Prior" v={get(s, 'prior', 'previous_value')} mono />
      <KV k="Consensus" v={get(s, 'consensus', 'expected')} mono />
      <KV k="Surprise" v={get(s, 'surprise')} mono />
    </div>
  )
}

function ProductSlots({ s }: { s: Slots }) {
  return (
    <div>
      <KV k="Product" v={get(s, 'product', 'product_name', 'name')} />
      <KV k="Category" v={get(s, 'category', 'product_category')} />
      <KV k="Launch date" v={get(s, 'launch_date', 'release_date', 'date')} />
      <KV k="Geography" v={get(s, 'geography', 'region', 'market')} />
    </div>
  )
}

function GenericSlots({ s }: { s: Slots }) {
  const keys = Object.keys(s).filter(k => {
    const v = s[k]
    return v != null && v !== '' && typeof v !== 'object'
  })
  if (keys.length === 0) {
    return <p className="label-xs text-faint">No structured slots extracted</p>
  }
  return (
    <div>
      {keys.map(k => (
        <KV key={k} k={k.replaceAll('_', ' ')} v={String(s[k])} />
      ))}
    </div>
  )
}

export function SlotRenderer({
  eventType, slots,
}: { eventType: string; slots: Slots | null }) {
  const [showRaw, setShowRaw] = useState(false)

  if (!slots) {
    return <p className="label-xs text-faint">Slots unavailable</p>
  }

  let typed
  switch (eventType) {
    case 'earnings':                typed = <EarningsSlots s={slots} />; break
    case 'acquisition_disposition': typed = <MnaSlots s={slots} />; break
    case 'analyst_action':          typed = <AnalystSlots s={slots} />; break
    case 'macro_release':           typed = <MacroSlots s={slots} />; break
    case 'product_launch':          typed = <ProductSlots s={slots} />; break
    default:                        typed = <GenericSlots s={slots} />; break
  }

  return (
    <div>
      {typed}
      <button
        onClick={() => setShowRaw(v => !v)}
        className="mt-3 inline-flex items-center gap-1 text-[10.5px] tracking-[0.085em] uppercase text-dim hover:text-fg transition"
      >
        {showRaw ? <ChevronDown className="size-3" /> : <ChevronRight className="size-3" />}
        Raw JSON
      </button>
      {showRaw && (
        <pre className="mt-2 max-h-72 overflow-auto rounded-sm border border-line bg-canvas p-2 font-mono text-[10.5px] leading-relaxed text-muted">
{JSON.stringify(slots, null, 2)}
        </pre>
      )}
    </div>
  )
}
