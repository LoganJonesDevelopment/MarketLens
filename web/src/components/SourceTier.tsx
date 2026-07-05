import { cn } from '../lib/cn'

const TIER_LABEL: Record<string, string> = {
  primary:     'primary',
  wire:        'wire',
  trade_press: 'trade press',
  aggregator:  'aggregator',
  opinion:     'opinion',
}

const TIER_CLR: Record<string, string> = {
  primary:     'text-trust',
  wire:        'text-wire',
  trade_press: 'text-muted',
  aggregator:  'text-aggregator',
  opinion:     'text-opinion',
}

const TIER_BG: Record<string, string> = {
  primary:     'bg-trust',
  wire:        'bg-wire',
  trade_press: 'bg-muted',
  aggregator:  'bg-aggregator',
  opinion:     'bg-opinion',
}

export function SourceTierLabel({ tier }: { tier: string }) {
  return (
    <span className={cn('text-[10px] tracking-[0.085em] uppercase font-medium', TIER_CLR[tier] ?? 'text-dim')}>
      {TIER_LABEL[tier] ?? tier}
    </span>
  )
}

export function SourceTierDot({ tier, size = 6 }: { tier: string; size?: number }) {
  return (
    <div
      className={cn('rounded-full shrink-0', TIER_BG[tier] ?? 'bg-line')}
      style={{ width: size, height: size }}
      title={TIER_LABEL[tier] ?? tier}
    />
  )
}
