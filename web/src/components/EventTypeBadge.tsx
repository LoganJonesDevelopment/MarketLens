import { eventLabel } from '../lib/format'

export function EventTypeBadge({ type }: { type: string }) {
  return (
    <span className="inline-flex h-5 shrink-0 items-center rounded-sm border border-line bg-surface-2/60 px-1.5 text-[10px] tracking-[0.06em] uppercase text-muted">
      {eventLabel(type)}
    </span>
  )
}
