import * as SliderPrimitive from '@radix-ui/react-slider'
import { cn } from '../lib/cn'

export function Slider({
  value,
  onChange,
  min = 0,
  max = 1,
  step = 0.05,
  className,
}: {
  value: number
  onChange: (v: number) => void
  min?: number
  max?: number
  step?: number
  className?: string
}) {
  return (
    <SliderPrimitive.Root
      value={[value]}
      onValueChange={([v]) => onChange(v)}
      min={min}
      max={max}
      step={step}
      className={cn('relative flex h-4 w-full touch-none items-center select-none', className)}
    >
      <SliderPrimitive.Track className="bg-line-soft relative h-px w-full grow">
        <SliderPrimitive.Range className="bg-accent absolute h-full" />
      </SliderPrimitive.Track>
      <SliderPrimitive.Thumb
        className={cn(
          'block size-2.5 rounded-full bg-accent ring-2 ring-canvas',
          'focus:outline-none focus-visible:ring-accent/40',
          'transition-shadow',
        )}
      />
    </SliderPrimitive.Root>
  )
}
