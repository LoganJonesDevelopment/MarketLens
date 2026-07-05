import * as TabsPrimitive from '@radix-ui/react-tabs'
import { cn } from '../lib/cn'
import type { ComponentProps } from 'react'

export const TabsRoot    = TabsPrimitive.Root
export const TabsContent = TabsPrimitive.Content

export function TabsList({ className, ...rest }: ComponentProps<typeof TabsPrimitive.List>) {
  return <TabsPrimitive.List className={cn('flex items-center gap-1', className)} {...rest} />
}

export function TabsTrigger({ className, ...rest }: ComponentProps<typeof TabsPrimitive.Trigger>) {
  return (
    <TabsPrimitive.Trigger
      className={cn(
        'relative inline-flex h-10 shrink-0 items-center gap-2 rounded-md border border-transparent px-3.5 text-[13px] font-medium transition',
        'text-dim hover:border-line-soft hover:bg-surface/70 hover:text-fg focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/45',
        'data-[state=active]:border-line data-[state=active]:bg-surface-2 data-[state=active]:text-fg',
        "after:content-[''] after:absolute after:inset-x-3 after:bottom-1 after:h-0.5 after:rounded-full after:bg-transparent",
        'data-[state=active]:after:bg-accent',
        className,
      )}
      {...rest}
    />
  )
}
