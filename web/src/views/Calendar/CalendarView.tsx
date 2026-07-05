import { useEffect, useMemo, useState, type CSSProperties, type MouseEvent } from 'react'
import { AlertCircle, CalendarDays, CandlestickChart, ChevronLeft, ChevronRight, ExternalLink, RefreshCw } from 'lucide-react'
import {
  fetchChartBars,
  fetchChartCalendar,
  fetchEvents,
  type CalendarEntry,
  type EventDto,
  type PriceBarRow,
} from '../../lib/api'
import { cn } from '../../lib/cn'
import { eventLabel, fmtSigned } from '../../lib/format'
import { useIsMobile } from '../../lib/useMediaQuery'
import { EventTypeBadge } from '../../components/EventTypeBadge'
import { SourceTierLabel } from '../../components/SourceTier'
import { Sheet, SheetClose } from '../../components/Sheet'

const WEEKDAYS = ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat']

type DayReturn = { spy: number | null; qqq: number | null }

type CalendarDay = {
  date: Date
  dateKey: string
  isCurrentMonth: boolean
  isToday: boolean
  isPast: boolean
  isWeekend: boolean
  returns: DayReturn
  scheduledEvents: CalendarEntry[]
  extractedEvents: EventDto[]
}

function dateKey(d: Date): string {
  return `${d.getFullYear()}-${String(d.getMonth() + 1).padStart(2, '0')}-${String(d.getDate()).padStart(2, '0')}`
}

function utcDateKey(epochSeconds: number): string {
  const d = new Date(epochSeconds * 1000)
  return `${d.getUTCFullYear()}-${String(d.getUTCMonth() + 1).padStart(2, '0')}-${String(d.getUTCDate()).padStart(2, '0')}`
}

function startOfMonth(d: Date): Date {
  return new Date(d.getFullYear(), d.getMonth(), 1)
}

function addMonths(d: Date, n: number): Date {
  return new Date(d.getFullYear(), d.getMonth() + n, 1)
}

function addDays(d: Date, n: number): Date {
  const r = new Date(d)
  r.setDate(r.getDate() + n)
  return r
}

function isSameDay(a: Date, b: Date): boolean {
  return a.getFullYear() === b.getFullYear() && a.getMonth() === b.getMonth() && a.getDate() === b.getDate()
}

function monthLabel(d: Date): string {
  return d.toLocaleDateString(undefined, { month: 'long', year: 'numeric' })
}

function moveTone(value: number | null | undefined) {
  if (value == null) return 'text-dim'
  if (value > 0.05) return 'text-positive'
  if (value < -0.05) return 'text-negative'
  return 'text-dim'
}

function moveLabel(value: number | null | undefined) {
  return value == null ? '' : `${fmtSigned(value, 2)}%`
}

function cellTint(returns: DayReturn): CSSProperties {
  const ret = returns.spy
  if (ret == null) return {}
  if (Math.abs(ret) < 0.05) return {}
  const color = ret > 0
    ? 'oklch(0.80 0.14 158 / 0.04)'
    : 'oklch(0.72 0.18 25 / 0.04)'
  return { backgroundColor: color }
}

function eventDotColor(eventType: string): string {
  if (eventType === 'earnings') return '#d29922'
  if (eventType === 'macro_release') return '#58a6ff'
  return 'var(--color-accent)'
}

function chartHref(symbol: string) {
  return `/chart?symbol=${encodeURIComponent(symbol)}`
}

function openChart(event: MouseEvent<HTMLAnchorElement>, symbol: string) {
  if (event.metaKey || event.ctrlKey || event.shiftKey || event.altKey || event.button !== 0) return
  event.preventDefault()
  window.history.pushState(null, '', chartHref(symbol))
  window.dispatchEvent(new PopStateEvent('popstate'))
}

function computeReturns(bars: PriceBarRow[]): Map<string, number> {
  const m = new Map<string, number>()
  for (let i = 1; i < bars.length; i++) {
    const key = utcDateKey(bars[i].t)
    const prevClose = bars[i - 1].c
    if (prevClose > 0) m.set(key, (bars[i].c - prevClose) / prevClose * 100)
  }
  return m
}

function buildCalendarDays(
  month: Date,
  today: Date,
  spyBars: PriceBarRow[],
  qqqBars: PriceBarRow[],
  calendarEntries: CalendarEntry[],
  events: EventDto[],
): CalendarDay[] {
  const first = startOfMonth(month)
  const gridStart = addDays(first, -first.getDay())

  const spyReturns = computeReturns(spyBars)
  const qqqReturns = computeReturns(qqqBars)

  const scheduledByDate = new Map<string, CalendarEntry[]>()
  for (const entry of calendarEntries) {
    const key = utcDateKey(entry.t)
    const list = scheduledByDate.get(key) ?? []
    list.push(entry)
    scheduledByDate.set(key, list)
  }

  const eventsByDate = new Map<string, EventDto[]>()
  for (const ev of events) {
    const d = new Date(ev.cluster.lastSeenAt)
    const key = dateKey(d)
    const list = eventsByDate.get(key) ?? []
    list.push(ev)
    eventsByDate.set(key, list)
  }

  const days: CalendarDay[] = []
  for (let i = 0; i < 42; i++) {
    const date = addDays(gridStart, i)
    const key = dateKey(date)
    const isCurrentMonth = date.getMonth() === month.getMonth() && date.getFullYear() === month.getFullYear()
    const isToday = isSameDay(date, today)
    const isPast = date < today && !isToday
    const dayOfWeek = date.getDay()

    const dayEvents = (eventsByDate.get(key) ?? [])
      .sort((a, b) => b.importance - a.importance)

    days.push({
      date,
      dateKey: key,
      isCurrentMonth,
      isToday,
      isPast,
      isWeekend: dayOfWeek === 0 || dayOfWeek === 6,
      returns: { spy: spyReturns.get(key) ?? null, qqq: qqqReturns.get(key) ?? null },
      scheduledEvents: scheduledByDate.get(key) ?? [],
      extractedEvents: dayEvents,
    })
  }
  return days
}

function weekOfDay(days: CalendarDay[], target: Date): CalendarDay[] {
  const targetKey = dateKey(target)
  let weekStart = 0
  for (let i = 0; i < days.length; i++) {
    if (days[i].dateKey === targetKey) {
      weekStart = i - (i % 7)
      break
    }
  }
  return days.slice(weekStart, weekStart + 7)
}

function DayEventItem({ label, eventType, symbol }: { label: string; eventType: string; symbol: string | null }) {
  return (
    <div className="flex items-start gap-1.5 text-[10.5px] leading-snug text-muted">
      <span
        className="mt-[5px] size-[5px] shrink-0 rounded-full"
        style={{ backgroundColor: eventDotColor(eventType) }}
      />
      <span className="min-w-0">
        {symbol ? <span className="font-medium text-fg">{symbol} </span> : null}
        {label}
      </span>
    </div>
  )
}

function DayCell({
  day,
  compact,
  onClick,
}: {
  day: CalendarDay
  compact?: boolean
  onClick: () => void
}) {
  const items: Array<{ label: string; eventType: string; symbol: string | null }> = []

  if (day.isPast || day.isToday) {
    for (const ev of day.extractedEvents.slice(0, compact ? 1 : 3)) {
      items.push({ label: ev.summary, eventType: ev.eventType, symbol: ev.symbol })
    }
  }
  for (const ce of day.scheduledEvents.slice(0, compact ? Math.max(0, 2 - items.length) : Math.max(0, 3 - items.length))) {
    items.push({ label: ce.label, eventType: ce.eventType, symbol: ce.symbol })
  }

  const totalEvents =
    (day.isPast || day.isToday ? day.extractedEvents.length : 0) + day.scheduledEvents.length
  const overflow = totalEvents - items.length

  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'group relative flex min-h-[130px] flex-col border-b border-r border-line-soft p-2 text-left transition hover:bg-surface-2 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/45 focus-visible:z-10',
        day.isCurrentMonth ? 'bg-surface' : 'bg-canvas',
        day.isWeekend && day.isCurrentMonth && 'bg-surface/60',
        day.isToday && 'ring-1 ring-accent/50 z-[1]',
        compact && 'min-h-[90px]',
      )}
      style={day.isPast ? cellTint(day.returns) : undefined}
    >
      <div className="flex items-start justify-between gap-1">
        <span
          className={cn(
            'text-[12px] font-medium tabular',
            day.isToday ? 'flex size-6 items-center justify-center rounded-full bg-accent text-canvas' : '',
            day.isCurrentMonth ? 'text-fg' : 'text-faint',
          )}
        >
          {day.date.getDate()}
        </span>
        {(day.returns.spy != null || day.returns.qqq != null) && (
          <div className="flex flex-col items-end gap-px">
            {day.returns.spy != null && (
              <span className={cn('text-[10px] font-medium tabular', moveTone(day.returns.spy))}>
                S {moveLabel(day.returns.spy)}
              </span>
            )}
            {day.returns.qqq != null && (
              <span className={cn('text-[10px] font-medium tabular', moveTone(day.returns.qqq))}>
                Q {moveLabel(day.returns.qqq)}
              </span>
            )}
          </div>
        )}
      </div>
      {items.length > 0 && (
        <div className="mt-1.5 flex flex-col gap-1">
          {items.map((it, i) => (
            <DayEventItem key={i} label={it.label} eventType={it.eventType} symbol={it.symbol} />
          ))}
          {overflow > 0 && (
            <span className="text-[10px] text-dim">+{overflow} more</span>
          )}
        </div>
      )}
    </button>
  )
}

function MobileDayRow({
  day,
  onClick,
}: {
  day: CalendarDay
  onClick: () => void
}) {
  const totalEvents =
    (day.isPast || day.isToday ? day.extractedEvents.length : 0) + day.scheduledEvents.length

  return (
    <button
      type="button"
      onClick={onClick}
      className={cn(
        'flex items-center gap-3 border-b border-line-soft px-3 py-3 text-left transition hover:bg-surface-2 w-full',
        day.isToday && 'bg-surface-2',
      )}
      style={day.isPast ? cellTint(day.returns) : undefined}
    >
      <div className="w-10 shrink-0 text-center">
        <div
          className={cn(
            'text-[14px] font-medium tabular',
            day.isToday ? 'mx-auto flex size-8 items-center justify-center rounded-full bg-accent text-canvas' : 'text-fg',
          )}
        >
          {day.date.getDate()}
        </div>
        <div className="mt-0.5 text-[10px] text-dim">
          {WEEKDAYS[day.date.getDay()]}
        </div>
      </div>
      <div className="min-w-0 flex-1">
        {totalEvents === 0 ? (
          <span className="text-[12px] text-faint">No events</span>
        ) : (
          <div className="flex flex-col gap-0.5">
            {day.scheduledEvents.slice(0, 2).map((ce, i) => (
              <DayEventItem key={`s${i}`} label={ce.label} eventType={ce.eventType} symbol={ce.symbol} />
            ))}
            {(day.isPast || day.isToday) && day.extractedEvents.slice(0, Math.max(0, 2 - day.scheduledEvents.length)).map((ev, i) => (
              <DayEventItem key={`e${i}`} label={ev.summary} eventType={ev.eventType} symbol={ev.symbol} />
            ))}
            {totalEvents > 2 && (
              <span className="text-[10px] text-dim">+{totalEvents - Math.min(totalEvents, 2)} more</span>
            )}
          </div>
        )}
      </div>
      {(day.returns.spy != null || day.returns.qqq != null) && (
        <div className="shrink-0 text-right">
          {day.returns.spy != null && (
            <div className={cn('text-[12px] font-medium tabular', moveTone(day.returns.spy))}>
              SPY {moveLabel(day.returns.spy)}
            </div>
          )}
          {day.returns.qqq != null && (
            <div className={cn('text-[11px] font-medium tabular', moveTone(day.returns.qqq))}>
              QQQ {moveLabel(day.returns.qqq)}
            </div>
          )}
        </div>
      )}
    </button>
  )
}

function DayDetail({ day }: { day: CalendarDay }) {
  const fullDate = day.date.toLocaleDateString(undefined, {
    weekday: 'long',
    month: 'long',
    day: 'numeric',
    year: 'numeric',
  })

  return (
    <div>
      <div className="flex items-center justify-between border-b border-line-soft px-4 py-3">
        <div>
          <div className="text-[14px] font-semibold text-fg">{fullDate}</div>
          {(day.returns.spy != null || day.returns.qqq != null) && (
            <div className="mt-0.5 flex items-baseline gap-3">
              {day.returns.spy != null && (
                <span className={cn('text-[16px] font-medium tabular', moveTone(day.returns.spy))}>
                  SPY {moveLabel(day.returns.spy)}
                </span>
              )}
              {day.returns.qqq != null && (
                <span className={cn('text-[14px] font-medium tabular', moveTone(day.returns.qqq))}>
                  QQQ {moveLabel(day.returns.qqq)}
                </span>
              )}
            </div>
          )}
        </div>
        <SheetClose className="text-[12px] text-muted hover:text-fg transition">Close</SheetClose>
      </div>

      {day.scheduledEvents.length > 0 && (
        <div>
          <div className="flex h-8 items-center border-b border-line-soft px-4">
            <span className="label-xs text-muted">Scheduled</span>
            <span className="ml-auto text-[10.5px] tabular text-dim">{day.scheduledEvents.length}</span>
          </div>
          <div className="divide-y divide-line-soft">
            {day.scheduledEvents.map(ce => {
              const when = new Date(ce.t * 1000)
              const time = ce.isTimeSpecific
                ? when.toLocaleTimeString(undefined, { hour: 'numeric', minute: '2-digit' })
                : null
              return (
                <div key={ce.id} className="px-4 py-2.5">
                  <div className="flex items-center gap-2">
                    {ce.symbol && (
                      <a
                        href={chartHref(ce.symbol)}
                        onClick={event => openChart(event, ce.symbol!)}
                        className="shrink-0 text-[12px] font-medium tabular text-fg hover:text-accent transition"
                      >
                        {ce.symbol}
                      </a>
                    )}
                    <EventTypeBadge type={ce.eventType} />
                    {time && <span className="ml-auto text-[11px] tabular text-dim">{time}</span>}
                  </div>
                  <div className="mt-1 text-[12.5px] leading-snug text-fg">{ce.label}</div>
                  {ce.notes && <div className="mt-0.5 text-[11px] text-muted">{ce.notes}</div>}
                </div>
              )
            })}
          </div>
        </div>
      )}

      {(day.isPast || day.isToday) && day.extractedEvents.length > 0 && (
        <div>
          <div className="flex h-8 items-center border-b border-line-soft px-4">
            <span className="label-xs text-muted">Events</span>
            <span className="ml-auto text-[10.5px] tabular text-dim">{day.extractedEvents.length}</span>
          </div>
          <div className="divide-y divide-line-soft">
            {day.extractedEvents.map(ev => (
              <div key={ev.clusterId} className="px-4 py-2.5">
                <div className="flex items-center gap-2">
                  {ev.symbol && (
                    <a
                      href={chartHref(ev.symbol)}
                      onClick={event => openChart(event, ev.symbol!)}
                      className="shrink-0 text-[12px] font-medium tabular text-fg hover:text-accent transition"
                    >
                      {ev.symbol}
                    </a>
                  )}
                  <EventTypeBadge type={ev.eventType} />
                  <SourceTierLabel tier={ev.cluster.dominantSourceTier} />
                  <span className="ml-auto text-[11px] tabular text-muted">{ev.importance.toFixed(2)}</span>
                </div>
                <div className="mt-1 text-[12.5px] leading-snug text-fg">{ev.summary}</div>
                <div className="mt-1 flex items-center gap-2 text-[11px] text-muted">
                  <span>{eventLabel(ev.eventType)}</span>
                  {ev.symbol && (
                    <a href={chartHref(ev.symbol)} onClick={event => openChart(event, ev.symbol!)} className="text-dim hover:text-fg" aria-label="Open chart">
                      <CandlestickChart className="size-3" />
                    </a>
                  )}
                  {ev.members?.[0]?.url && (
                    <a href={ev.members[0].url} target="_blank" rel="noreferrer" className="text-dim hover:text-fg" aria-label="Open source">
                      <ExternalLink className="size-3" />
                    </a>
                  )}
                </div>
              </div>
            ))}
          </div>
        </div>
      )}

      {day.scheduledEvents.length === 0 && day.extractedEvents.length === 0 && (
        <div className="px-4 py-8 text-[12px] text-faint">No events for this day</div>
      )}
    </div>
  )
}

function CalendarHeader({
  month,
  loading,
  onPrev,
  onNext,
  onToday,
  onRefresh,
}: {
  month: Date
  loading: boolean
  onPrev: () => void
  onNext: () => void
  onToday: () => void
  onRefresh: () => void
}) {
  return (
    <div className="flex flex-wrap items-center gap-3 border-b border-line bg-canvas px-3 py-3 sm:px-5">
      <div className="flex items-center gap-2">
        <CalendarDays className="size-4 text-accent" />
        <h1 className="text-[16px] font-semibold text-fg">{monthLabel(month)}</h1>
      </div>
      <div className="flex items-center gap-1">
        <button
          type="button"
          onClick={onPrev}
          className="inline-flex size-8 items-center justify-center rounded-md border border-line bg-surface text-muted transition hover:bg-surface-2 hover:text-fg"
          aria-label="Previous month"
        >
          <ChevronLeft className="size-4" />
        </button>
        <button
          type="button"
          onClick={onToday}
          className="inline-flex h-8 items-center rounded-md border border-line bg-surface px-2.5 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
        >
          <span className="label-xs">Today</span>
        </button>
        <button
          type="button"
          onClick={onNext}
          className="inline-flex size-8 items-center justify-center rounded-md border border-line bg-surface text-muted transition hover:bg-surface-2 hover:text-fg"
          aria-label="Next month"
        >
          <ChevronRight className="size-4" />
        </button>
      </div>
      <button
        type="button"
        onClick={onRefresh}
        className="ml-auto inline-flex h-8 items-center gap-1.5 rounded-md border border-line bg-surface px-2.5 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
      >
        <RefreshCw className={cn('size-3.5', loading && 'animate-spin')} />
        <span className="label-xs hidden sm:inline">Refresh</span>
      </button>
    </div>
  )
}

export function CalendarView() {
  const isMobile = useIsMobile()
  const [currentMonth, setCurrentMonth] = useState(() => startOfMonth(new Date()))
  const [mobileWeekOffset, setMobileWeekOffset] = useState(0)
  const [spyBars, setSpyBars] = useState<PriceBarRow[]>([])
  const [qqqBars, setQqqBars] = useState<PriceBarRow[]>([])
  const [calendarEntries, setCalendarEntries] = useState<CalendarEntry[]>([])
  const [events, setEvents] = useState<EventDto[]>([])
  const [loading, setLoading] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const [selectedDay, setSelectedDay] = useState<CalendarDay | null>(null)
  const [stamp, setStamp] = useState(() => Date.now())

  const gridStart = useMemo(() => {
    const first = startOfMonth(currentMonth)
    return addDays(first, -first.getDay())
  }, [currentMonth])

  const gridEnd = useMemo(() => addDays(gridStart, 42), [gridStart])

  useEffect(() => {
    const ac = new AbortController()
    setLoading(true)

    const from = addDays(gridStart, -5)
    const to = gridEnd

    Promise.all([
      fetchChartBars({ symbol: 'SPY', interval: '1d', from, to }, ac.signal),
      fetchChartBars({ symbol: 'QQQ', interval: '1d', from, to }, ac.signal),
      fetchChartCalendar({ from: gridStart, to: gridEnd, take: 500 }, ac.signal),
      fetchEvents({ take: 500, from: gridStart, to: gridEnd }, ac.signal),
    ])
      .then(([spyRes, qqqRes, calRes, evRes]) => {
        setSpyBars(spyRes.bars)
        setQqqBars(qqqRes.bars)
        setCalendarEntries(calRes)
        setEvents(evRes)
        setError(null)
        setLoading(false)
      })
      .catch(err => {
        if ((err as { name?: string }).name === 'AbortError') return
        setError(err instanceof Error ? err.message : 'Request failed')
        setLoading(false)
      })
    return () => ac.abort()
  }, [gridStart, gridEnd, stamp])

  const today = useMemo(() => new Date(stamp), [stamp])

  const calendarDays = useMemo(
    () => buildCalendarDays(currentMonth, today, spyBars, qqqBars, calendarEntries, events),
    [currentMonth, today, spyBars, qqqBars, calendarEntries, events],
  )

  const mobileWeek = useMemo(() => {
    const target = addDays(today, mobileWeekOffset * 7)
    return weekOfDay(calendarDays, target)
  }, [calendarDays, today, mobileWeekOffset])

  const mobileWeekLabel = useMemo(() => {
    if (mobileWeek.length === 0) return ''
    const first = mobileWeek[0].date
    const last = mobileWeek[mobileWeek.length - 1].date
    if (first.getMonth() === last.getMonth()) {
      return `${first.toLocaleDateString(undefined, { month: 'short' })} ${first.getDate()}–${last.getDate()}`
    }
    return `${first.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })} – ${last.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })}`
  }, [mobileWeek])

  if (error && spyBars.length === 0) {
    return (
      <div className="flex items-center gap-2 p-5 text-[13px] text-negative">
        <AlertCircle className="size-4" />
        <span>API error: {error}</span>
      </div>
    )
  }

  return (
    <div className="h-full overflow-y-auto overflow-x-hidden bg-canvas">
      {isMobile ? (
        <>
          <div className="flex items-center gap-3 border-b border-line bg-canvas px-3 py-3">
            <div className="flex items-center gap-2">
              <CalendarDays className="size-4 text-accent" />
              <h1 className="text-[16px] font-semibold text-fg">{mobileWeekLabel}</h1>
            </div>
            <div className="ml-auto flex items-center gap-1">
              <button
                type="button"
                onClick={() => setMobileWeekOffset(o => o - 1)}
                className="inline-flex size-8 items-center justify-center rounded-md border border-line bg-surface text-muted transition hover:bg-surface-2 hover:text-fg"
                aria-label="Previous week"
              >
                <ChevronLeft className="size-4" />
              </button>
              <button
                type="button"
                onClick={() => {
                  setMobileWeekOffset(0)
                  setCurrentMonth(startOfMonth(new Date()))
                }}
                className="inline-flex h-8 items-center rounded-md border border-line bg-surface px-2 text-muted transition hover:bg-surface-2 hover:text-fg"
              >
                <span className="label-xs">Today</span>
              </button>
              <button
                type="button"
                onClick={() => setMobileWeekOffset(o => o + 1)}
                className="inline-flex size-8 items-center justify-center rounded-md border border-line bg-surface text-muted transition hover:bg-surface-2 hover:text-fg"
                aria-label="Next week"
              >
                <ChevronRight className="size-4" />
              </button>
            </div>
          </div>
          <div className="bg-surface">
            {mobileWeek.map(day => (
              <MobileDayRow key={day.dateKey} day={day} onClick={() => setSelectedDay(day)} />
            ))}
          </div>
        </>
      ) : (
        <>
          <CalendarHeader
            month={currentMonth}
            loading={loading}
            onPrev={() => setCurrentMonth(m => addMonths(m, -1))}
            onNext={() => setCurrentMonth(m => addMonths(m, 1))}
            onToday={() => setCurrentMonth(startOfMonth(new Date()))}
            onRefresh={() => setStamp(Date.now())}
          />
          {loading && spyBars.length === 0 ? (
            <div className="p-5 text-[12px] text-faint pulse-soft">Loading…</div>
          ) : (
            <div className="p-3 sm:p-5">
              <div className="overflow-hidden border border-line bg-surface">
                <div className="grid grid-cols-7 border-b border-line bg-canvas">
                  {WEEKDAYS.map(w => (
                    <div key={w} className="px-1.5 py-2 text-center">
                      <span className="label-xs text-muted">{w}</span>
                    </div>
                  ))}
                </div>
                <div className="grid grid-cols-7">
                  {calendarDays.map(day => (
                    <DayCell key={day.dateKey} day={day} onClick={() => setSelectedDay(day)} />
                  ))}
                </div>
              </div>
            </div>
          )}
        </>
      )}

      <Sheet open={selectedDay != null} onOpenChange={open => { if (!open) setSelectedDay(null) }}>
        {selectedDay && <DayDetail day={selectedDay} />}
      </Sheet>
    </div>
  )
}
