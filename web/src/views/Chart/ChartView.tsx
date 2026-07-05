import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  createChart,
  CandlestickSeries,
  HistogramSeries,
  LineSeries,
  PriceScaleMode,
  createSeriesMarkers,
  type IChartApi,
  type ISeriesApi,
  type ISeriesMarkersPluginApi,
  type LineData,
  type MouseEventParams,
  type SeriesMarker,
  type Time,
  type UTCTimestamp,
} from 'lightweight-charts'
import {
  AlertCircle,
  CalendarDays,
  CandlestickChart,
  Check,
  ChevronRight,
  Eye,
  LineChart,
  List,
  PanelRightClose,
  PanelRightOpen,
  RefreshCw,
  Search,
  Settings,
  Star,
  X,
} from 'lucide-react'
import {
  fetchChartBars,
  fetchChartCalendar,
  fetchChartMarks,
  fetchChartSymbolInfo,
  fetchWatchlist,
  searchChartSymbols,
  unwatchSymbol,
  watchSymbol,
  type CalendarEntry,
  type ChartMark,
  type SymbolSearchResult,
  type WatchedSymbol,
} from '../../lib/api'
import { ClusterDrill } from '../Research/ClusterDrill'
import { cn } from '../../lib/cn'
import { useIsMobile } from '../../lib/useMediaQuery'
import { Sheet, SheetClose } from '../../components/Sheet'

type Interval = '5m' | '15m' | '30m' | '1h' | '1d' | '3d' | '1w' | '1mo'

const INTERVALS: { value: Interval; label: string; lookbackDays: number }[] = [
  { value: '5m',  label: '5m',  lookbackDays: 5 },
  { value: '15m', label: '15m', lookbackDays: 14 },
  { value: '30m', label: '30m', lookbackDays: 30 },
  { value: '1h',  label: '1H',  lookbackDays: 60 },
  { value: '1d',  label: '1D',  lookbackDays: 5 * 365 },
  { value: '3d',  label: '3D',  lookbackDays: 5 * 365 },
  { value: '1w',  label: '1W',  lookbackDays: 10 * 365 },
  { value: '1mo', label: '1M',  lookbackDays: 20 * 365 },
]

type ChartType = 'candles' | 'line'
type ScaleMode = 'linear' | 'log'

type ChartSettings = {
  chartType: ChartType
  scaleMode: ScaleMode
  showVolume: boolean
  showEvents: boolean
  showCalendar: boolean
  indicators: Record<IndicatorKey, boolean>
}

type IndicatorKey = 'sma20' | 'sma50' | 'sma200' | 'ema20'

const INDICATORS: { key: IndicatorKey; label: string; color: string; period: number; kind: 'sma' | 'ema' }[] = [
  { key: 'sma20',  label: 'SMA 20',  color: '#1f6feb', period: 20,  kind: 'sma' },
  { key: 'sma50',  label: 'SMA 50',  color: '#bf8700', period: 50,  kind: 'sma' },
  { key: 'sma200', label: 'SMA 200', color: '#8250df', period: 200, kind: 'sma' },
  { key: 'ema20',  label: 'EMA 20',  color: '#1a7f37', period: 20,  kind: 'ema' },
]

const DEFAULT_SETTINGS: ChartSettings = {
  chartType: 'candles',
  scaleMode: 'linear',
  showVolume: true,
  showEvents: false,
  showCalendar: true,
  indicators: { sma20: false, sma50: true, sma200: true, ema20: false },
}

function loadSettings(): ChartSettings {
  try {
    const raw = window.localStorage.getItem('marketlens.chart.settings')
    if (!raw) return DEFAULT_SETTINGS
    const parsed = JSON.parse(raw) as Partial<ChartSettings>
    return {
      ...DEFAULT_SETTINGS,
      ...parsed,
      indicators: { ...DEFAULT_SETTINGS.indicators, ...(parsed.indicators ?? {}) },
    }
  } catch {
    return DEFAULT_SETTINGS
  }
}

function computeSMA(bars: { t: number; c: number }[], period: number): LineData<UTCTimestamp>[] {
  if (bars.length < period) return []
  const out: LineData<UTCTimestamp>[] = []
  let sum = 0
  for (let i = 0; i < period; i++) sum += bars[i].c
  out.push({ time: bars[period - 1].t as UTCTimestamp, value: sum / period })
  for (let i = period; i < bars.length; i++) {
    sum += bars[i].c - bars[i - period].c
    out.push({ time: bars[i].t as UTCTimestamp, value: sum / period })
  }
  return out
}

function computeEMA(bars: { t: number; c: number }[], period: number): LineData<UTCTimestamp>[] {
  if (bars.length < period) return []
  const k = 2 / (period + 1)
  const out: LineData<UTCTimestamp>[] = []
  let sum = 0
  for (let i = 0; i < period; i++) sum += bars[i].c
  let ema = sum / period
  out.push({ time: bars[period - 1].t as UTCTimestamp, value: ema })
  for (let i = period; i < bars.length; i++) {
    ema = bars[i].c * k + ema * (1 - k)
    out.push({ time: bars[i].t as UTCTimestamp, value: ema })
  }
  return out
}

function isAbort(err: unknown): boolean {
  return (err as { name?: string }).name === 'AbortError'
}

function shapeForMark(mark: ChartMark, displayTime: number, stackSize = 1): SeriesMarker<Time> {
  const isPositive = mark.sentiment > 0.05
  const isNegative = mark.sentiment < -0.05
  const color = isPositive ? '#3fb950' : isNegative ? '#f85149' : '#8b949e'
  const importance = Math.max(0.2, Math.min(1, mark.importance))
  return {
    time: displayTime as UTCTimestamp,
    position: isPositive ? 'aboveBar' : 'belowBar',
    color,
    shape: isPositive ? 'arrowUp' : isNegative ? 'arrowDown' : 'circle',
    text: stackSize > 1 ? `${stackSize} events` : mark.eventType,
    size: importance > 0.6 ? 2 : 1,
  }
}

function shapeForCalendar(entry: CalendarEntry, displayTime: number, stackSize = 1): SeriesMarker<Time> {
  const color = entry.eventType === 'earnings' ? '#d29922' : '#58a6ff'
  return {
    time: displayTime as UTCTimestamp,
    position: 'belowBar',
    color,
    shape: 'square',
    text: stackSize > 1 ? `${stackSize} calendar` : entry.label.length > 24 ? entry.label.slice(0, 24) + '…' : entry.label,
    size: 1,
  }
}

function nearestBarTime(bars: { t: number }[], target: number): number | null {
  if (bars.length === 0) return null
  let lo = 0
  let hi = bars.length - 1
  while (lo < hi) {
    const mid = Math.floor((lo + hi) / 2)
    if (bars[mid].t < target) lo = mid + 1
    else hi = mid
  }
  const right = bars[lo]
  const left = lo > 0 ? bars[lo - 1] : null
  if (!left) return right.t
  return Math.abs(left.t - target) <= Math.abs(right.t - target) ? left.t : right.t
}

function pctClass(value: number | null | undefined): string {
  if (value == null) return 'text-faint'
  return value >= 0 ? 'text-positive' : 'text-negative'
}

function formatPct(value: number | null | undefined): string {
  if (value == null) return '—'
  return `${value >= 0 ? '+' : ''}${value.toFixed(2)}%`
}

function formatPrice(value: number | null | undefined): string {
  if (value == null) return '—'
  if (Math.abs(value) >= 1000) return value.toLocaleString(undefined, { maximumFractionDigits: 2 })
  return value.toFixed(2)
}

function SymbolSearch({
  current,
  onSelect,
  onAdHoc,
  watchlistVersion,
}: {
  current: string | null
  onSelect: (symbol: string) => void
  onAdHoc: (symbol: string) => void
  watchlistVersion: number
}) {
  const [open, setOpen] = useState(false)
  const [query, setQuery] = useState('')
  const [results, setResults] = useState<SymbolSearchResult[]>([])
  const [loading, setLoading] = useState(false)
  const inputRef = useRef<HTMLInputElement | null>(null)

  useEffect(() => {
    if (!open) return
    const ac = new AbortController()
    const handle = setTimeout(() => {
      setLoading(true)
      searchChartSymbols(query, ac.signal)
        .then(r => { setResults(r); setLoading(false) })
        .catch(err => { if (!isAbort(err)) setLoading(false) })
    }, 150)
    return () => {
      clearTimeout(handle)
      ac.abort()
    }
  }, [query, open, watchlistVersion])

  useEffect(() => {
    if (open) inputRef.current?.focus()
  }, [open])

  const exactQuery = query.trim().toUpperCase()
  const noExact = exactQuery.length > 0 && !results.some(r => r.symbol === exactQuery)

  return (
    <div className="relative">
      <button
        onClick={() => setOpen(o => !o)}
        className="inline-flex h-8 items-center gap-2 rounded-sm border border-line bg-canvas px-3 text-[12.5px] font-medium tabular text-fg transition hover:bg-surface-2"
      >
        <Search className="size-3.5 text-dim" />
        <span>{current ?? 'Symbol'}</span>
      </button>
      {open && (
        <>
          <div className="fixed inset-0 z-20" onClick={() => setOpen(false)} />
          <div className="absolute left-0 top-full z-30 mt-1 w-72 max-w-[calc(100vw-1.5rem)] rounded-sm border border-line bg-canvas shadow-lg">
            <div className="flex items-center gap-2 border-b border-line-soft px-2 py-1.5">
              <Search className="size-3 text-dim" />
              <input
                ref={inputRef}
                value={query}
                onChange={e => setQuery(e.target.value)}
                onKeyDown={e => {
                  if (e.key === 'Enter' && exactQuery.length > 0) {
                    onAdHoc(exactQuery)
                    setOpen(false); setQuery('')
                  }
                }}
                placeholder="Type any ticker…"
                className="h-6 flex-1 bg-transparent text-[12px] text-fg outline-none"
              />
              <button onClick={() => setOpen(false)} className="text-dim hover:text-fg">
                <X className="size-3" />
              </button>
            </div>
            <ul className="max-h-72 overflow-y-auto py-1">
              {loading && <li className="px-3 py-2 text-[11px] text-faint pulse-soft">Searching…</li>}
              {!loading && results.length === 0 && exactQuery.length === 0 && (
                <li className="px-3 py-2 text-[11px] text-faint">Type a ticker or company name</li>
              )}
              {!loading && results.length === 0 && exactQuery.length > 0 && (
                <li className="px-3 py-2 text-[11px] text-faint">No registry matches</li>
              )}
              {results.map(r => (
                <li key={r.symbol}>
                  <button
                    onClick={() => { onSelect(r.symbol); setOpen(false); setQuery('') }}
                    className="flex w-full items-center gap-2 px-3 py-1.5 text-left hover:bg-surface-2"
                  >
                    <Star className={cn('size-3 shrink-0', r.watched ? 'fill-accent text-accent' : 'text-faint')} />
                    <span className="text-[12px] font-medium tabular text-fg">{r.symbol}</span>
                    <span className="truncate text-[10.5px] text-dim">{r.description}</span>
                    {r.source && r.source !== 'registry' && (
                      <span className="ml-auto shrink-0 rounded-sm border border-line-soft px-1 text-[9.5px] uppercase text-faint">
                        {r.source}
                      </span>
                    )}
                  </button>
                </li>
              ))}
              {noExact && (
                <li>
                  <button
                    onClick={() => { onAdHoc(exactQuery); setOpen(false); setQuery('') }}
                    className="flex w-full items-center gap-2 border-t border-line-soft px-3 py-1.5 text-left text-[11px] text-muted hover:bg-surface-2 hover:text-fg"
                  >
                    <Search className="size-3 text-dim" />
                    Open <span className="font-mono text-fg">{exactQuery}</span> and check price coverage
                  </button>
                </li>
              )}
            </ul>
          </div>
        </>
      )}
    </div>
  )
}

function WatchToggle({
  symbol,
  watched,
  onChange,
}: {
  symbol: string | null
  watched: boolean
  onChange: () => void
}) {
  if (!symbol) return null
  return (
    <button
      onClick={onChange}
      title={watched ? 'Unwatch this symbol' : 'Add to watchlist'}
      className={cn(
        'inline-flex h-8 items-center gap-1 rounded-sm border px-2 text-[11px] transition',
        watched
          ? 'border-accent/60 bg-accent-soft text-fg'
          : 'border-line bg-canvas text-dim hover:bg-surface-2 hover:text-fg',
      )}
    >
      <Star className={cn('size-3.5', watched && 'fill-current')} />
      <span className="hidden sm:inline">{watched ? 'Watching' : 'Watch'}</span>
    </button>
  )
}

function IntervalBar({
  value,
  onChange,
}: {
  value: Interval
  onChange: (v: Interval) => void
}) {
  return (
    <div className="inline-flex overflow-hidden rounded-sm border border-line">
      {INTERVALS.map(iv => (
        <button
          key={iv.value}
          onClick={() => onChange(iv.value)}
          className={cn(
            'h-8 px-3 text-[11.5px] tabular transition',
            value === iv.value
              ? 'bg-surface-2 text-fg'
              : 'bg-canvas text-dim hover:bg-surface hover:text-fg',
          )}
        >
          {iv.label}
        </button>
      ))}
    </div>
  )
}

function OverlayToggles({
  showEvents,
  showCalendar,
  onToggleEvents,
  onToggleCalendar,
}: {
  showEvents: boolean
  showCalendar: boolean
  onToggleEvents: () => void
  onToggleCalendar: () => void
}) {
  return (
    <div className="ml-2 flex items-center gap-1">
      <button
        onClick={onToggleEvents}
        className={cn(
          'h-7 rounded-sm border px-2 text-[11px] transition',
          showEvents
            ? 'border-accent/60 bg-accent-soft text-fg'
            : 'border-line bg-canvas text-dim hover:text-fg',
        )}
      >
        Events
      </button>
      <button
        onClick={onToggleCalendar}
        className={cn(
          'h-7 rounded-sm border px-2 text-[11px] transition',
          showCalendar
            ? 'border-accent/60 bg-accent-soft text-fg'
            : 'border-line bg-canvas text-dim hover:text-fg',
        )}
      >
        Calendar
      </button>
    </div>
  )
}

function IndicatorMenu({
  active,
  onToggle,
}: {
  active: Record<IndicatorKey, boolean>
  onToggle: (key: IndicatorKey) => void
}) {
  const [open, setOpen] = useState(false)
  const count = Object.values(active).filter(Boolean).length
  return (
    <div className="relative">
      <button
        onClick={() => setOpen(o => !o)}
        className={cn(
          'inline-flex h-7 items-center gap-1 rounded-sm border px-2 text-[11px] transition',
          count > 0
            ? 'border-accent/60 bg-accent-soft text-fg'
            : 'border-line bg-canvas text-dim hover:text-fg',
        )}
      >
        Indicators{count > 0 && ` (${count})`}
      </button>
      {open && (
        <>
          <div className="fixed inset-0 z-20" onClick={() => setOpen(false)} />
          <div className="absolute left-0 top-full z-30 mt-1 w-44 rounded-sm border border-line bg-canvas py-1 shadow-lg">
            {INDICATORS.map(ind => (
              <button
                key={ind.key}
                onClick={() => onToggle(ind.key)}
                className="flex w-full items-center gap-2 px-3 py-1.5 text-left text-[11.5px] hover:bg-surface-2"
              >
                <span
                  className="inline-block size-2 rounded-full"
                  style={{ background: ind.color, opacity: active[ind.key] ? 1 : 0.25 }}
                />
                <span className={active[ind.key] ? 'text-fg' : 'text-dim'}>{ind.label}</span>
              </button>
            ))}
          </div>
        </>
      )}
    </div>
  )
}

type CrosshairInfo = {
  time: number
  open: number
  high: number
  low: number
  close: number
  volume: number | null
  mark: ChartMark | null
  calendar: CalendarEntry | null
} | null

function ChartCanvas({
  symbol,
  interval,
  chartType,
  scaleMode,
  indicators,
  showVolume,
  showEvents,
  showCalendar,
  onSelectCluster,
  onSelectEventStack,
  onSelectCalendar,
  onEventsLoaded,
  onLatestBar,
}: {
  symbol: string
  interval: Interval
  chartType: ChartType
  scaleMode: ScaleMode
  indicators: Record<IndicatorKey, boolean>
  showVolume: boolean
  showEvents: boolean
  showCalendar: boolean
  onSelectCluster: (clusterId: string) => void
  onSelectEventStack: (marks: ChartMark[]) => void
  onSelectCalendar: (entry: CalendarEntry) => void
  onEventsLoaded: (marks: ChartMark[], calendar: CalendarEntry[]) => void
  onLatestBar: (t: number) => void
}) {
  const containerRef = useRef<HTMLDivElement | null>(null)
  const chartRef = useRef<IChartApi | null>(null)
  const candleSeriesRef = useRef<ISeriesApi<'Candlestick'> | null>(null)
  const lineSeriesRef = useRef<ISeriesApi<'Line'> | null>(null)
  const volumeSeriesRef = useRef<ISeriesApi<'Histogram'> | null>(null)
  const markersPluginRef = useRef<ISeriesMarkersPluginApi<Time> | null>(null)
  const markerHostRef = useRef<ChartType | null>(null)
  const markIndexRef = useRef<Map<number, ChartMark[]>>(new Map())
  const calendarIndexRef = useRef<Map<number, CalendarEntry[]>>(new Map())
  const indicatorSeriesRef = useRef<Map<IndicatorKey, ISeriesApi<'Line'>>>(new Map())
  const barsRef = useRef<{ t: number; o: number; h: number; l: number; c: number; v: number | null }[]>([])

  const [error, setError] = useState<string | null>(null)
  const [empty, setEmpty] = useState(false)
  const [loading, setLoading] = useState(false)
  const [crosshair, setCrosshair] = useState<CrosshairInfo>(null)
  const [cursorPos, setCursorPos] = useState<{ x: number; y: number } | null>(null)
  const [containerWidth, setContainerWidth] = useState(800)

  const reconcileIndicators = useCallback(() => {
    const chart = chartRef.current
    if (!chart) return
    const bars = barsRef.current
    const current = indicatorSeriesRef.current

    for (const ind of INDICATORS) {
      const enabled = indicators[ind.key]
      const existing = current.get(ind.key)
      if (enabled && bars.length > 0) {
        const data = ind.kind === 'sma'
          ? computeSMA(bars, ind.period)
          : computeEMA(bars, ind.period)
        if (existing) {
          existing.setData(data)
        } else {
          const lineSeries = chart.addSeries(LineSeries, {
            color: ind.color,
            lineWidth: 1,
            priceLineVisible: false,
            lastValueVisible: false,
            crosshairMarkerVisible: false,
          })
          lineSeries.setData(data)
          current.set(ind.key, lineSeries)
        }
      } else if (existing) {
        chart.removeSeries(existing)
        current.delete(ind.key)
      }
    }
  }, [indicators])

  useEffect(() => {
    if (!containerRef.current) return
    setContainerWidth(containerRef.current.clientWidth || 800)
    const chart = createChart(containerRef.current, {
      layout: {
        background: { color: '#ffffff' },
        textColor: '#1f2328',
        fontSize: 11,
        attributionLogo: false,
      },
      grid: {
        vertLines: { color: '#eaeef2' },
        horzLines: { color: '#eaeef2' },
      },
      rightPriceScale: { borderColor: '#d0d7de' },
      timeScale: {
        borderColor: '#d0d7de',
        timeVisible: true,
        secondsVisible: false,
      },
      crosshair: { mode: 1 },
      autoSize: true,
    })

    const candles = chart.addSeries(CandlestickSeries, {
      upColor: '#ffffff',
      downColor: '#000000',
      borderUpColor: '#000000',
      borderDownColor: '#000000',
      wickUpColor: '#000000',
      wickDownColor: '#000000',
      borderVisible: true,
    })
    const line = chart.addSeries(LineSeries, {
      color: '#1f6feb',
      lineWidth: 2,
      priceLineVisible: true,
      lastValueVisible: true,
    })
    const volume = chart.addSeries(HistogramSeries, {
      priceFormat: { type: 'volume' },
      priceScaleId: 'volume',
      color: '#9aa4b2',
    })
    chart.priceScale('volume').applyOptions({
      scaleMargins: { top: 0.85, bottom: 0 },
    })

    chartRef.current = chart
    candleSeriesRef.current = candles
    lineSeriesRef.current = line
    volumeSeriesRef.current = volume
    const resizeObserver = new ResizeObserver(([entry]) => {
      setContainerWidth(entry.contentRect.width || 800)
    })
    resizeObserver.observe(containerRef.current)

    const handleClick = (param: MouseEventParams) => {
      if (param.time == null) return
      const ts = typeof param.time === 'number' ? param.time : 0
      const marks = markIndexRef.current.get(ts) ?? []
      if (marks.length === 1) onSelectCluster(marks[0].clusterId)
      else if (marks.length > 1) onSelectEventStack(marks)
      else {
        const calendar = calendarIndexRef.current.get(ts) ?? []
        if (calendar.length > 0) onSelectCalendar(calendar[0])
      }
    }
    chart.subscribeClick(handleClick)

    const handleCrosshair = (param: MouseEventParams) => {
      if (param.time == null || !param.point) {
        setCrosshair(null)
        setCursorPos(null)
        return
      }
      const ts = typeof param.time === 'number' ? param.time : 0
      const candleData = candles && param.seriesData.get(candles) as { open?: number; high?: number; low?: number; close?: number } | undefined
      const volumeData = volume && param.seriesData.get(volume) as { value?: number } | undefined
      const bar = barsRef.current.find(b => b.t === ts)
      const ohlc = candleData?.open
        ? { open: candleData.open!, high: candleData.high!, low: candleData.low!, close: candleData.close! }
        : bar
          ? { open: bar.o, high: bar.h, low: bar.l, close: bar.c }
          : null
      if (!ohlc) {
        setCrosshair(null)
        setCursorPos(null)
        return
      }
      setCrosshair({
        time: ts,
        open: ohlc.open,
        high: ohlc.high,
        low: ohlc.low,
        close: ohlc.close,
        volume: volumeData?.value ?? bar?.v ?? null,
        mark: markIndexRef.current.get(ts)?.[0] ?? null,
        calendar: calendarIndexRef.current.get(ts)?.[0] ?? null,
      })
      setCursorPos({ x: param.point.x, y: param.point.y })
    }
    chart.subscribeCrosshairMove(handleCrosshair)

    return () => {
      resizeObserver.disconnect()
      chart.remove()
      chartRef.current = null
      candleSeriesRef.current = null
      lineSeriesRef.current = null
      volumeSeriesRef.current = null
      markersPluginRef.current = null
      markerHostRef.current = null
      indicatorSeriesRef.current = new Map()
    }
  }, [onSelectCalendar, onSelectCluster, onSelectEventStack])

  useEffect(() => {
    candleSeriesRef.current?.applyOptions({ visible: chartType === 'candles' })
    lineSeriesRef.current?.applyOptions({ visible: chartType === 'line' })
    volumeSeriesRef.current?.applyOptions({ visible: showVolume })
    chartRef.current?.priceScale('right').applyOptions({
      mode: scaleMode === 'log' ? PriceScaleMode.Logarithmic : PriceScaleMode.Normal,
    })
  }, [chartType, scaleMode, showVolume])

  useEffect(() => {
    if (!candleSeriesRef.current || !lineSeriesRef.current || !volumeSeriesRef.current) return
    const series = candleSeriesRef.current
    const line = lineSeriesRef.current
    const volume = volumeSeriesRef.current

    const lookbackDays = INTERVALS.find(i => i.value === interval)?.lookbackDays ?? 365
    const to = new Date()
    const from = new Date(to.getTime() - lookbackDays * 24 * 60 * 60 * 1000)

    const ac = new AbortController()
    setLoading(true)
    setError(null)
    setEmpty(false)

    Promise.all([
      fetchChartBars({ symbol, interval, from, to }, ac.signal),
      showEvents ? fetchChartMarks({ symbol, from, to }, ac.signal) : Promise.resolve(null),
      showCalendar ? fetchChartCalendar({ symbol, from, to }, ac.signal) : Promise.resolve(null),
    ])
      .then(([barsRes, marksRes, calendarRes]) => {
        if (barsRes.bars.length === 0) {
          setEmpty(true)
          series.setData([])
          line.setData([])
          volume.setData([])
          barsRef.current = []
          markIndexRef.current.clear()
          calendarIndexRef.current.clear()
          if (markersPluginRef.current) markersPluginRef.current.setMarkers([])
          for (const s of indicatorSeriesRef.current.values()) chartRef.current?.removeSeries(s)
          indicatorSeriesRef.current.clear()
          onEventsLoaded([], [])
          return
        }

        barsRef.current = barsRes.bars
        const latestT = barsRes.bars[barsRes.bars.length - 1].t
        onLatestBar(latestT)

        series.setData(barsRes.bars.map(b => ({
          time: b.t as UTCTimestamp,
          open: b.o, high: b.h, low: b.l, close: b.c,
        })))
        line.setData(barsRes.bars.map(b => ({
          time: b.t as UTCTimestamp,
          value: b.c,
        })))
        volume.setData(barsRes.bars.map(b => ({
          time: b.t as UTCTimestamp,
          value: b.v ?? 0,
          color: b.c >= b.o ? '#d0d7de' : '#3a3a3a',
        })))

        const markers: SeriesMarker<Time>[] = []
        const markIdx = new Map<number, ChartMark[]>()
        const calIdx = new Map<number, CalendarEntry[]>()
        const loadedMarks = marksRes?.marks ?? []
        const loadedCalendar = calendarRes ?? []

        if (marksRes) {
          for (const m of marksRes.marks) {
            const displayT = nearestBarTime(barsRes.bars, m.t)
            if (displayT == null) continue
            const stack = markIdx.get(displayT) ?? []
            stack.push(m)
            markIdx.set(displayT, stack)
          }
        }

        if (calendarRes) {
          for (const c of calendarRes) {
            const displayT = nearestBarTime(barsRes.bars, c.t)
            if (displayT == null) continue
            const stack = calIdx.get(displayT) ?? []
            stack.push(c)
            calIdx.set(displayT, stack)
          }
        }

        for (const [time, stack] of markIdx) {
          const sortedStack = [...stack].sort((a, b) => b.importance - a.importance)
          markIdx.set(time, sortedStack)
          markers.push(shapeForMark(sortedStack[0], time, sortedStack.length))
        }
        for (const [time, stack] of calIdx) {
          const sortedStack = [...stack].sort((a, b) => a.t - b.t)
          calIdx.set(time, sortedStack)
          markers.push(shapeForCalendar(sortedStack[0], time, sortedStack.length))
        }

        markers.sort((a, b) => (a.time as number) - (b.time as number))
        markIndexRef.current = markIdx
        calendarIndexRef.current = calIdx
        onEventsLoaded(loadedMarks, loadedCalendar)

        const markerHost = chartType === 'line' ? line : series
        if (markerHostRef.current !== chartType) {
          markersPluginRef.current?.detach()
          markersPluginRef.current = null
          markerHostRef.current = chartType
        }

        if (markersPluginRef.current) {
          markersPluginRef.current.setMarkers(markers)
        } else {
          markersPluginRef.current = createSeriesMarkers(markerHost, markers)
        }

        reconcileIndicators()
        chartRef.current?.timeScale().fitContent()
      })
      .catch(err => {
        if (isAbort(err)) return
        setError(err instanceof Error ? err.message : 'Request failed')
      })
      .finally(() => setLoading(false))

    return () => ac.abort()
  }, [symbol, interval, chartType, showEvents, showCalendar, onEventsLoaded, onLatestBar, reconcileIndicators])

  useEffect(() => {
    reconcileIndicators()
  }, [reconcileIndicators])

  return (
    <div className="relative h-full w-full bg-white">
      <div ref={containerRef} className="absolute inset-0" />

      {crosshair && (
        <div className="pointer-events-none absolute left-3 top-3 rounded-sm border border-[#d0d7de] bg-white/95 px-2 py-1.5 text-[10.5px] tabular text-[#1f2328] shadow-md">
          <div className="text-[10px] text-[#656d76]">
            {new Date(crosshair.time * 1000).toLocaleString(undefined, {
              year: 'numeric', month: 'short', day: 'numeric',
              hour: interval !== '1d' ? '2-digit' : undefined,
              minute: interval !== '1d' ? '2-digit' : undefined,
            })}
          </div>
          <div className="mt-0.5 flex gap-2">
            <span><span className="text-[#656d76]">O</span> {crosshair.open.toFixed(2)}</span>
            <span><span className="text-[#656d76]">H</span> {crosshair.high.toFixed(2)}</span>
            <span><span className="text-[#656d76]">L</span> {crosshair.low.toFixed(2)}</span>
            <span className={crosshair.close >= crosshair.open ? 'text-[#1a7f37]' : 'text-[#cf222e]'}>
              <span className="text-[#656d76]">C</span> {crosshair.close.toFixed(2)}
            </span>
          </div>
          {crosshair.volume != null && crosshair.volume > 0 && (
            <div className="mt-0.5 text-[10px] text-[#656d76]">
              vol {(crosshair.volume / 1e6).toFixed(2)}M
            </div>
          )}
        </div>
      )}

      {crosshair?.mark && cursorPos && (
        <div
          className="pointer-events-none absolute z-10 max-w-xs rounded-sm border border-[#d0d7de] bg-white/97 px-2.5 py-2 text-[11px] leading-snug text-[#1f2328] shadow-md"
          style={{
            left: Math.min(cursorPos.x + 14, containerWidth - 280),
            top: Math.max(cursorPos.y - 60, 10),
          }}
        >
          <div className="mb-1 flex items-center gap-1.5">
            <span className="text-[9.5px] tracking-[0.085em] uppercase text-[#656d76]">{crosshair.mark.eventType.replaceAll('_', ' ')}</span>
            <span className="text-[9.5px] tabular text-[#656d76]">imp {crosshair.mark.importance.toFixed(2)}</span>
            <span className={cn(
              'text-[9.5px] tabular',
              crosshair.mark.sentiment > 0.05 ? 'text-[#1a7f37]' :
              crosshair.mark.sentiment < -0.05 ? 'text-[#cf222e]' : 'text-[#656d76]',
            )}>
              sent {crosshair.mark.sentiment.toFixed(2)}
            </span>
          </div>
          <div className="line-clamp-3 text-[11px] text-[#1f2328]">{crosshair.mark.summary}</div>
          <div className="mt-1 text-[9.5px] text-[#656d76]">click to drill</div>
        </div>
      )}

      {crosshair?.calendar && !crosshair?.mark && cursorPos && (
        <div
          className="pointer-events-none absolute z-10 max-w-xs rounded-sm border border-[#d0d7de] bg-white/97 px-2.5 py-2 text-[11px] leading-snug text-[#1f2328] shadow-md"
          style={{
            left: Math.min(cursorPos.x + 14, containerWidth - 280),
            top: Math.max(cursorPos.y - 50, 10),
          }}
        >
          <div className="mb-1 text-[9.5px] tracking-[0.085em] uppercase text-[#656d76]">
            {crosshair.calendar.eventType.replaceAll('_', ' ')}
          </div>
          <div className="text-[11px] text-[#1f2328]">{crosshair.calendar.label}</div>
          {crosshair.calendar.notes && (
            <div className="mt-0.5 text-[10px] text-[#656d76]">{crosshair.calendar.notes}</div>
          )}
        </div>
      )}

      {loading && (
        <div className="absolute right-3 top-3 rounded-sm bg-surface/80 px-2 py-1 text-[10.5px] text-dim pulse-soft">
          Loading…
        </div>
      )}
      {error && (
        <div className="absolute left-1/2 top-4 flex -translate-x-1/2 items-center gap-2 rounded-sm border border-negative/40 bg-canvas px-3 py-2 text-[12px] text-negative">
          <AlertCircle className="size-3.5" />
          {error}
        </div>
      )}
      {empty && !loading && !error && (
        <div className="absolute left-1/2 top-1/2 -translate-x-1/2 -translate-y-1/2 max-w-md text-center text-[12px] text-faint">
          No price bars for {symbol} at {interval} yet. The backfill service may still be running, or
          your market-data tier may not include this resolution.
        </div>
      )}
    </div>
  )
}

function ChartSettingsButton({
  settings,
  onChange,
}: {
  settings: ChartSettings
  onChange: (next: ChartSettings) => void
}) {
  const [open, setOpen] = useState(false)
  const update = (patch: Partial<ChartSettings>) => onChange({ ...settings, ...patch })
  const toggleIndicator = (key: IndicatorKey) => update({
    indicators: { ...settings.indicators, [key]: !settings.indicators[key] },
  })

  return (
    <>
      <button
        onClick={() => setOpen(true)}
        className="inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
      >
        <Settings className="size-3" />
        <span className="hidden sm:inline">Settings</span>
      </button>
      <Sheet open={open} onOpenChange={setOpen} width={380}>
        <div className="flex h-full flex-col">
          <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-line bg-surface px-4 py-3">
            <h3 className="text-[12px] font-medium">Chart Settings</h3>
            <SheetClose className="ml-auto rounded-sm p-1 text-dim transition hover:bg-surface-2 hover:text-fg">
              <X className="size-3.5" />
            </SheetClose>
          </header>
          <div className="space-y-5 p-4">
            <section>
              <div className="label-xs mb-2">Style</div>
              <div className="grid grid-cols-2 gap-2">
                <button
                  onClick={() => update({ chartType: 'candles' })}
                  className={cn(
                    'flex h-9 items-center justify-center gap-2 rounded-sm border text-[12px]',
                    settings.chartType === 'candles' ? 'border-accent/60 bg-accent-soft text-fg' : 'border-line text-muted hover:bg-surface-2',
                  )}
                >
                  <CandlestickChart className="size-3.5" />
                  Candles
                </button>
                <button
                  onClick={() => update({ chartType: 'line' })}
                  className={cn(
                    'flex h-9 items-center justify-center gap-2 rounded-sm border text-[12px]',
                    settings.chartType === 'line' ? 'border-accent/60 bg-accent-soft text-fg' : 'border-line text-muted hover:bg-surface-2',
                  )}
                >
                  <LineChart className="size-3.5" />
                  Line
                </button>
              </div>
            </section>

            <section>
              <div className="label-xs mb-2">Scale</div>
              <div className="inline-flex overflow-hidden rounded-sm border border-line">
                {(['linear', 'log'] as ScaleMode[]).map(mode => (
                  <button
                    key={mode}
                    onClick={() => update({ scaleMode: mode })}
                    className={cn(
                      'h-8 px-3 text-[11.5px] capitalize transition',
                      settings.scaleMode === mode ? 'bg-surface-2 text-fg' : 'text-dim hover:bg-surface hover:text-fg',
                    )}
                  >
                    {mode}
                  </button>
                ))}
              </div>
            </section>

            <section>
              <div className="label-xs mb-2">Overlays</div>
              <div className="space-y-1">
                {([
                  { key: 'showVolume', label: 'Volume', Icon: Eye },
                  { key: 'showEvents', label: 'Events', Icon: List },
                  { key: 'showCalendar', label: 'Calendar', Icon: CalendarDays },
                ] as const).map(({ key, label, Icon }) => {
                  const enabled = settings[key as keyof ChartSettings] === true
                  return (
                    <button
                      key={key}
                      onClick={() => update({ [key]: !enabled } as Partial<ChartSettings>)}
                      className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-[12px] text-muted transition hover:bg-surface-2 hover:text-fg"
                    >
                      <Icon className="size-3.5 text-dim" />
                      <span>{label}</span>
                      <Check className={cn('ml-auto size-3.5 text-accent', enabled ? 'opacity-100' : 'opacity-0')} />
                    </button>
                  )
                })}
              </div>
            </section>

            <section>
              <div className="label-xs mb-2">Indicators</div>
              <div className="space-y-1">
                {INDICATORS.map(ind => (
                  <button
                    key={ind.key}
                    onClick={() => toggleIndicator(ind.key)}
                    className="flex w-full items-center gap-2 rounded-sm px-2 py-1.5 text-left text-[12px] text-muted transition hover:bg-surface-2 hover:text-fg"
                  >
                    <span className="inline-block size-2 rounded-full" style={{ background: ind.color }} />
                    <span>{ind.label}</span>
                    <Check className={cn('ml-auto size-3.5 text-accent', settings.indicators[ind.key] ? 'opacity-100' : 'opacity-0')} />
                  </button>
                ))}
              </div>
            </section>
          </div>
        </div>
      </Sheet>
    </>
  )
}

function EventsPanel({
  marks,
  calendar,
  onSelectCluster,
  onSelectCalendar,
}: {
  marks: ChartMark[]
  calendar: CalendarEntry[]
  onSelectCluster: (clusterId: string) => void
  onSelectCalendar: (entry: CalendarEntry) => void
}) {
  const sortedMarks = [...marks].sort((a, b) => b.t - a.t).slice(0, 80)
  const sortedCalendar = [...calendar].sort((a, b) => a.t - b.t).slice(0, 40)
  return (
    <div className="space-y-4 p-3">
      <section>
        <div className="label-xs mb-2">Events In Range</div>
        {sortedMarks.length === 0 ? (
          <div className="rounded-sm border border-line-soft p-3 text-[11.5px] text-faint">No event marks in this window.</div>
        ) : (
          <div className="space-y-1">
            {sortedMarks.map(mark => (
              <button
                key={mark.clusterId}
                onClick={() => onSelectCluster(mark.clusterId)}
                className="group w-full rounded-sm border border-line-soft px-2 py-2 text-left transition hover:border-line hover:bg-surface-2"
              >
                <div className="flex items-center gap-2">
                  <span className="truncate text-[11px] uppercase text-dim">{mark.eventType.replaceAll('_', ' ')}</span>
                  <span className="ml-auto text-[10.5px] tabular text-faint">{new Date(mark.t * 1000).toLocaleDateString()}</span>
                </div>
                <div className="mt-1 line-clamp-2 text-[11.5px] text-muted group-hover:text-fg">{mark.summary}</div>
                <div className="mt-1 text-[10.5px] tabular text-faint">imp {mark.importance.toFixed(2)} · {mark.memberCount} sources</div>
              </button>
            ))}
          </div>
        )}
      </section>
      <section>
        <div className="label-xs mb-2">Calendar</div>
        {sortedCalendar.length === 0 ? (
          <div className="rounded-sm border border-line-soft p-3 text-[11.5px] text-faint">No calendar marks in this window.</div>
        ) : (
          <div className="space-y-1">
            {sortedCalendar.map(entry => (
              <button
                key={entry.id}
                onClick={() => onSelectCalendar(entry)}
                className="w-full rounded-sm border border-line-soft px-2 py-2 text-left transition hover:border-line hover:bg-surface-2"
              >
                <div className="flex items-center gap-2">
                  <span className="truncate text-[11.5px] text-muted">{entry.label}</span>
                  <span className="ml-auto text-[10.5px] tabular text-faint">{new Date(entry.t * 1000).toLocaleDateString()}</span>
                </div>
                <div className="mt-1 text-[10.5px] uppercase text-faint">{entry.eventType.replaceAll('_', ' ')}</div>
              </button>
            ))}
          </div>
        )}
      </section>
    </div>
  )
}

function WatchlistPanel({
  items,
  current,
  onSelect,
}: {
  items: WatchedSymbol[]
  current: string | null
  onSelect: (symbol: string) => void
}) {
  const [query, setQuery] = useState('')
  const filtered = items
    .filter(item => {
      const q = query.trim().toLowerCase()
      if (!q) return true
      return item.symbol.toLowerCase().includes(q) || item.name.toLowerCase().includes(q)
    })
    .sort((a, b) => a.symbol.localeCompare(b.symbol))

  return (
    <div className="flex h-full flex-col">
      <div className="border-b border-line-soft p-3">
        <div className="label-xs mb-2">Watchlist</div>
        <div className="flex h-8 items-center gap-2 rounded-sm border border-line bg-surface px-2">
          <Search className="size-3 text-dim" />
          <input
            value={query}
            onChange={e => setQuery(e.target.value)}
            placeholder="Filter watchlist"
            className="min-w-0 flex-1 bg-transparent text-[12px] text-fg outline-none placeholder:text-faint"
          />
        </div>
      </div>
      <div className="min-h-0 flex-1 overflow-y-auto p-2">
        {items.length === 0 ? (
          <div className="p-4 text-center text-[11.5px] text-faint">No watched symbols yet. Search a ticker and star it.</div>
        ) : filtered.length === 0 ? (
          <div className="p-4 text-center text-[11.5px] text-faint">No watchlist matches.</div>
        ) : (
          <div className="space-y-1">
            {filtered.map(item => (
              <button
                key={item.id}
                onClick={() => onSelect(item.symbol)}
                className={cn(
                  'grid w-full grid-cols-[70px_1fr_auto] items-center gap-2 rounded-sm border px-2 py-2 text-left transition',
                  current === item.symbol ? 'border-accent/60 bg-accent-soft' : 'border-line-soft hover:border-line hover:bg-surface-2',
                )}
              >
                <span className="font-medium tabular text-[12px] text-fg">{item.symbol}</span>
                <span className="min-w-0 truncate text-[11px] text-dim">{item.name}</span>
                <span className="text-right">
                  <span className="block text-[11.5px] tabular text-muted">{formatPrice(item.last)}</span>
                  <span className={cn('block text-[10.5px] tabular', pctClass(item.changePercent))}>{formatPct(item.changePercent)}</span>
                </span>
              </button>
            ))}
          </div>
        )}
      </div>
    </div>
  )
}

function MarkerStackPanel({
  marks,
  onSelectCluster,
}: {
  marks: ChartMark[]
  onSelectCluster: (clusterId: string) => void
}) {
  return (
    <div className="p-3">
      <div className="label-xs mb-2">Stacked Events</div>
      <div className="space-y-1">
        {marks.map(mark => (
          <button
            key={mark.clusterId}
            onClick={() => onSelectCluster(mark.clusterId)}
            className="flex w-full items-start gap-2 rounded-sm border border-line-soft px-2 py-2 text-left transition hover:border-line hover:bg-surface-2"
          >
            <div className="min-w-0 flex-1">
              <div className="text-[11px] uppercase text-dim">{mark.eventType.replaceAll('_', ' ')}</div>
              <div className="mt-1 line-clamp-3 text-[11.5px] text-muted">{mark.summary}</div>
            </div>
            <ChevronRight className="mt-1 size-3 text-faint" />
          </button>
        ))}
      </div>
    </div>
  )
}

function CalendarDetailPanel({ entry }: { entry: CalendarEntry }) {
  return (
    <div className="p-4">
      <div className="label-xs mb-2">Calendar Event</div>
      <div className="rounded-sm border border-line-soft p-3">
        <div className="text-[13px] font-medium text-fg">{entry.label}</div>
        <div className="mt-1 text-[11px] uppercase text-dim">{entry.eventType.replaceAll('_', ' ')}</div>
        <div className="mt-2 text-[11.5px] tabular text-muted">{new Date(entry.t * 1000).toLocaleString()}</div>
        {entry.notes && <div className="mt-2 text-[11.5px] text-muted">{entry.notes}</div>}
      </div>
    </div>
  )
}

export function ChartView() {
  const [symbol, setSymbol] = useState<string | null>(() => {
    const querySymbol = new URLSearchParams(window.location.search).get('symbol')?.trim().toUpperCase()
    return querySymbol || null
  })
  const [interval, setInterval] = useState<Interval>('1d')
  const [settings, setSettings] = useState<ChartSettings>(() => loadSettings())
  const [selectedCluster, setSelectedCluster] = useState<string | null>(null)
  const [selectedStack, setSelectedStack] = useState<ChartMark[] | null>(null)
  const [selectedCalendar, setSelectedCalendar] = useState<CalendarEntry | null>(null)
  const [rightPanel, setRightPanel] = useState<'watchlist' | 'events'>('watchlist')
  const [rightPanelOpen, setRightPanelOpen] = useState(true)
  const [refreshStamp, setRefreshStamp] = useState(0)
  const [defaultSymbols, setDefaultSymbols] = useState<SymbolSearchResult[]>([])
  const [, setLatestBar] = useState(0)
  const [watchedSet, setWatchedSet] = useState<Set<string>>(new Set())
  const [watchlistItems, setWatchlistItems] = useState<WatchedSymbol[]>([])
  const [visibleMarks, setVisibleMarks] = useState<ChartMark[]>([])
  const [visibleCalendar, setVisibleCalendar] = useState<CalendarEntry[]>([])
  const [watchlistVersion, setWatchlistVersion] = useState(0)
  const [watchPending, setWatchPending] = useState(false)

  useEffect(() => {
    const syncSymbolFromUrl = () => {
      if (window.location.pathname.replace(/\/+$/, '') !== '/chart') return
      const querySymbol = new URLSearchParams(window.location.search).get('symbol')?.trim().toUpperCase()
      if (querySymbol) setSymbol(querySymbol)
    }
    syncSymbolFromUrl()
    window.addEventListener('popstate', syncSymbolFromUrl)
    return () => window.removeEventListener('popstate', syncSymbolFromUrl)
  }, [])

  useEffect(() => {
    window.localStorage.setItem('marketlens.chart.settings', JSON.stringify(settings))
  }, [settings])

  useEffect(() => {
    const ac = new AbortController()
    fetchWatchlist(ac.signal)
      .then(items => {
        setWatchlistItems(items)
        setWatchedSet(new Set(items.map(i => i.symbol)))
      })
      .catch(() => {})
    return () => ac.abort()
  }, [watchlistVersion])

  useEffect(() => {
    const ac = new AbortController()
    searchChartSymbols('', ac.signal)
      .then(r => {
        setDefaultSymbols(r)
        if (!symbol && r.length > 0) setSymbol(r[0].symbol)
      })
      .catch(() => {})
    return () => ac.abort()
  }, [watchlistVersion, symbol])

  const isWatched = !!symbol && watchedSet.has(symbol)
  const selectSymbol = useCallback((next: string) => {
    const normalized = next.trim().toUpperCase()
    setSymbol(normalized)
    const url = new URL(window.location.href)
    url.pathname = '/chart'
    url.searchParams.set('symbol', normalized)
    window.history.replaceState(null, '', `${url.pathname}${url.search}`)
    setSelectedCluster(null)
    setSelectedStack(null)
    setSelectedCalendar(null)
    setRightPanel('watchlist')
  }, [])

  const selectCluster = useCallback((clusterId: string) => {
    setSelectedCluster(clusterId)
    setSelectedStack(null)
    setSelectedCalendar(null)
    setRightPanelOpen(true)
  }, [])

  const selectCalendar = useCallback((entry: CalendarEntry) => {
    if (entry.clusterId) {
      selectCluster(entry.clusterId)
      return
    }
    setSelectedCalendar(entry)
    setSelectedCluster(null)
    setSelectedStack(null)
    setRightPanelOpen(true)
  }, [selectCluster])

  const selectEventStack = useCallback((marks: ChartMark[]) => {
    setSelectedStack(marks)
    setSelectedCluster(null)
    setSelectedCalendar(null)
    setRightPanelOpen(true)
  }, [])

  const handleEventsLoaded = useCallback((marks: ChartMark[], calendar: CalendarEntry[]) => {
    setVisibleMarks(marks)
    setVisibleCalendar(calendar)
  }, [])

  const handleToggleWatch = async () => {
    if (!symbol || watchPending) return
    setWatchPending(true)
    try {
      if (isWatched) await unwatchSymbol(symbol)
      else await watchSymbol(symbol)
      setWatchlistVersion(v => v + 1)
    } catch {
      // surface in next request
    } finally {
      setWatchPending(false)
    }
  }

  const [coverageNote, setCoverageNote] = useState<string | null>(null)
  useEffect(() => {
    if (!symbol) { setCoverageNote(null); return }
    const ac = new AbortController()
    fetchChartSymbolInfo(symbol, ac.signal)
      .then(info => {
        const dailyCov = info.coverage.find(c => c.interval === '1d')
        if (!dailyCov) {
          setCoverageNote('No price coverage yet')
        } else {
          const earliest = new Date(dailyCov.earliest).toLocaleDateString()
          const latest = new Date(dailyCov.latest).toLocaleDateString()
          setCoverageNote(`${dailyCov.count} daily bars · ${earliest} → ${latest}`)
        }
      })
      .catch(() => setCoverageNote(null))
    return () => ac.abort()
  }, [symbol, refreshStamp])

  const canvas = useMemo(() => symbol
    ? <ChartCanvas
        key={`${symbol}-${interval}-${refreshStamp}`}
        symbol={symbol}
        interval={interval}
        chartType={settings.chartType}
        scaleMode={settings.scaleMode}
        indicators={settings.indicators}
        showVolume={settings.showVolume}
        showEvents={settings.showEvents}
        showCalendar={settings.showCalendar}
        onSelectCluster={selectCluster}
        onSelectEventStack={selectEventStack}
        onSelectCalendar={selectCalendar}
        onEventsLoaded={handleEventsLoaded}
        onLatestBar={setLatestBar}
      />
    : null,
    [symbol, interval, settings, refreshStamp, selectCluster, selectEventStack, selectCalendar, handleEventsLoaded],
  )

  const isMobile = useIsMobile()

  return (
    <div className="flex h-full flex-col">
      <div className="flex flex-wrap items-center gap-2 border-b border-line-soft bg-canvas/80 px-2 sm:px-4 py-2">
        <SymbolSearch
          current={symbol}
          onSelect={selectSymbol}
          onAdHoc={selectSymbol}
          watchlistVersion={watchlistVersion}
        />
        <WatchToggle symbol={symbol} watched={isWatched} onChange={handleToggleWatch} />
        <IntervalBar value={interval} onChange={setInterval} />
        <IndicatorMenu
          active={settings.indicators}
          onToggle={key => setSettings(s => ({ ...s, indicators: { ...s.indicators, [key]: !s.indicators[key] } }))}
        />
        <OverlayToggles
          showEvents={settings.showEvents}
          showCalendar={settings.showCalendar}
          onToggleEvents={() => setSettings(s => ({ ...s, showEvents: !s.showEvents }))}
          onToggleCalendar={() => setSettings(s => ({ ...s, showCalendar: !s.showCalendar }))}
        />
        <ChartSettingsButton settings={settings} onChange={setSettings} />
        <button
          onClick={() => setRightPanelOpen(open => !open)}
          className="hidden md:inline-flex h-7 items-center justify-center rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
          title={rightPanelOpen ? 'Hide watchlist panel' : 'Show watchlist panel'}
          aria-label={rightPanelOpen ? 'Hide watchlist panel' : 'Show watchlist panel'}
        >
          {rightPanelOpen ? <PanelRightClose className="size-3" /> : <PanelRightOpen className="size-3" />}
        </button>
        {coverageNote && (
          <span className="hidden lg:inline ml-3 text-[10.5px] tabular text-faint">{coverageNote}</span>
        )}
        <button
          onClick={() => setRefreshStamp(s => s + 1)}
          className="ml-auto inline-flex h-7 items-center gap-1.5 rounded-sm border border-line bg-canvas px-2 text-[11px] text-muted transition hover:bg-surface-2 hover:text-fg"
        >
          <RefreshCw className="size-3" />
          <span className="hidden sm:inline">Refresh</span>
        </button>
      </div>

      <div
        className="grid min-h-0 flex-1 grid-cols-1"
        style={!isMobile && rightPanelOpen ? { gridTemplateColumns: 'minmax(0,1fr) 360px' } : undefined}
      >
        <main className="min-h-0 overflow-hidden">
          {symbol
            ? canvas
            : (
              <div className="flex h-full items-center justify-center px-4 text-center text-[12px] text-faint">
                Pick a symbol to start.{defaultSymbols.length === 0 && ' (No symbols indexed yet. Let the pipeline ingest some articles first.)'}
              </div>
            )}
        </main>
        {rightPanelOpen && (
          <aside className="hidden min-h-0 overflow-hidden border-l border-line-soft bg-canvas md:block">
            <div className="flex h-full flex-col">
              <div className="flex h-10 shrink-0 items-center gap-2 border-b border-line-soft px-2">
                {selectedCluster ? (
                  <span className="px-1 text-[10px] tracking-[0.1em] uppercase text-dim">Cluster</span>
                ) : selectedStack ? (
                  <span className="px-1 text-[10px] tracking-[0.1em] uppercase text-dim">Events</span>
                ) : selectedCalendar ? (
                  <span className="px-1 text-[10px] tracking-[0.1em] uppercase text-dim">Calendar</span>
                ) : (
                  <div className="flex min-w-0 flex-1">
                    <button
                      onClick={() => setRightPanel('watchlist')}
                      className={cn(
                        'flex h-7 flex-1 items-center justify-center gap-1.5 rounded-sm text-[11.5px] transition',
                        rightPanel === 'watchlist' ? 'bg-surface-2 text-fg' : 'text-dim hover:bg-surface hover:text-fg',
                      )}
                    >
                      <Star className="size-3" />
                      Watchlist
                    </button>
                    <button
                      onClick={() => setRightPanel('events')}
                      className={cn(
                        'flex h-7 flex-1 items-center justify-center gap-1.5 rounded-sm text-[11.5px] transition',
                        rightPanel === 'events' ? 'bg-surface-2 text-fg' : 'text-dim hover:bg-surface hover:text-fg',
                      )}
                    >
                      <List className="size-3" />
                      Events
                    </button>
                  </div>
                )}
                <button
                  onClick={() => setRightPanelOpen(false)}
                  className="ml-auto inline-flex size-6 items-center justify-center rounded-sm text-dim transition hover:bg-surface-2 hover:text-fg"
                  title="Hide watchlist panel"
                  aria-label="Hide watchlist panel"
                >
                  <PanelRightClose className="size-3.5" />
                </button>
              </div>
              <div className="min-h-0 flex-1 overflow-y-auto overflow-x-hidden">
                {selectedCluster ? (
                  <ClusterDrill clusterId={selectedCluster} />
                ) : selectedStack ? (
                  <MarkerStackPanel marks={selectedStack} onSelectCluster={selectCluster} />
                ) : selectedCalendar ? (
                  <CalendarDetailPanel entry={selectedCalendar} />
                ) : rightPanel === 'watchlist' ? (
                  <WatchlistPanel items={watchlistItems} current={symbol} onSelect={selectSymbol} />
                ) : (
                  <EventsPanel marks={visibleMarks} calendar={visibleCalendar} onSelectCluster={selectCluster} onSelectCalendar={selectCalendar} />
                )}
              </div>
            </div>
          </aside>
        )}
      </div>

      {isMobile && (
        <Sheet
          open={!!selectedCluster || !!selectedStack || !!selectedCalendar}
          onOpenChange={open => {
            if (!open) {
              setSelectedCluster(null)
              setSelectedStack(null)
              setSelectedCalendar(null)
            }
          }}
          width={420}
        >
          <div className="flex h-full flex-col">
            <header className="sticky top-0 z-10 flex items-center gap-2 border-b border-line bg-surface px-4 py-3">
              <h3 className="text-[12px] font-medium">{selectedCluster ? 'Cluster' : selectedStack ? 'Events' : 'Calendar'}</h3>
              <SheetClose className="ml-auto rounded-sm p-1 text-dim hover:bg-surface-2 hover:text-fg transition">
                <X className="size-3.5" />
              </SheetClose>
            </header>
            <div className="flex-1 overflow-y-auto">
              {selectedCluster && <ClusterDrill clusterId={selectedCluster} />}
              {selectedStack && <MarkerStackPanel marks={selectedStack} onSelectCluster={selectCluster} />}
              {selectedCalendar && <CalendarDetailPanel entry={selectedCalendar} />}
            </div>
          </div>
        </Sheet>
      )}
    </div>
  )
}
