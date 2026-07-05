export function fmtImportance(v: number): string {
  return v.toFixed(2)
}

export function fmtPct(v: number, digits = 0): string {
  return `${(v * 100).toFixed(digits)}%`
}

export function fmtSigned(v: number, digits = 2): string {
  if (Math.abs(v) < 5e-3) return (0).toFixed(digits)
  const sign = v >= 0 ? '+' : '−'
  return `${sign}${Math.abs(v).toFixed(digits)}`
}

export function fmtAge(iso: string, now: Date = new Date()): string {
  const t = new Date(iso).getTime()
  const ms = now.getTime() - t
  if (Number.isNaN(ms) || ms < 0) return '—'
  const min = Math.floor(ms / 60_000)
  if (min < 1)   return 'now'
  if (min < 60)  return `${min}m`
  const h = Math.floor(min / 60)
  if (h < 24)    return `${h}h`
  const d = Math.floor(h / 24)
  if (d < 7)     return `${d}d`
  const w = Math.floor(d / 7)
  return `${w}w`
}

export function fmtCount(n: number): string {
  if (n < 1_000)        return n.toString()
  if (n < 1_000_000)    return `${(n / 1_000).toFixed(n < 10_000 ? 1 : 0)}k`
  return `${(n / 1_000_000).toFixed(1)}M`
}

const EVENT_LABELS: Record<string, string> = {
  earnings: 'Earnings',
  acquisition_disposition: 'M&A',
  material_agreement: 'Agreement',
  material_impairment: 'Impairment',
  delisting: 'Delisting',
  restatement: 'Restatement',
  officer_change: 'Officer',
  vote_result: 'Vote',
  regulation_fd_disclosure: 'Reg FD',
  analyst_action: 'Analyst',
  product_launch: 'Launch',
  litigation: 'Litigation',
  regulatory_action: 'Regulatory',
  macro_release: 'Macro',
  other_material_event: 'Other',
}

export function eventLabel(t: string): string {
  return EVENT_LABELS[t] ?? t.replaceAll('_', ' ')
}

export const EVENT_TYPES = Object.keys(EVENT_LABELS)

const SOURCE_LABELS: Record<string, string> = {
  edgar: 'EDGAR',
  business_wire: 'BusinessWire',
  globe_newswire: 'GlobeNewswire',
  pr_newswire: 'PRNewswire',
  ir_feed: 'IR Feed',
  fred: 'FRED',
  census: 'Census',
  finnhub: 'Finnhub',
  mining_com: 'MINING.COM',
  fed_speeches: 'Fed Speeches',
  fed_press: 'Fed Press',
  bls: 'BLS',
  bea: 'BEA',
  courtlistener: 'CourtListener',
  sec_enforcement: 'SEC Enforcement',
  ftc: 'FTC',
  doj_antitrust: 'DOJ Antitrust',
  transcript: 'Transcript',
  earnings_call: 'Earnings Call',
  bis: 'BIS',
  industry_analyst: 'Industry Analyst',
  reddit: 'Reddit',
  tech_press: 'Tech Press',
  cnbc: 'CNBC',
  nbc_news: 'NBC News',
  cnn: 'CNN',
  cbs_news: 'CBS News',
  fox_business: 'Fox Business',
  seeking_alpha: 'Seeking Alpha',
  npr: 'NPR',
  pew_research: 'Pew Research',
  white_house: 'White House',
  crypto_press: 'Crypto Press',
  ai_analyst: 'AI Analyst',
  bbc: 'BBC',
  upi: 'UPI',
  eia: 'EIA',
  usgs: 'USGS',
  doe_nuclear: 'DOE Nuclear',
  nuclear_press: 'Nuclear Press',
  ev_press: 'EV Press',
}

export function sourceLabel(s: string): string {
  return SOURCE_LABELS[s] ?? s
}
