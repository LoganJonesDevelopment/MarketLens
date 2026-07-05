export function metalDot(metal: string) {
  const colors: Record<string, string> = {
    gold: 'bg-yellow-500',
    silver: 'bg-gray-400',
    copper: 'bg-orange-600',
    platinum: 'bg-blue-300',
    palladium: 'bg-indigo-400',
    uranium: 'bg-green-500',
    lithium: 'bg-cyan-400',
  }
  return colors[metal?.toLowerCase()] ?? 'bg-muted'
}

export function threatColor(level: string) {
  if (level === 'critical') return 'bg-negative/20 text-negative border-negative/40'
  if (level === 'elevated') return 'bg-accent/15 text-accent border-accent/40'
  if (level === 'watching') return 'bg-yellow-500/15 text-yellow-500 border-yellow-500/40'
  return 'bg-surface-2 text-dim border-line-soft'
}

export function threatLabel(level: string) {
  if (level === 'critical') return 'Critical'
  if (level === 'elevated') return 'Elevated'
  if (level === 'watching') return 'Watching'
  return 'Dormant'
}

export function statusColor(status: string) {
  if (status === 'full') return 'text-positive'
  if (status === 'partial') return 'text-accent'
  return 'text-dim'
}

export function statusLabel(status: string) {
  if (status === 'full') return 'Full'
  if (status === 'partial') return 'Partial'
  return 'Planned'
}

export function catalystProximityColor(dateStr: string) {
  const now = Date.now()
  const date = new Date(dateStr).getTime()
  const daysUntil = (date - now) / (1000 * 60 * 60 * 24)
  if (daysUntil < 0) return 'text-dim'
  if (daysUntil <= 14) return 'text-negative'
  if (daysUntil <= 30) return 'text-accent'
  return 'text-muted'
}

export function fmtDate(iso: string) {
  const d = new Date(iso)
  return d.toLocaleDateString('en-US', { month: 'short', day: 'numeric' })
}
