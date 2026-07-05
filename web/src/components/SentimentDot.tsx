import { cn } from '../lib/cn'
import { fmtSigned } from '../lib/format'

export function SentimentDot({ value, size = 6 }: { value: number; size?: number }) {
  const cls =
    value >  0.15 ? 'bg-positive' :
    value < -0.15 ? 'bg-negative' :
                    'bg-faint'
  return (
    <div
      className={cn('rounded-full shrink-0', cls)}
      style={{ width: size, height: size }}
      title={`sentiment ${fmtSigned(value)}`}
    />
  )
}
