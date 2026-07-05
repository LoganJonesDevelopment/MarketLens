import * as Popover from '@radix-ui/react-popover'
import { ChevronDown, Check } from 'lucide-react'
import { useState } from 'react'
import { cn } from '../lib/cn'

export type SelectOption = { value: string | null; label: string }

export function Select({
  value,
  options,
  onChange,
  placeholder,
  className,
  width = 180,
}: {
  value: string | null
  options: SelectOption[]
  onChange: (v: string | null) => void
  placeholder?: string
  className?: string
  width?: number
}) {
  const [open, setOpen] = useState(false)
  const current = options.find(o => o.value === value)
  const label = current?.label ?? placeholder ?? '—'

  return (
    <Popover.Root open={open} onOpenChange={setOpen}>
      <Popover.Trigger
        className={cn(
          'inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[12px] text-fg',
          'hover:border-line hover:bg-surface-2 transition',
          'focus-visible:outline-none focus-visible:border-accent/60',
          className,
        )}
        style={{ minWidth: width }}
      >
        <span className="flex-1 truncate text-left">{label}</span>
        <ChevronDown className="size-3 text-dim" />
      </Popover.Trigger>
      <Popover.Portal>
        <Popover.Content
          align="end"
          sideOffset={4}
          className={cn(
            'z-50 max-h-72 overflow-y-auto rounded-sm border border-line bg-surface-2 p-1 shadow-2xl',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=open]:fade-in-0 data-[state=closed]:fade-out-0',
          )}
          style={{ minWidth: width }}
        >
          {options.map(o => {
            const selected = o.value === value
            return (
              <button
                key={o.value ?? '__null'}
                onClick={() => { onChange(o.value); setOpen(false) }}
                className={cn(
                  'flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-[12px]',
                  'hover:bg-surface text-muted hover:text-fg transition',
                  selected && 'text-fg',
                )}
              >
                <Check className={cn('size-3 shrink-0', selected ? 'opacity-100 text-accent' : 'opacity-0')} />
                <span className="truncate">{o.label}</span>
              </button>
            )
          })}
        </Popover.Content>
      </Popover.Portal>
    </Popover.Root>
  )
}
