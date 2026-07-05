import { Tooltip } from './Tooltip'

export type ImportanceComponents = {
  sourceWeight: number
  noveltyWeight: number
  eventClassPrior: number
  magnitudeSignal: number
}

const KEYS: Array<{ k: keyof ImportanceComponents; label: string }> = [
  { k: 'sourceWeight',     label: 'Source' },
  { k: 'noveltyWeight',    label: 'Novelty' },
  { k: 'eventClassPrior',  label: 'Event class' },
  { k: 'magnitudeSignal',  label: 'Magnitude' },
]

export function ImportanceBar({ components }: { components: ImportanceComponents }) {
  return (
    <Tooltip
      content={
        <div className="space-y-0.5 tabular">
          {KEYS.map(({ k, label }) => (
            <div key={k} className="flex justify-between gap-3">
              <span className="text-dim">{label}</span>
              <span>{components[k].toFixed(2)}</span>
            </div>
          ))}
        </div>
      }
    >
      <div className="flex h-4 items-end gap-[2px]" aria-hidden>
        {KEYS.map(({ k }) => {
          const v = Math.max(0, Math.min(1, components[k]))
          return (
            <div key={k} className="relative h-full w-[3px] overflow-hidden rounded-[1px] bg-line">
              <div
                className="absolute inset-x-0 bottom-0 bg-fg/65"
                style={{ height: `${(v * 100).toFixed(1)}%` }}
              />
            </div>
          )
        })}
      </div>
    </Tooltip>
  )
}

export function importanceColor(v: number): string {
  if (v >= 0.7)  return 'var(--color-accent)'
  if (v >= 0.5)  return 'oklch(0.85 0.10 85)'
  if (v >= 0.3)  return 'var(--color-fg)'
  return 'var(--color-faint)'
}
