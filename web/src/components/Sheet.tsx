import * as Dialog from '@radix-ui/react-dialog'
import type { CSSProperties, ReactNode } from 'react'
import { cn } from '../lib/cn'

export function Sheet({
  open,
  onOpenChange,
  children,
  width = 480,
}: {
  open: boolean
  onOpenChange: (v: boolean) => void
  children: ReactNode
  width?: number
}) {
  return (
    <Dialog.Root open={open} onOpenChange={onOpenChange}>
      <Dialog.Portal>
        <Dialog.Overlay
          className={cn(
            'fixed inset-0 z-40 bg-black/40 backdrop-blur-[1px]',
            'data-[state=open]:animate-in data-[state=closed]:animate-out',
            'data-[state=open]:fade-in-0 data-[state=closed]:fade-out-0',
            'transition-opacity duration-150',
          )}
        />
        <Dialog.Content
          style={{ '--sheet-width': `${width}px` } as CSSProperties}
          className={cn(
            'fixed inset-y-0 right-0 z-40 w-full overflow-y-auto sm:w-[var(--sheet-width)] sm:max-w-full',
            'bg-surface border-l border-line shadow-2xl',
            'focus:outline-none',
          )}
        >
          <Dialog.Title className="sr-only">Detail</Dialog.Title>
          <Dialog.Description className="sr-only">Event detail panel</Dialog.Description>
          {children}
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  )
}

export const SheetClose = Dialog.Close
