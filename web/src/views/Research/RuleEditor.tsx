import { useEffect, useState } from 'react'
import { Check, Trash2, X } from 'lucide-react'
import { Sheet, SheetClose } from '../../components/Sheet'
import { Slider } from '../../components/Slider'
import {
  createThesisRule,
  deleteThesisRule,
  updateThesisRule,
  type ThesisRuleDto,
  type UpsertRuleCommand,
} from '../../lib/api'
import { cn } from '../../lib/cn'

type DraftRule = {
  name: string
  isEnabled: boolean
  assetKeywords: string
  conceptKeywords: string
  excludeTerms: string
  eventTypes: string
  sourceNames: string
  sourceTiers: string
  similarity: number
}

const EMPTY_DRAFT: DraftRule = {
  name: '',
  isEnabled: true,
  assetKeywords: '',
  conceptKeywords: '',
  excludeTerms: '',
  eventTypes: '',
  sourceNames: '',
  sourceTiers: '',
  similarity: 0.74,
}

function fromRule(rule: ThesisRuleDto): DraftRule {
  return {
    name: rule.name,
    isEnabled: rule.isEnabled,
    assetKeywords: rule.assetKeywords.join(', '),
    conceptKeywords: rule.conceptKeywords.join(', '),
    excludeTerms: rule.excludeTerms.join(', '),
    eventTypes: rule.eventTypes.join(', '),
    sourceNames: rule.sourceNames.join(', '),
    sourceTiers: rule.sourceTiers.join(', '),
    similarity: rule.minArticleSimilarity ?? 0.74,
  }
}

function toCommand(draft: DraftRule): UpsertRuleCommand {
  const split = (s: string) =>
    s.split(/[,\n]/).map(v => v.trim()).filter(Boolean)
  return {
    name: draft.name.trim() || 'Research rule',
    isEnabled: draft.isEnabled,
    assetKeywords: split(draft.assetKeywords),
    conceptKeywords: split(draft.conceptKeywords),
    excludeTerms: split(draft.excludeTerms),
    eventTypes: split(draft.eventTypes),
    sourceNames: split(draft.sourceNames),
    sourceTiers: split(draft.sourceTiers),
    minArticleSimilarity: draft.similarity,
  }
}

function Field({
  label, hint, children,
}: { label: string; hint?: string; children: React.ReactNode }) {
  return (
    <div className="space-y-1">
      <div className="flex items-baseline gap-2">
        <label className="label-xs text-dim">{label}</label>
        {hint && <span className="text-[10px] tracking-wide text-faint">{hint}</span>}
      </div>
      {children}
    </div>
  )
}

const TEXT_AREA =
  'w-full resize-none rounded-sm border border-line bg-canvas px-2 py-1.5 text-[12px] leading-relaxed text-fg outline-none focus:border-accent/70'

export function RuleEditor({
  open,
  thesisId,
  rule,
  onClose,
  onSaved,
}: {
  open: boolean
  thesisId: string
  rule: ThesisRuleDto | null
  onClose: () => void
  onSaved: () => void
}) {
  const [draft, setDraft] = useState<DraftRule>(EMPTY_DRAFT)
  const [saving, setSaving] = useState(false)
  const [deleting, setDeleting] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    if (open) {
      setDraft(rule ? fromRule(rule) : EMPTY_DRAFT)
      setError(null)
    }
  }, [open, rule])

  const handleSave = async () => {
    setSaving(true)
    setError(null)
    try {
      const cmd = toCommand(draft)
      if (rule) {
        await updateThesisRule(thesisId, rule.id, cmd)
      } else {
        await createThesisRule(thesisId, cmd)
      }
      onSaved()
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Save failed')
    } finally {
      setSaving(false)
    }
  }

  const handleDelete = async () => {
    if (!rule) return
    setDeleting(true)
    setError(null)
    try {
      await deleteThesisRule(thesisId, rule.id)
      onSaved()
      onClose()
    } catch (err) {
      setError(err instanceof Error ? err.message : 'Delete failed')
    } finally {
      setDeleting(false)
    }
  }

  return (
    <Sheet open={open} onOpenChange={v => !v && onClose()} width={520}>
      <div className="flex h-full flex-col">
        <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-line bg-surface px-5 py-3">
          <h3 className="text-[13px] font-medium">{rule ? 'Edit rule' : 'New rule'}</h3>
          <SheetClose className="ml-auto rounded-sm p-1 text-dim hover:bg-surface-2 hover:text-fg transition">
            <X className="size-3.5" />
          </SheetClose>
        </header>

        <div className="flex-1 overflow-y-auto px-5 py-4 space-y-4">
          <Field label="Rule name">
            <input
              value={draft.name}
              onChange={e => setDraft(d => ({ ...d, name: e.target.value }))}
              placeholder="e.g. Copper mining coverage"
              className="h-8 w-full rounded-sm border border-line bg-canvas px-2 text-[12px] text-fg outline-none focus:border-accent/70"
            />
          </Field>

          <Field label="Enabled">
            <button
              onClick={() => setDraft(d => ({ ...d, isEnabled: !d.isEnabled }))}
              className={cn(
                'inline-flex h-7 items-center gap-1.5 rounded-sm border px-2 text-[11px] transition',
                draft.isEnabled
                  ? 'border-accent/60 bg-accent-soft text-fg'
                  : 'border-line bg-canvas text-faint hover:bg-surface-2 hover:text-fg',
              )}
            >
              {draft.isEnabled ? <Check className="size-3" /> : <X className="size-3" />}
              <span>{draft.isEnabled ? 'enabled' : 'disabled'}</span>
            </button>
          </Field>

          <Field label="Asset keywords" hint="hard filter: must match somewhere in the cluster">
            <textarea
              value={draft.assetKeywords}
              onChange={e => setDraft(d => ({ ...d, assetKeywords: e.target.value }))}
              placeholder="copper, FCX, SCCO, Chile, Peru"
              rows={2}
              className={TEXT_AREA}
            />
          </Field>

          <Field label="Concept keywords" hint="soft anchors: embedding similarity is also weighed">
            <textarea
              value={draft.conceptKeywords}
              onChange={e => setDraft(d => ({ ...d, conceptKeywords: e.target.value }))}
              placeholder="inventory draw, mine disruption, grid capex"
              rows={2}
              className={TEXT_AREA}
            />
          </Field>

          <Field label="Exclude terms" hint="hard rejects at the cluster level">
            <textarea
              value={draft.excludeTerms}
              onChange={e => setDraft(d => ({ ...d, excludeTerms: e.target.value }))}
              placeholder="best copper stocks, ETF flows, price recap"
              rows={2}
              className={TEXT_AREA}
            />
          </Field>

          <Field label="Event types" hint="restrict to these extracted event types (empty = all)">
            <textarea
              value={draft.eventTypes}
              onChange={e => setDraft(d => ({ ...d, eventTypes: e.target.value }))}
              placeholder="earnings, material_agreement, regulatory_action"
              rows={2}
              className={TEXT_AREA}
            />
          </Field>

          <div className="grid grid-cols-2 gap-3">
            <Field label="Sources" hint="empty = any">
              <textarea
                value={draft.sourceNames}
                onChange={e => setDraft(d => ({ ...d, sourceNames: e.target.value }))}
                placeholder="edgar, mining_com"
                rows={2}
                className={TEXT_AREA}
              />
            </Field>
            <Field label="Source tiers" hint="empty = any">
              <textarea
                value={draft.sourceTiers}
                onChange={e => setDraft(d => ({ ...d, sourceTiers: e.target.value }))}
                placeholder="primary, wire, trade_press"
                rows={2}
                className={TEXT_AREA}
              />
            </Field>
          </div>

          <Field
            label={`Min similarity ${draft.similarity.toFixed(2)}`}
            hint="cosine threshold for embedding-only matches"
          >
            <Slider
              value={draft.similarity}
              min={0}
              max={1}
              step={0.01}
              onChange={v => setDraft(d => ({ ...d, similarity: v }))}
            />
          </Field>

          {error && (
            <div className="text-[11.5px] text-negative">{error}</div>
          )}
        </div>

        <footer className="sticky bottom-0 flex items-center gap-2 border-t border-line bg-surface px-5 py-3">
          {rule && (
            <button
              onClick={handleDelete}
              disabled={deleting || saving}
              className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-negative transition hover:bg-surface-2 disabled:cursor-not-allowed disabled:opacity-50"
            >
              <Trash2 className="size-3" />
              <span>{deleting ? 'Deleting' : 'Delete'}</span>
            </button>
          )}
          <button
            onClick={onClose}
            disabled={saving || deleting}
            className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg disabled:opacity-50"
          >
            Cancel
          </button>
          <button
            onClick={handleSave}
            disabled={saving || deleting}
            className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-accent/60 bg-accent-soft px-2 text-[11px] text-fg transition hover:bg-accent/15 disabled:opacity-50"
          >
            <Check className="size-3" />
            <span>{saving ? 'Saving' : rule ? 'Save' : 'Create'}</span>
          </button>
        </footer>
      </div>
    </Sheet>
  )
}
