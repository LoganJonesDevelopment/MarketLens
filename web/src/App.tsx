import { useEffect, useState } from 'react'
import { Activity, BookOpen, CalendarDays, CandlestickChart, Crosshair, Gauge, Layers, Sunrise } from 'lucide-react'
import { TooltipProvider } from './components/Tooltip'
import { TabsRoot, TabsList, TabsTrigger, TabsContent } from './components/Tabs'
import { OpenView } from './views/Open/OpenView'
import { IdeasView } from './views/Ideas/IdeasView'
import { StackView } from './views/Stack/StackView'
import { PipelineView } from './views/Pipeline/PipelineView'
import { ResearchView } from './views/Research/ResearchView'
import { CalendarView } from './views/Calendar/CalendarView'
import { ChartView } from './views/Chart/ChartView'
import { ThesesView } from './views/Theses/ThesesView'
import { SourcesView } from './views/Sources/SourcesView'

type AppTab = 'open' | 'ideas' | 'stack' | 'theses' | 'calendar' | 'research' | 'chart' | 'system'

const DEFAULT_TAB: AppTab = 'open'

const TAB_ROUTES: Record<AppTab, string> = {
  open: '/open',
  ideas: '/ideas',
  stack: '/stack',
  theses: '/theses',
  calendar: '/calendar',
  research: '/research',
  chart: '/chart',
  system: '/system',
}

function tabFromPath(pathname: string): AppTab {
  const path = pathname.replace(/\/+$/, '') || '/'
  if (path === '/' || path === '/home') return DEFAULT_TAB
  if (path === '/open') return 'open'
  if (path === '/ideas') return 'ideas'
  if (path === '/inbox') return 'ideas'
  if (path === '/stack') return 'stack'
  if (path === '/theses') return 'theses'
  if (path === '/calendar') return 'calendar'
  if (path === '/research' || path.startsWith('/research/')) return 'research'
  if (path === '/chart') return 'chart'
  if (path === '/pipeline' || path === '/sources' || path === '/system') return 'system'
  return DEFAULT_TAB
}

function pathForTab(tab: string): string {
  return TAB_ROUTES[tab as AppTab] ?? TAB_ROUTES[DEFAULT_TAB]
}

function useRoutedTab(): [AppTab, (tab: string) => void] {
  const [tab, setTab] = useState<AppTab>(() => tabFromPath(window.location.pathname))

  useEffect(() => {
    if (window.location.pathname === '/' || window.location.pathname === '/home') {
      window.history.replaceState(null, '', TAB_ROUTES[DEFAULT_TAB])
    }

    const onPopState = () => setTab(tabFromPath(window.location.pathname))
    window.addEventListener('popstate', onPopState)
    return () => window.removeEventListener('popstate', onPopState)
  }, [])

  const navigate = (nextTab: string) => {
    const next = tabFromPath(pathForTab(nextTab))
    const nextPath = pathForTab(next)
    setTab(next)
    if (window.location.pathname !== nextPath) {
      window.history.pushState(null, '', nextPath)
    }
  }

  return [tab, navigate]
}

const [SYSTEM_PIPELINE, SYSTEM_SOURCES] = ['pipeline', 'sources'] as const

function SystemView() {
  const [mode, setMode] = useState<'pipeline' | 'sources'>('pipeline')
  return (
    <div className="flex h-full flex-col">
      <div className="flex min-h-10 items-center gap-1 border-b border-line-soft bg-canvas px-3 py-1.5">
        <button
          type="button"
          onClick={() => setMode(SYSTEM_PIPELINE)}
          className={`h-7 rounded-[2px] px-2.5 text-[11px] text-muted transition hover:text-fg ${mode === 'pipeline' ? 'bg-accent-soft text-accent' : ''}`}
        >
          Pipeline
        </button>
        <button
          type="button"
          onClick={() => setMode(SYSTEM_SOURCES)}
          className={`h-7 rounded-[2px] px-2.5 text-[11px] text-muted transition hover:text-fg ${mode === 'sources' ? 'bg-accent-soft text-accent' : ''}`}
        >
          Sources
        </button>
      </div>
      <div className="min-h-0 flex-1">
        {mode === 'pipeline' ? <PipelineView /> : <SourcesView />}
      </div>
    </div>
  )
}

export function App() {
  const [tab, setTab] = useRoutedTab()

  return (
    <TooltipProvider delayDuration={200} skipDelayDuration={300}>
      <TabsRoot value={tab} onValueChange={setTab} className="flex h-full flex-col">
        <header className="flex min-h-12 items-center gap-1 border-b border-line bg-canvas px-3 sm:gap-4 sm:px-5">
          <TabsList className="min-w-0 flex-1 overflow-x-auto sm:flex-none">
            <TabsTrigger value="open">
              <Sunrise className="size-4" />
              <span className="hidden sm:inline">Open</span>
            </TabsTrigger>
            <TabsTrigger value="ideas">
              <Gauge className="size-4" />
              <span className="hidden sm:inline">Ideas</span>
            </TabsTrigger>
            <TabsTrigger value="stack">
              <Layers className="size-4" />
              <span className="hidden sm:inline">Stack</span>
            </TabsTrigger>
            <TabsTrigger value="theses">
              <Crosshair className="size-4" />
              <span className="hidden sm:inline">Theses</span>
            </TabsTrigger>
            <TabsTrigger value="research">
              <BookOpen className="size-4" />
              <span className="hidden sm:inline">Research</span>
            </TabsTrigger>
            <TabsTrigger value="chart">
              <CandlestickChart className="size-4" />
              <span className="hidden sm:inline">Chart</span>
            </TabsTrigger>
            <TabsTrigger value="calendar">
              <CalendarDays className="size-4" />
              <span className="hidden sm:inline">Calendar</span>
            </TabsTrigger>
            <TabsTrigger value="system">
              <Activity className="size-4" />
              <span className="hidden sm:inline">System</span>
            </TabsTrigger>
          </TabsList>
        </header>
        <TabsContent value="open" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <OpenView />
        </TabsContent>
        <TabsContent value="ideas" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <IdeasView />
        </TabsContent>
        <TabsContent value="stack" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <StackView />
        </TabsContent>
        <TabsContent value="theses" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <ThesesView />
        </TabsContent>
        <TabsContent value="research" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <ResearchView />
        </TabsContent>
        <TabsContent value="chart" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <ChartView />
        </TabsContent>
        <TabsContent value="calendar" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <CalendarView />
        </TabsContent>
        <TabsContent value="system" className="min-h-0 flex-1 outline-none data-[state=inactive]:hidden">
          <SystemView />
        </TabsContent>
      </TabsRoot>
    </TooltipProvider>
  )
}
