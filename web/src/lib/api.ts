export type EventDto = {
  clusterId: string
  eventType: string
  symbol: string | null
  summary: string
  sentiment: number
  importance: number
  components: {
    sourceWeight: number
    noveltyWeight: number
    eventClassPrior: number
    magnitudeSignal: number
  }
  slots: string
  cluster: {
    memberCount: number
    dominantSourceTier: string
    firstSeenAt: string
    lastSeenAt: string
    triageConfidence: number | null
  }
  members: Array<{
    source: string
    sourceTier: string
    publisher: string | null
    headline: string
    url: string | null
    publishedAt: string
  }>
  model: { modelName: string; promptVersion: string; extractedAt: string }
}

export type StatsDto = {
  articles: number
  clusters: number
  events: number
  triaged: number
  pendingExtraction: number
  clusterCompressionRatio: number
  byEventType: Array<{ eventType: string; count: number; avgImportance: number; avgSentiment: number }>
  bySource: Array<{ source: string; count: number }>
}

export type PendingClusterDto = {
  id: string
  symbol: string | null
  memberCount: number
  dominantSourceTier: string
  triageEventType: string | null
  triageConfidence: number | null
  firstSeenAt: string
  lastSeenAt: string
}

export type ThesisStatus =
  | 'exploration'
  | 'draft'
  | 'active'
  | 'paused'
  | 'watching'
  | 'validated'
  | 'invalidated'
  | 'archived'
  | (string & {})

export type ThesisEvidenceReviewStatus =
  | 'pending'
  | 'accepted'
  | 'rejected'
  | 'needs_review'
  | (string & {})

export type ThesisEvidenceStance =
  | 'supports'
  | 'contradicts'
  | 'neutral'
  | 'unknown'
  | (string & {})

export type ThesisDto = {
  id: string
  symbol: string | null
  title: string
  statement?: string | null
  summary?: string | null
  status?: ThesisStatus | null
  confidence?: number | null
  conviction?: number | null
  evidenceCount?: number | null
  pendingEvidenceCount?: number | null
  supportedCount?: number | null
  contradictedCount?: number | null
  createdAt?: string | null
  updatedAt?: string | null
  lastEvidenceAt?: string | null
  planStatus?: 'pending' | 'ready' | (string & {}) | null
  planGeneratedAt?: string | null
  tags?: string[] | null
}

export type ThesisLeaning = 'supports' | 'contradicts' | 'mixed' | 'insufficient' | (string & {})
export type ThesisCoverage = 'thick' | 'moderate' | 'thin' | (string & {})

export type ThesisPlanDto = {
  summary: string
  trackedEntities: TrackedEntityDto[]
  subTracks: ThesisSubTrackDto[]
  confirmingSignals: string[]
  refutingSignals: string[]
  corpusContextSize: number
  verdict: string
  leaning: ThesisLeaning
  coverage: ThesisCoverage
  strongestSupportClusterIds: string[]
  strongestContradictClusterIds: string[]
}

export type TrackedEntityDto = {
  name: string
  symbol: string | null
  rationale: string
}

export type ThesisSubTrackDto = {
  name: string
  question: string
  expectedDirection: 'confirms_if' | 'contradicts_if' | 'neutral_lever' | (string & {})
  assetTerms: string[]
  conceptTerms: string[]
  eventTypes: string[]
  excludeTerms: string[]
}

export type ThesisEvidenceDto = {
  id: string
  evidenceKind?: 'cluster' | 'article' | 'segment' | 'chunk' | (string & {}) | null
  thesisId?: string | null
  eventId?: string | null
  clusterId?: string | null
  symbol?: string | null
  eventType?: string | null
  stance?: ThesisEvidenceStance | null
  stanceConfidence?: number | null
  stanceRationale?: string | null
  classifiedAt?: string | null
  reviewStatus?: ThesisEvidenceReviewStatus | null
  matchKind?: string | null
  matchReason?: string | null
  similarity?: number | null
  confidence?: number | null
  importance?: number | null
  sentiment?: number | null
  source?: string | null
  sourceTier?: string | null
  publisher?: string | null
  headline?: string | null
  summary?: string | null
  url?: string | null
  publishedAt?: string | null
  observedAt?: string | null
  createdAt?: string | null
  reviewedAt?: string | null
  reviewerNote?: string | null
  isPinned?: boolean | null
  transcriptId?: string | null
  segmentIndex?: number | null
  startSeconds?: number | null
  endSeconds?: number | null
  speaker?: string | null
  segmentText?: string | null
  audioUrl?: string | null
  callDate?: string | null
  chunkId?: string | null
  chunkArticleId?: string | null
  chunkIndex?: number | null
  chunkSection?: string | null
  chunkText?: string | null
  chunkFilingForm?: string | null
  chunkFilingUrl?: string | null
  chunkFilingSymbol?: string | null
  chunkFilingHeadline?: string | null
  chunkFilingPublishedAt?: string | null
}

export type ThesisAssetLinkDto = {
  assetId: string
  role: string
  kind: string | null
  name: string | null
  symbol: string | null
  keywords: string[]
}

export type ThesisDetailDto = ThesisDto & {
  evidence?: ThesisEvidenceDto[] | null
  relatedEvents?: EventDto[] | null
  assets?: ThesisAssetLinkDto[]
  rules?: ThesisRuleDto[]
  plan?: ThesisPlanDto | null
  planModel?: string | null
  planPromptVersion?: string | null
  planGeneratedAt?: string | null
}

export type EventQuery = {
  symbol?: string
  eventType?: string
  minImportance?: number
  take?: number
  from?: Date
  to?: Date
}

export type ThesisQuery = {
  symbol?: string
  status?: string
  take?: number
}

export type CreateThesisCommand = {
  name: string
  thesisText: string
  status?: string
  summary?: string
  assetKeywords?: string[]
  conceptKeywords?: string[]
  eventTypes?: string[]
  sourceNames?: string[]
  sourceTiers?: string[]
  excludeTerms?: string[]
  minArticleSimilarity?: number
}

export type ThesisScanResult = {
  thesesScanned: number
  articlesScanned: number
  eventsScanned: number
  evidenceAdded: number
}

export type ReviewThesisEvidenceCommand = {
  reviewStatus: ThesisEvidenceReviewStatus
  stance?: ThesisEvidenceStance
  isPinned?: boolean
  reviewerNote?: string
}

async function requestJson<T>(url: string, init: RequestInit = {}, signal?: AbortSignal): Promise<T> {
  const headers = new Headers(init.headers)
  headers.set('Accept', 'application/json')
  if (init.body && !headers.has('Content-Type')) headers.set('Content-Type', 'application/json')
  const res = await fetch(url, { ...init, signal, headers })
  if (!res.ok) throw new Error(`${res.status} ${res.statusText}`)
  if (res.status === 204) return undefined as T
  return res.json() as Promise<T>
}

async function getJson<T>(url: string, signal?: AbortSignal): Promise<T> {
  return requestJson<T>(url, {}, signal)
}

function listFromPayload<T>(payload: unknown, keys: string[]): T[] {
  if (Array.isArray(payload)) return payload as T[]
  if (!payload || typeof payload !== 'object') return []
  const obj = payload as Record<string, unknown>
  for (const key of keys) {
    const value = obj[key]
    if (Array.isArray(value)) return value as T[]
  }
  return []
}

function objectFromPayload<T>(payload: unknown, keys: string[]): T {
  if (!payload || typeof payload !== 'object') return payload as T
  const obj = payload as Record<string, unknown>
  for (const key of keys) {
    const value = obj[key]
    if (value && typeof value === 'object' && !Array.isArray(value)) return value as T
  }
  return payload as T
}

function valueOf(obj: Record<string, unknown>, keys: string[]): unknown {
  for (const key of keys) {
    if (key in obj) return obj[key]
  }
  return undefined
}

function nestedObject(obj: Record<string, unknown>, keys: string[]): Record<string, unknown> | null {
  const value = valueOf(obj, keys)
  return value && typeof value === 'object' && !Array.isArray(value)
    ? value as Record<string, unknown>
    : null
}

function stringOf(obj: Record<string, unknown>, keys: string[]): string | null {
  const value = valueOf(obj, keys)
  return typeof value === 'string' && value.trim() ? value : null
}

function numberOf(obj: Record<string, unknown>, keys: string[]): number | null {
  const value = valueOf(obj, keys)
  return typeof value === 'number' ? value : null
}

function stringsOf(obj: Record<string, unknown>, keys: string[]): string[] | null {
  const value = valueOf(obj, keys)
  return Array.isArray(value) ? value.filter((v): v is string => typeof v === 'string') : null
}

function normalizeThesis(raw: unknown): ThesisDto {
  const obj = raw && typeof raw === 'object' ? raw as Record<string, unknown> : {}
  const assets = Array.isArray(obj.assets) ? obj.assets as unknown[] : []
  const primaryAsset = assets
    .map(a => a && typeof a === 'object' ? a as Record<string, unknown> : null)
    .find(a => a?.symbol)

  return {
    id: stringOf(obj, ['id', 'Id']) ?? '',
    symbol: stringOf(obj, ['symbol', 'Symbol']) ?? (primaryAsset ? stringOf(primaryAsset, ['symbol', 'Symbol']) : null),
    title: stringOf(obj, ['title', 'name', 'Name']) ?? 'Untitled thesis',
    statement: stringOf(obj, ['statement', 'thesisText', 'ThesisText']),
    summary: stringOf(obj, ['summary', 'Summary']),
    status: stringOf(obj, ['status', 'Status']),
    confidence: numberOf(obj, ['confidence', 'conviction']),
    conviction: numberOf(obj, ['conviction', 'confidence']),
    evidenceCount: numberOf(obj, ['evidenceCount', 'EvidenceCount']),
    pendingEvidenceCount: numberOf(obj, ['pendingEvidenceCount', 'PendingEvidenceCount']),
    supportedCount: numberOf(obj, ['supportedCount', 'SupportedCount']),
    contradictedCount: numberOf(obj, ['contradictedCount', 'ContradictedCount']),
    createdAt: stringOf(obj, ['createdAt', 'CreatedAt']),
    updatedAt: stringOf(obj, ['updatedAt', 'UpdatedAt']),
    lastEvidenceAt: stringOf(obj, ['lastEvidenceAt', 'LastEvidenceAt']),
    planStatus: stringOf(obj, ['planStatus', 'PlanStatus']),
    planGeneratedAt: stringOf(obj, ['planGeneratedAt', 'PlanGeneratedAt']),
    tags: stringsOf(obj, ['tags', 'Tags']),
  }
}

function normalizePlan(raw: unknown): ThesisPlanDto | null {
  if (raw == null) return null
  let parsed: unknown = raw
  if (typeof raw === 'string') {
    const trimmed = raw.trim()
    if (!trimmed) return null
    try { parsed = JSON.parse(trimmed) } catch { return null }
  }
  if (!parsed || typeof parsed !== 'object') return null
  const obj = parsed as Record<string, unknown>
  const entities = Array.isArray(obj.trackedEntities)
    ? (obj.trackedEntities as unknown[]).map(e => {
        const eo = e && typeof e === 'object' ? e as Record<string, unknown> : {}
        return {
          name: stringOf(eo, ['name', 'Name']) ?? '',
          symbol: stringOf(eo, ['symbol', 'Symbol']),
          rationale: stringOf(eo, ['rationale', 'Rationale']) ?? '',
        }
      })
    : []
  const subTracks = Array.isArray(obj.subTracks)
    ? (obj.subTracks as unknown[]).map(s => {
        const so = s && typeof s === 'object' ? s as Record<string, unknown> : {}
        return {
          name: stringOf(so, ['name', 'Name']) ?? '',
          question: stringOf(so, ['question', 'Question']) ?? '',
          expectedDirection: (stringOf(so, ['expectedDirection', 'ExpectedDirection']) ?? 'neutral_lever') as ThesisSubTrackDto['expectedDirection'],
          assetTerms: asStringArray(valueOf(so, ['assetTerms', 'AssetTerms'])),
          conceptTerms: asStringArray(valueOf(so, ['conceptTerms', 'ConceptTerms'])),
          eventTypes: asStringArray(valueOf(so, ['eventTypes', 'EventTypes'])),
          excludeTerms: asStringArray(valueOf(so, ['excludeTerms', 'ExcludeTerms'])),
        }
      })
    : []
  return {
    summary: stringOf(obj, ['summary', 'Summary']) ?? '',
    trackedEntities: entities,
    subTracks,
    confirmingSignals: asStringArray(valueOf(obj, ['confirmingSignals', 'ConfirmingSignals'])),
    refutingSignals: asStringArray(valueOf(obj, ['refutingSignals', 'RefutingSignals'])),
    corpusContextSize: numberOf(obj, ['corpusContextSize', 'CorpusContextSize']) ?? 0,
    verdict: stringOf(obj, ['verdict', 'Verdict']) ?? '',
    leaning: (stringOf(obj, ['leaning', 'Leaning']) ?? 'insufficient') as ThesisLeaning,
    coverage: (stringOf(obj, ['coverage', 'Coverage']) ?? 'thin') as ThesisCoverage,
    strongestSupportClusterIds: asStringArray(valueOf(obj, ['strongestSupportClusterIds', 'StrongestSupportClusterIds'])),
    strongestContradictClusterIds: asStringArray(valueOf(obj, ['strongestContradictClusterIds', 'StrongestContradictClusterIds'])),
  }
}

function normalizeEvidence(raw: unknown): ThesisEvidenceDto {
  const obj = raw && typeof raw === 'object' ? raw as Record<string, unknown> : {}
  const article = nestedObject(obj, ['article', 'Article'])
  const eventItem = nestedObject(obj, ['eventItem', 'EventItem', 'event', 'Event'])
  const segmentItem = nestedObject(obj, ['segmentItem', 'SegmentItem'])
  const chunkItem = nestedObject(obj, ['chunkItem', 'ChunkItem'])
  const sourceObj = article ?? obj
  const eventObj = eventItem ?? obj

  function extractFormFromPayload(rawPayload: unknown): string | null {
    if (typeof rawPayload !== 'string') return null
    try {
      const parsed = JSON.parse(rawPayload)
      return typeof parsed?.form === 'string' ? parsed.form : null
    } catch { return null }
  }

  return {
    id: stringOf(obj, ['id', 'Id']) ?? '',
    evidenceKind: stringOf(obj, ['evidenceKind', 'EvidenceKind']) as ThesisEvidenceDto['evidenceKind'],
    thesisId: stringOf(obj, ['thesisId', 'ThesisId']),
    eventId: stringOf(obj, ['eventId', 'EventId']),
    clusterId: stringOf(obj, ['clusterId', 'ClusterId']) ?? (eventItem ? stringOf(eventItem, ['clusterId', 'ClusterId']) : null),
    symbol: stringOf(sourceObj, ['symbol', 'Symbol']) ?? stringOf(eventObj, ['symbol', 'Symbol']) ?? (segmentItem ? stringOf(segmentItem, ['transcriptSymbol', 'TranscriptSymbol']) : null) ?? (chunkItem ? stringOf(chunkItem, ['filingSymbol', 'FilingSymbol']) : null),
    eventType: stringOf(eventObj, ['eventType', 'EventType']),
    stance: stringOf(obj, ['stance', 'Stance']),
    stanceConfidence: numberOf(obj, ['stanceConfidence', 'StanceConfidence']),
    stanceRationale: stringOf(obj, ['stanceRationale', 'StanceRationale']),
    classifiedAt: stringOf(obj, ['classifiedAt', 'ClassifiedAt']),
    reviewStatus: stringOf(obj, ['reviewStatus', 'ReviewStatus']),
    matchKind: stringOf(obj, ['matchKind', 'MatchKind']),
    matchReason: stringOf(obj, ['matchReason', 'MatchReason']),
    similarity: numberOf(obj, ['similarity', 'Similarity']),
    confidence: numberOf(obj, ['confidence', 'Confidence']),
    importance: numberOf(eventObj, ['importance', 'Importance']),
    sentiment: numberOf(eventObj, ['sentiment', 'Sentiment']),
    source: stringOf(sourceObj, ['source', 'Source']),
    sourceTier: stringOf(sourceObj, ['sourceTier', 'SourceTier']),
    publisher: stringOf(sourceObj, ['publisher', 'Publisher']),
    headline: stringOf(sourceObj, ['headline', 'Headline']),
    summary: stringOf(sourceObj, ['summary', 'Summary']) ?? stringOf(eventObj, ['summary', 'Summary']) ?? stringOf(obj, ['notes', 'Notes']),
    url: stringOf(sourceObj, ['url', 'Url']),
    publishedAt: stringOf(sourceObj, ['publishedAt', 'PublishedAt']) ?? stringOf(eventObj, ['lastSeenAt', 'LastSeenAt']),
    observedAt: stringOf(obj, ['observedAt', 'matchedAt', 'MatchedAt']),
    createdAt: stringOf(obj, ['createdAt', 'CreatedAt']),
    reviewedAt: stringOf(obj, ['reviewedAt', 'ReviewedAt']),
    reviewerNote: stringOf(obj, ['reviewerNote', 'notes', 'Notes']),
    isPinned: valueOf(obj, ['isPinned', 'IsPinned']) === true,
    transcriptId: segmentItem ? stringOf(segmentItem, ['transcriptId', 'TranscriptId']) : null,
    segmentIndex: segmentItem ? numberOf(segmentItem, ['segmentIndex', 'SegmentIndex']) : null,
    startSeconds: segmentItem ? numberOf(segmentItem, ['startSeconds', 'StartSeconds']) : null,
    endSeconds: segmentItem ? numberOf(segmentItem, ['endSeconds', 'EndSeconds']) : null,
    speaker: segmentItem ? stringOf(segmentItem, ['speaker', 'Speaker']) : null,
    segmentText: segmentItem ? stringOf(segmentItem, ['text', 'Text']) : null,
    audioUrl: segmentItem ? stringOf(segmentItem, ['audioUrl', 'AudioUrl']) : null,
    callDate: segmentItem ? stringOf(segmentItem, ['callDate', 'CallDate']) : null,
    chunkId: chunkItem ? stringOf(chunkItem, ['chunkId', 'ChunkId']) : null,
    chunkArticleId: chunkItem ? stringOf(chunkItem, ['articleId', 'ArticleId']) : null,
    chunkIndex: chunkItem ? numberOf(chunkItem, ['chunkIndex', 'ChunkIndex']) : null,
    chunkSection: chunkItem ? stringOf(chunkItem, ['section', 'Section']) : null,
    chunkText: chunkItem ? stringOf(chunkItem, ['text', 'Text']) : null,
    chunkFilingForm: chunkItem ? (stringOf(chunkItem, ['filingForm', 'FilingForm']) ?? extractFormFromPayload(chunkItem['filingRawPayload'] ?? chunkItem['FilingRawPayload'])) : null,
    chunkFilingUrl: chunkItem ? stringOf(chunkItem, ['filingUrl', 'FilingUrl']) : null,
    chunkFilingSymbol: chunkItem ? stringOf(chunkItem, ['filingSymbol', 'FilingSymbol']) : null,
    chunkFilingHeadline: chunkItem ? stringOf(chunkItem, ['filingHeadline', 'FilingHeadline']) : null,
    chunkFilingPublishedAt: chunkItem ? stringOf(chunkItem, ['filingPublishedAt', 'FilingPublishedAt']) : null,
  }
}

export function fetchEvents(q: EventQuery = {}, signal?: AbortSignal): Promise<EventDto[]> {
  const p = new URLSearchParams()
  if (q.symbol)        p.set('symbol', q.symbol)
  if (q.eventType)     p.set('eventType', q.eventType)
  if (q.minImportance != null) p.set('minImportance', q.minImportance.toString())
  if (q.take != null)  p.set('take', q.take.toString())
  if (q.from)          p.set('from', q.from.toISOString())
  if (q.to)            p.set('to', q.to.toISOString())
  const qs = p.toString()
  return getJson<EventDto[]>(`/api/events${qs ? `?${qs}` : ''}`, signal)
}

export function fetchStats(signal?: AbortSignal): Promise<StatsDto> {
  return getJson<StatsDto>('/api/stats', signal)
}

export function fetchPending(signal?: AbortSignal): Promise<PendingClusterDto[]> {
  return getJson<PendingClusterDto[]>('/api/clusters/pending', signal)
}

export async function fetchTheses(q: ThesisQuery = {}, signal?: AbortSignal): Promise<ThesisDto[]> {
  const p = new URLSearchParams()
  if (q.symbol) p.set('symbol', q.symbol)
  if (q.status) p.set('status', q.status)
  if (q.take != null) p.set('take', q.take.toString())
  const qs = p.toString()
  const payload = await getJson<unknown>(`/api/research/theses${qs ? `?${qs}` : ''}`, signal)
  return listFromPayload<unknown>(payload, ['items', 'data', 'results', 'theses']).map(normalizeThesis)
}

function normalizeAssetLink(raw: unknown): ThesisAssetLinkDto {
  const obj = raw && typeof raw === 'object' ? raw as Record<string, unknown> : {}
  return {
    assetId: stringOf(obj, ['assetId', 'AssetId']) ?? '',
    role: stringOf(obj, ['role', 'Role']) ?? 'subject',
    kind: stringOf(obj, ['kind', 'Kind']),
    name: stringOf(obj, ['name', 'Name']),
    symbol: stringOf(obj, ['symbol', 'Symbol']),
    keywords: asStringArray(valueOf(obj, ['keywords', 'Keywords'])),
  }
}

export async function fetchThesis(id: string, signal?: AbortSignal): Promise<ThesisDetailDto> {
  const payload = await getJson<unknown>(`/api/research/theses/${encodeURIComponent(id)}`, signal)
  const detail = objectFromPayload<unknown>(payload, ['item', 'data', 'result', 'thesis'])
  const base = normalizeThesis(detail)
  const obj = detail && typeof detail === 'object' ? detail as Record<string, unknown> : {}
  const assetsRaw = valueOf(obj, ['assets', 'Assets'])
  const rulesRaw = valueOf(obj, ['rules', 'Rules'])
  return {
    ...base,
    assets: Array.isArray(assetsRaw) ? assetsRaw.map(normalizeAssetLink) : undefined,
    rules: Array.isArray(rulesRaw) ? rulesRaw.map(normalizeRule) : undefined,
    plan: normalizePlan(valueOf(obj, ['plan', 'Plan'])),
    planModel: stringOf(obj, ['planModel', 'PlanModel']),
    planPromptVersion: stringOf(obj, ['planPromptVersion', 'PlanPromptVersion']),
    planGeneratedAt: stringOf(obj, ['planGeneratedAt', 'PlanGeneratedAt']),
  }
}

export type BootstrapThesisResult = {
  thesisId: string
  planGenerated: boolean
  corpusContextSize: number
  subTracksCreated: number
  error: string | null
}

export function bootstrapThesis(id: string, signal?: AbortSignal): Promise<BootstrapThesisResult> {
  return requestJson<BootstrapThesisResult>(
    `/api/research/theses/${encodeURIComponent(id)}/bootstrap`,
    { method: 'POST' },
    signal,
  )
}

export type CreateExplorationCommand = {
  thesisText: string
  name?: string
}

export type ExplorationResult = {
  explorationId: string
  bootstrap: BootstrapThesisResult
}

export function createExploration(command: CreateExplorationCommand, signal?: AbortSignal): Promise<ExplorationResult> {
  return requestJson<ExplorationResult>(
    '/api/research/explorations',
    { method: 'POST', body: JSON.stringify(command) },
    signal,
  )
}

export type PromoteThesisResult = {
  id: string
  status: string
  updatedAt: string
}

export function promoteThesis(id: string, signal?: AbortSignal): Promise<PromoteThesisResult> {
  return requestJson<PromoteThesisResult>(
    `/api/research/theses/${encodeURIComponent(id)}/promote`,
    { method: 'POST' },
    signal,
  )
}

export async function fetchThesisEvidence(id: string, signal?: AbortSignal): Promise<ThesisEvidenceDto[]> {
  const payload = await getJson<unknown>(`/api/research/theses/${encodeURIComponent(id)}/evidence?take=200`, signal)
  return listFromPayload<unknown>(payload, ['items', 'data', 'results', 'evidence']).map(normalizeEvidence)
}

export async function createThesis(command: CreateThesisCommand, signal?: AbortSignal): Promise<ThesisDto> {
  const payload = await requestJson<unknown>(
    '/api/research/theses',
    { method: 'POST', body: JSON.stringify(command) },
    signal,
  )
  return normalizeThesis(payload)
}

export function scanThesis(id: string, signal?: AbortSignal): Promise<ThesisScanResult> {
  return requestJson<ThesisScanResult>(
    `/api/research/theses/${encodeURIComponent(id)}/scan`,
    { method: 'POST', body: JSON.stringify({ lookbackDays: 30, batchSize: 1000 }) },
    signal,
  )
}

export function reviewThesisEvidence(
  thesisId: string,
  evidenceId: string,
  command: ReviewThesisEvidenceCommand,
  signal?: AbortSignal,
): Promise<ThesisEvidenceDto> {
  return requestJson<ThesisEvidenceDto>(
    `/api/research/theses/${encodeURIComponent(thesisId)}/evidence/${encodeURIComponent(evidenceId)}/review`,
    { method: 'PATCH', body: JSON.stringify(command) },
    signal,
  )
}

export type ClusterMemberDto = {
  id: string
  source: string
  sourceTier: string
  symbol: string | null
  headline: string
  summary: string | null
  url: string | null
  publisher: string | null
  publishedAt: string
}

export type ClusterMarketDto = {
  symbol: string | null
  status: string | null
  capturedAt: string | null
  quoteTime: string | null
  lastPrice: number | null
  previousClose: number | null
  movePercent: number | null
  benchmarkSymbol: string | null
  benchmarkMovePercent: number | null
  relativeMovePercent: number | null
  relativeVolume: number | null
  reactionScore: number | null
  isAfterHours: boolean
  isStale: boolean
}

export type ClusterEventDto = {
  eventType: string
  summary: string
  sentiment: number
  importance: number
  components: {
    sourceWeight: number
    noveltyWeight: number
    eventClassPrior: number
    magnitudeSignal: number
  }
  slots: string
  model: { modelName: string; promptVersion: string; extractedAt: string }
  market: ClusterMarketDto | null
}

export type ClusterDetailDto = {
  id: string
  symbol: string | null
  memberCount: number
  dominantSourceTier: string | null
  topSourceWeight: number | null
  triageEventType: string | null
  triageConfidence: number | null
  firstSeenAt: string
  lastSeenAt: string
  members: ClusterMemberDto[]
  extractedEvent: ClusterEventDto | null
}

export async function fetchClusterDetail(clusterId: string, signal?: AbortSignal): Promise<ClusterDetailDto> {
  return getJson<ClusterDetailDto>(`/api/clusters/${encodeURIComponent(clusterId)}`, signal)
}

export type ResearchAssetDto = {
  id: string
  kind: string
  name: string
  symbol: string | null
  keywords: string[] | null
  createdAt: string
  updatedAt: string
}

export type ThesisRuleDto = {
  id: string
  thesisId?: string
  name: string
  isEnabled: boolean
  assetKeywords: string[]
  conceptKeywords: string[]
  eventTypes: string[]
  sourceNames: string[]
  sourceTiers: string[]
  excludeTerms: string[]
  minArticleSimilarity: number | null
  createdAt: string
  updatedAt: string
}

export type UpsertRuleCommand = {
  name?: string
  isEnabled?: boolean
  assetKeywords?: string[]
  conceptKeywords?: string[]
  eventTypes?: string[]
  sourceNames?: string[]
  sourceTiers?: string[]
  excludeTerms?: string[]
  minArticleSimilarity?: number | null
}

export type AssetQuery = {
  query?: string
  kind?: string
  take?: number
}

function asStringArray(value: unknown): string[] {
  if (Array.isArray(value)) return value.filter((v): v is string => typeof v === 'string')
  if (typeof value === 'string') {
    const trimmed = value.trim()
    if (trimmed.startsWith('[')) {
      try {
        const parsed = JSON.parse(trimmed)
        if (Array.isArray(parsed)) return parsed.filter((v): v is string => typeof v === 'string')
      } catch { /* fall through */ }
    }
  }
  return []
}

function normalizeAsset(raw: unknown): ResearchAssetDto {
  const obj = raw && typeof raw === 'object' ? raw as Record<string, unknown> : {}
  return {
    id: stringOf(obj, ['id', 'Id']) ?? '',
    kind: stringOf(obj, ['kind', 'Kind']) ?? 'concept',
    name: stringOf(obj, ['name', 'Name']) ?? '',
    symbol: stringOf(obj, ['symbol', 'Symbol']),
    keywords: stringsOf(obj, ['keywords', 'Keywords']),
    createdAt: stringOf(obj, ['createdAt', 'CreatedAt']) ?? '',
    updatedAt: stringOf(obj, ['updatedAt', 'UpdatedAt']) ?? '',
  }
}

function normalizeRule(raw: unknown): ThesisRuleDto {
  const obj = raw && typeof raw === 'object' ? raw as Record<string, unknown> : {}
  return {
    id: stringOf(obj, ['id', 'Id']) ?? '',
    thesisId: stringOf(obj, ['thesisId', 'ThesisId']) ?? undefined,
    name: stringOf(obj, ['name', 'Name']) ?? 'Research rule',
    isEnabled: valueOf(obj, ['isEnabled', 'IsEnabled']) !== false,
    assetKeywords: asStringArray(valueOf(obj, ['assetKeywords', 'AssetKeywords'])),
    conceptKeywords: asStringArray(valueOf(obj, ['conceptKeywords', 'ConceptKeywords'])),
    eventTypes: asStringArray(valueOf(obj, ['eventTypes', 'EventTypes'])),
    sourceNames: asStringArray(valueOf(obj, ['sourceNames', 'SourceNames'])),
    sourceTiers: asStringArray(valueOf(obj, ['sourceTiers', 'SourceTiers'])),
    excludeTerms: asStringArray(valueOf(obj, ['excludeTerms', 'ExcludeTerms'])),
    minArticleSimilarity: numberOf(obj, ['minArticleSimilarity', 'MinArticleSimilarity']),
    createdAt: stringOf(obj, ['createdAt', 'CreatedAt']) ?? '',
    updatedAt: stringOf(obj, ['updatedAt', 'UpdatedAt']) ?? '',
  }
}

export async function fetchResearchAssets(q: AssetQuery = {}, signal?: AbortSignal): Promise<ResearchAssetDto[]> {
  const p = new URLSearchParams()
  if (q.query) p.set('query', q.query)
  if (q.kind) p.set('kind', q.kind)
  if (q.take != null) p.set('take', q.take.toString())
  const qs = p.toString()
  const payload = await getJson<unknown>(`/api/research/assets${qs ? `?${qs}` : ''}`, signal)
  return listFromPayload<unknown>(payload, ['items', 'data', 'results', 'assets']).map(normalizeAsset)
}

export async function fetchThesisRules(thesisId: string, signal?: AbortSignal): Promise<ThesisRuleDto[]> {
  const payload = await getJson<unknown>(
    `/api/research/theses/${encodeURIComponent(thesisId)}/rules`,
    signal,
  )
  return listFromPayload<unknown>(payload, ['items', 'data', 'results', 'rules']).map(normalizeRule)
}

export async function createThesisRule(
  thesisId: string,
  command: UpsertRuleCommand,
  signal?: AbortSignal,
): Promise<ThesisRuleDto> {
  const payload = await requestJson<unknown>(
    `/api/research/theses/${encodeURIComponent(thesisId)}/rules`,
    { method: 'POST', body: JSON.stringify(command) },
    signal,
  )
  return normalizeRule(payload)
}

export async function updateThesisRule(
  thesisId: string,
  ruleId: string,
  command: UpsertRuleCommand,
  signal?: AbortSignal,
): Promise<ThesisRuleDto> {
  const payload = await requestJson<unknown>(
    `/api/research/theses/${encodeURIComponent(thesisId)}/rules/${encodeURIComponent(ruleId)}`,
    { method: 'PATCH', body: JSON.stringify(command) },
    signal,
  )
  return normalizeRule(payload)
}

export async function deleteThesisRule(
  thesisId: string,
  ruleId: string,
  signal?: AbortSignal,
): Promise<void> {
  await requestJson<void>(
    `/api/research/theses/${encodeURIComponent(thesisId)}/rules/${encodeURIComponent(ruleId)}`,
    { method: 'DELETE' },
    signal,
  )
}

export function fetchHealth(signal?: AbortSignal): Promise<{ status: string }> {
  return getJson<{ status: string }>('/health', signal)
}

export type IdeaScoutScores = {
  eventIntensity: number
  sourceQuality: number
  priceAction: number
  insiderSignal: number
  hypeRisk: number
  marketReaction: number
}

export type IdeaPriceDigest = {
  return7d: number | null
  return30d: number | null
  return90d: number | null
  return1y: number | null
  latestClose: number | null
  latestPriceDate: string | null
}

export type IdeaValuationDigest = {
  hasFundamentals: boolean
  marketCap: number | null
  peTtm: number | null
  forwardPe: number | null
  psTtm: number | null
  evRevenueTtm: number | null
  revenueGrowthTtmYoy: number | null
  epsGrowthTtmYoy: number | null
  valuationRisk: number | null
  updatedAt: string | null
}

export type IdeaEvidenceDigest = {
  eventCount: number
  primaryEventCount: number
  sourceCount: number
  primarySourceCount: number
  wireSourceCount: number
  tradePressCount: number
  lowTrustCount: number
  topEventType: string | null
  maxImportance: number
}

export type IdeaInsiderDigest = {
  openMarketTransactions: number
  netDollars: number
  grossDollars: number
  latestTransactionAt: string | null
}

export type IdeaRadarItem = {
  symbol: string
  name: string
  category: 'deep-dive' | 'hype-check' | 'fresh' | 'watch' | 'thin' | (string & {})
  interestScore: number
  hypeRisk: number
  qualityScore: number
  stance: 'constructive' | 'caution' | 'mixed' | (string & {})
  latestSignalAt: string | null
  scouts: IdeaScoutScores
  valuation: IdeaValuationDigest
  price: IdeaPriceDigest
  evidence: IdeaEvidenceDigest
  insiders: IdeaInsiderDigest
  whyNow: string[]
  hypeCheck: string[]
  watchNext: string[]
}

export type IdeaRadarDto = {
  generatedAt: string
  windowDays: number
  windowStart: string
  universe: {
    candidates: number
    eventRows: number
    symbolsWithPrices: number
    symbolsWithInsiders: number
    symbolsWithFundamentals?: number
  }
  items: IdeaRadarItem[]
}

export type ForwardPipelineModuleDto = {
  key: string
  label: string
  description: string
  weight: number
}

export type ForwardModuleResultDto = {
  key: string
  label: string
  score: number
  weight: number
  contribution: number
  rationale: string
  inputs: string[]
}

export type ForwardIdeaItemDto = {
  symbol: string
  name: string
  setupType: string
  group: string | null
  tradeIntent: string
  pipelineScore: number
  thesisFit: number
  actionability: 'research-now' | 'watchlist' | 'monitor' | 'wait' | (string & {})
  crowdingRisk: number
  latestSignalAt: string | null
  modules: ForwardModuleResultDto[]
  rationale: string[]
  nextChecks: string[]
  invalidates: string[]
  current: IdeaRadarItem
}

export type ForwardIdeasDto = {
  generatedAt: string
  windowDays: number
  windowStart: string
  thesis: {
    key: string
    label: string
    description: string
    keywords: string[]
    groups: Array<{
      key: string
      label: string
      setupType: string
      weight: number
      symbols: string[]
      subcategories?: Array<{ label: string; symbols: string[] }> | null
      benchmarks?: string[] | null
    }>
  }
  pipeline: {
    modules: ForwardPipelineModuleDto[]
    crowdingGuard: {
      enabled: boolean
      excluded: number
      rule: string
    }
  }
  universe: {
    candidates: number
    eventRows: number
    symbolsWithPrices: number
    symbolsWithFundamentals: number
  }
  items: ForwardIdeaItemDto[]
}

export type ForwardPipelineCatalogDto = {
  defaultPipelineKey: string
  items: Array<{
    key: string
    label: string
    description: string
    keywords: string[]
    aliases: string[]
    moduleKeys: string[]
    groups: Array<{
      key: string
      label: string
      setupType: string
      weight: number
      symbols: string[]
      subcategories?: Array<{ label: string; symbols: string[] }> | null
      benchmarks?: string[] | null
    }>
  }>
  modules: ForwardPipelineModuleDto[]
}

export type IdeaSourceMix = {
  total: number
  byTier: Array<{ tier: string; count: number; share: number }>
  bySource: Array<{ source: string; tier: string | null; count: number; latestAt: string }>
}

export type IdeaBriefPrice = {
  hasPrice: boolean
  latestClose: number | null
  latestDate: string | null
  return7d: number | null
  return30d: number | null
  return90d: number | null
  return1y: number | null
  ytdReturn: number | null
  yearHigh?: number | null
  yearLow?: number | null
  rangePosition?: number | null
}

export type IdeaFundamentals = {
  hasFundamentals: boolean
  source: string | null
  ingestedAt: string | null
  status: string
  error?: string | null
  name?: string | null
  industry?: string | null
  exchange?: string | null
  currency?: string | null
  webUrl?: string | null
  ipoDate?: string | null
  marketCap?: number | null
  enterpriseValue?: number | null
  shareOutstanding?: number | null
  peTtm?: number | null
  forwardPe?: number | null
  pegTtm?: number | null
  psTtm?: number | null
  evRevenueTtm?: number | null
  evEbitdaTtm?: number | null
  priceToBook?: number | null
  priceToFreeCashFlowTtm?: number | null
  revenueGrowthTtmYoy?: number | null
  epsGrowthTtmYoy?: number | null
  grossMarginTtm?: number | null
  operatingMarginTtm?: number | null
  netMarginTtm?: number | null
  roeTtm?: number | null
  debtToEquityQuarterly?: number | null
  beta?: number | null
  week52High?: number | null
  week52Low?: number | null
  week52PriceReturnDaily?: number | null
  valuationRisk?: number | null
}

export type IdeaOverpricing = {
  level: 'low' | 'moderate' | 'high' | 'unknown' | (string & {})
  score: number | null
  label: string
  reasons: string[]
  missingInputs: string[]
}

export type IdeaBriefInsider = {
  totalTransactions: number
  openMarketTransactions: number
  bought: number
  sold: number
  netDollars: number
  latestTransactionAt: string | null
  topInsiders: Array<{
    ownerName: string
    role: string
    transactions: number
    netDollars: number
    latestTransactionAt: string | null
  }>
}

export type IdeaTopEvent = {
  clusterId: string
  eventType: string
  summary: string
  importance: number
  sentiment: number
  sourceTier: string
  memberCount: number
  lastSeenAt: string
  market: {
    reactionScore: number | null
    movePercent: number | null
    relativeMovePercent: number | null
    relativeVolume: number | null
  }
  topSource: {
    topSource: string | null
    topPublisher: string | null
    topHeadline: string | null
    topUrl: string | null
  }
}

export type IdeaBriefNarrative = {
  bottomLine: string
  bullCase: string[]
  bearCase: string[]
  nextQuestions: string[]
  researchMode: string
  overpricing?: IdeaOverpricing
}

export type IdeaBriefDto = {
  generatedAt: string
  windowDays: number
  symbol: string
  metadata: {
    companyName: string
    cik: string
    irFeedUrl: string | null
    aliases: string[]
  } | null
  idea: IdeaRadarItem
  price: IdeaBriefPrice
  fundamentals: IdeaFundamentals
  overpricing: IdeaOverpricing
  sourceMix: IdeaSourceMix
  eventMix: Array<{ eventType: string; count: number; maxImportance: number }>
  topEvents: IdeaTopEvent[]
  insiders: IdeaBriefInsider
  theses: Array<Record<string, unknown>>
  transcripts: Array<Record<string, unknown>>
  filingChunks: Array<Record<string, unknown>>
  calendar: Array<{
    symbol: string
    eventType: string
    label: string
    scheduledAt: string
    status: string
    source: string
  }>
  brief: IdeaBriefNarrative
  dataGaps: string[]
}

export type IdeaMemoClaim = {
  claim: string
  evidenceIds: string[]
}

export type IdeaMemoAction = {
  action?: string
  trigger?: string
  evidenceIds: string[]
}

export type IdeaMemoContent = {
  bottomLine: string
  researchMode: 'deep-dive' | 'hype-check' | 'watch' | 'skip' | (string & {})
  bullCase: IdeaMemoClaim[]
  bearCase: IdeaMemoClaim[]
  contradictions: IdeaMemoClaim[]
  overpricingRisk: {
    level: 'low' | 'moderate' | 'high' | 'unknown' | (string & {})
    rationale: string
    evidenceIds: string[]
  }
  keyUnknowns: string[]
  nextResearchActions: IdeaMemoAction[]
  watchTriggers: IdeaMemoAction[]
  dataQualityWarnings: string[]
}

export type IdeaMemoDto = {
  symbol: string
  windowDays: number
  evidenceHash: string
  currentEvidenceHash: string
  status: 'pending' | 'running' | 'ready' | 'failed' | (string & {})
  currentStatus: 'pending' | 'running' | 'ready' | 'failed' | (string & {})
  isCurrent: boolean
  requestedAt: string
  startedAt: string | null
  generatedAt: string | null
  completedAt: string | null
  modelName: string | null
  promptVersion: string | null
  memo: IdeaMemoContent | null
  error: string | null
}

export type IdeaEvidenceDto = {
  symbol: string
  windowDays: number
  evidenceId: string
  evidenceType: string
  title: string
  subtitle: string | null
  summary: string | null
  data: Record<string, unknown> | Array<unknown> | string | number | boolean | null
}

export type IdeaRadarParams = {
  windowDays?: number
  take?: number
}

export type ForwardIdeasParams = {
  thesis?: string
  windowDays?: number
  take?: number
  modules?: string[]
  includeCrowded?: boolean
}

export function fetchIdeaRadar(params: IdeaRadarParams = {}, signal?: AbortSignal): Promise<IdeaRadarDto> {
  const p = new URLSearchParams()
  if (params.windowDays != null) p.set('windowDays', String(params.windowDays))
  if (params.take != null) p.set('take', String(params.take))
  const qs = p.toString()
  return getJson<IdeaRadarDto>(`/api/ideas/radar${qs ? `?${qs}` : ''}`, signal)
}

export function fetchForwardIdeas(params: ForwardIdeasParams = {}, signal?: AbortSignal): Promise<ForwardIdeasDto> {
  const p = new URLSearchParams()
  if (params.thesis) p.set('thesis', params.thesis)
  if (params.windowDays != null) p.set('windowDays', String(params.windowDays))
  if (params.take != null) p.set('take', String(params.take))
  if (params.modules?.length) p.set('modules', params.modules.join(','))
  if (params.includeCrowded != null) p.set('includeCrowded', String(params.includeCrowded))
  const qs = p.toString()
  return getJson<ForwardIdeasDto>(`/api/ideas/forward${qs ? `?${qs}` : ''}`, signal)
}

export function fetchForwardPipelines(signal?: AbortSignal): Promise<ForwardPipelineCatalogDto> {
  return getJson<ForwardPipelineCatalogDto>('/api/ideas/forward/pipelines', signal)
}

export function fetchIdeaBrief(symbol: string, windowDays = 90, signal?: AbortSignal): Promise<IdeaBriefDto> {
  const p = new URLSearchParams({ windowDays: String(windowDays) })
  return getJson<IdeaBriefDto>(`/api/ideas/symbols/${encodeURIComponent(symbol)}?${p.toString()}`, signal)
}

export function fetchIdeaMemo(symbol: string, windowDays = 90, signal?: AbortSignal): Promise<IdeaMemoDto> {
  const p = new URLSearchParams({ windowDays: String(windowDays) })
  return getJson<IdeaMemoDto>(`/api/ideas/symbols/${encodeURIComponent(symbol)}/memo?${p.toString()}`, signal)
}

export function fetchIdeaEvidence(symbol: string, evidenceId: string, windowDays = 90, evidenceHash?: string, signal?: AbortSignal): Promise<IdeaEvidenceDto> {
  const p = new URLSearchParams({ windowDays: String(windowDays), evidenceId })
  if (evidenceHash) p.set('evidenceHash', evidenceHash)
  return getJson<IdeaEvidenceDto>(`/api/ideas/symbols/${encodeURIComponent(symbol)}/evidence?${p.toString()}`, signal)
}

export function refreshIdeaMemo(symbol: string, windowDays = 90, signal?: AbortSignal): Promise<IdeaMemoDto> {
  const p = new URLSearchParams({ windowDays: String(windowDays), force: 'true' })
  return requestJson<IdeaMemoDto>(
    `/api/ideas/symbols/${encodeURIComponent(symbol)}/memo/refresh?${p.toString()}`,
    { method: 'POST' },
    signal,
  )
}

export type MarketOverviewQuote = {
  symbol: string
  label: string
  group: string
  last: number | null
  previousClose: number | null
  change: number | null
  changePercent: number | null
  asOf: string | null
  ingestedAt: string | null
  status: string
  provider: string | null
  source: string | null
  weight: number
  delayed?: boolean
}

export type MarketOverviewDriver = {
  clusterId: string
  symbol: string | null
  eventType: string
  summary: string
  importance: number
  sentiment: number
  sourceTier: string
  memberCount: number
  lastSeenAt: string
  topSource: {
    source: string
    sourceTier: string
    publisher: string | null
    headline: string
    url: string | null
    publishedAt: string
  } | null
  market: {
    symbol: string
    movePercent: number | null
    relativeMovePercent: number | null
    relativeVolume: number | null
    reactionScore: number | null
    status: string | null
    isStale: boolean
  } | null
}

export type MarketOverviewCalendarEntry = {
  id: string
  eventType: string
  symbol: string | null
  label: string
  scheduledAt: string
  isTimeSpecific: boolean
  status: string
  source: string
}

export type MarketOverviewSectorDetail = {
  symbol: string
  label: string
  group: string
  members: MarketOverviewQuote[]
  drivers: MarketOverviewDriver[]
}

export type MarketOverviewWeeklyPerformanceItem = {
  symbol: string
  label: string
  returnPct?: number | null
  lastClose?: number | null
  startClose?: number | null
  status?: string
  warning?: string | null
  stale?: boolean
  asOf?: string | null
}

export type MarketOverviewWeeklyPerformanceGroup = {
  key: string
  label: string
  items: MarketOverviewWeeklyPerformanceItem[]
}

export type MarketOverviewWeeklyPerformance = {
  windowStart?: string
  windowEnd?: string
  groups: MarketOverviewWeeklyPerformanceGroup[]
  topMovers?: MarketOverviewWeeklyPerformanceItem[]
  warnings?: string[]
}

export type MarketOverviewDto = {
  generatedAt: string
  pulse: MarketOverviewQuote[]
  sectors: MarketOverviewQuote[]
  movers: MarketOverviewQuote[]
  watchlist: MarketOverviewQuote[]
  sectorDetails: MarketOverviewSectorDetail[]
  breadth: {
    total: number
    advancers: number
    decliners: number
    unchanged: number
    averageMove: number | null
    positiveShare: number | null
  }
  drivers: MarketOverviewDriver[]
  calendar: MarketOverviewCalendarEntry[]
  freshness: {
    latestQuoteAt: string | null
    quoteStatuses: Array<{ status: string; count: number }>
  }
  weeklyPerformance?: MarketOverviewWeeklyPerformance | null
}

export function fetchMarketOverview(signal?: AbortSignal): Promise<MarketOverviewDto> {
  return getJson<MarketOverviewDto>('/api/market/overview', signal)
}

export type Position = {
  id: number
  symbol: string
  metal: string
  targetAllocationPct: number
  deployedPct: number
  entryPrice: number | null
  entryDate: string | null
  scaleInTriggerPrice: number | null
  scaleInNotes: string | null
  status: 'partial' | 'full' | 'planned' | (string & {})
  createdAt: string
  updatedAt: string
  thesisId: string
}

export type MetalAllocation = {
  metal: string
  totalTargetPct: number
  totalDeployedPct: number
  positionCount: number
  positions: Position[]
}

export type Portfolio = {
  totalTargetPct: number
  totalDeployedPct: number
  reservedPct: number
  metals: MetalAllocation[]
}

export type Catalyst = {
  id: number
  thesisId: string
  title: string
  description: string | null
  catalystDate: string
  metal: string
  catalystType: string
  resolved: boolean
  outcome: string | null
  createdAt: string
  thesis: unknown | null
}

export type KillCriterion = {
  id: number
  thesisId: string
  scenario: string
  monitoringKeywords: string | null
  threatLevel: 'critical' | 'elevated' | 'watching' | 'dormant' | (string & {})
  contradictingEvidenceCount: number
  lastTriggeredReason: string | null
  lastEscalatedAt: string | null
  createdAt: string
  thesis: unknown | null
}

export function fetchCatalysts(signal?: AbortSignal): Promise<Catalyst[]> {
  return getJson<Catalyst[]>('/api/research/catalysts', signal)
}

export function fetchKillCriteria(signal?: AbortSignal): Promise<KillCriterion[]> {
  return getJson<KillCriterion[]>('/api/research/kill-criteria', signal)
}

export function fetchPositions(signal?: AbortSignal): Promise<Position[]> {
  return getJson<Position[]>('/api/research/positions', signal)
}

export function fetchPortfolio(signal?: AbortSignal): Promise<Portfolio> {
  return getJson<Portfolio>('/api/research/positions/portfolio', signal)
}

export type WeeklyOpenQuote = {
  provider: string
  symbol: string
  displayName: string | null
  instrumentType: string | null
  exchange: string | null
  currency: string | null
  last: number | null
  previousClose: number | null
  change: number | null
  changePercent: number | null
  asOf: string | null
  ingestedAt: string
  status: string
  error: string | null
  delayed?: boolean
}

export type WeeklyOpenCluster = {
  clusterId: string
  symbol: string | null
  eventType: string
  summary: string
  importance: number
  sentiment: number
  components: {
    sourceWeight: number
    noveltyWeight: number
    eventClassPrior: number
    magnitudeSignal: number
  }
  sourceTier: string
  memberCount: number
  articlesInWindow: number
  activityRatio: number
  firstSeenInWindow: boolean
  weightedImportance: number
  firstSeenAt: string
  lastSeenAt: string
  topSource: {
    source: string
    sourceTier: string
    publisher: string | null
    headline: string
    url: string | null
    publishedAt: string
  } | null
}

export type WeeklyOpenThesisEvidence = {
  evidenceId: string
  stance: string | null
  stanceConfidence: number | null
  stanceRationale: string | null
  isPinned: boolean
  matchedAt: string
  clusterId: string | null
  articleId: string | null
  transcriptSegmentId: string | null
  articleChunkId: string | null
  headline: string | null
  symbol: string | null
  importance: number | null
  sourceTier: string | null
}

export type WeeklyOpenThesis = {
  id: string
  name: string
  status: string
  primarySymbol: string | null
  newSupports: number
  newContradicts: number
  newNeutral: number
  newUnknown: number
  newPinned: number
  newTotal: number
  leanDelta: number
  priorLean: number
  topNew: WeeklyOpenThesisEvidence | null
}

export type WeeklyOpenCalendarEntry = {
  id: string
  eventType: string
  symbol: string | null
  label: string
  scheduledAt: string
  isTimeSpecific: boolean
  status: string
  source: string
  notes: string | null
}

export type WeeklyOpenPipelineFreshness = {
  stage: string
  latestStartedAt: string
  latestStatus: string
}

export type WeeklyOpenWindowPreset = 'auto' | 'since-friday' | 'since-prior-close' | '1d' | '3d' | '1w' | '1m' | 'custom'
export type WeeklyOpenWindowKind = 'session-close' | 'duration' | 'custom'

export type WeeklyOpenDto = {
  generatedAt: string
  windowStart: string
  windowEnd: string
  windowPreset: WeeklyOpenWindowPreset
  windowKind: WeeklyOpenWindowKind
  lookbackHours: number
  lookbackDays: number
  calendarHorizonDays: number
  sinceUtc: string
  quotes: WeeklyOpenQuote[]
  clusters: WeeklyOpenCluster[]
  theses: WeeklyOpenThesis[]
  calendar: WeeklyOpenCalendarEntry[]
  pipelineFreshness: WeeklyOpenPipelineFreshness[]
}

export type WeeklyOpenParams = {
  window?: WeeklyOpenWindowPreset
  topClusters?: number
  calendarDays?: number
  from?: string
}

export function fetchWeeklyOpen(params?: WeeklyOpenParams, signal?: AbortSignal): Promise<WeeklyOpenDto> {
  const qs = new URLSearchParams()
  qs.set('topClusters', String(params?.topClusters ?? 10))
  if (params?.window) qs.set('window', params.window)
  if (params?.calendarDays != null) qs.set('calendarDays', String(params.calendarDays))
  if (params?.from) qs.set('from', params.from)
  return getJson<WeeklyOpenDto>(`/api/weekly-open?${qs.toString()}`, signal)
}

export type SourceHealthStatus = 'healthy' | 'degraded' | 'stale' | 'silent'

export type SourceHealthRow = {
  name: string
  tier: string
  weight: number
  count1h: number
  count24h: number
  count7d: number
  lastIngestedAt: string | null
  lastPolledAt?: string | null
  lastStartedAt?: string | null
  lastSucceededAt?: string | null
  lastFailedAt?: string | null
  nextEligibleRunAt?: string | null
  consecutiveFailures?: number
  lastError?: string | null
  articleStatus?: SourceHealthStatus
  status: SourceHealthStatus
}

export type SourcesHealthDto = {
  summary: {
    healthy: number
    degraded?: number
    stale: number
    silent: number
    total24h: number
  }
  sources: SourceHealthRow[]
}

export function fetchSourcesHealth(signal?: AbortSignal): Promise<SourcesHealthDto> {
  return getJson<SourcesHealthDto>('/api/sources/health', signal)
}

export type PipelineRunStatus = 'running' | 'succeeded' | 'succeeded_with_errors' | 'failed' | 'dead_letter' | (string & {})
export type PipelineQueueStatus = 'queued' | 'running' | 'completed' | 'dead_letter' | 'cancelled' | (string & {})

export type PipelineStageRow = {
  stage: string
  latestStatus: PipelineRunStatus
  latestStartedAt: string
  latestFinishedAt: string | null
  runs: number
  failures: number
  errorCount: number
  inputCount: number
  outputCount: number
}

export type PipelineStagesDto = {
  stages: PipelineStageRow[]
}

export type PipelineRunRow = {
  id: string
  stage: string
  scopeType: string | null
  scopeKey: string | null
  trigger: string
  status: PipelineRunStatus
  attempt: number
  startedAt: string
  finishedAt: string | null
  durationMs: number | null
  inputCount: number
  outputCount: number
  skippedCount: number
  errorCount: number
  errorCategory: string | null
  errorMessage: string | null
  metadata: string
}

export type PipelineRunsDto = {
  runs: PipelineRunRow[]
}

export type PipelineQueueCount = {
  workType: string
  status: PipelineQueueStatus
  count: number
}

export type PipelineQueueItem = {
  id: string
  workType: string
  naturalKey: string
  status: PipelineQueueStatus
  priority: number
  availableAt: string
  createdAt: string
  updatedAt: string
  attemptCount: number
  maxAttempts: number
  currentAttemptId: string | null
  claimedBy: string | null
  claimedAt: string | null
  leaseExpiresAt: string | null
  completedAt: string | null
  deadLetteredAt: string | null
  lastError: string | null
}

export type PipelineQueueAttempt = {
  id: string
  workItemId: string
  workType: string
  naturalKey: string
  attemptNumber: number
  status: PipelineQueueStatus
  workerId: string
  startedAt: string
  leaseExpiresAt: string
  finishedAt: string | null
  durationMs: number | null
  errorMessage: string | null
}

export type PipelineQueueSummaryDto = {
  counts: PipelineQueueCount[]
  oldestPending: PipelineQueueItem | null
  recentDeadLetters: PipelineQueueItem[]
  recentErrors: PipelineQueueAttempt[]
}

export type PipelineQueueItemsDto = {
  items: PipelineQueueItem[]
}

export type PipelineQueueAttemptsDto = {
  attempts: PipelineQueueAttempt[]
}

export type PipelineQueueActionResult = {
  id: string
  status: PipelineQueueStatus
  changed: boolean
  message: string
}

export type PipelineQueuePurgeResult = {
  deleted: number
  cutoff: string
  limit: number
}

export type PipelineQueueQuery = {
  workType?: string | null
  status?: string | null
  q?: string
  take?: number
}

export function fetchPipelineStages(signal?: AbortSignal): Promise<PipelineStagesDto> {
  return getJson<PipelineStagesDto>('/api/pipeline/stages', signal)
}

export function fetchPipelineRuns(signal?: AbortSignal): Promise<PipelineRunsDto> {
  return getJson<PipelineRunsDto>('/api/pipeline/runs?take=12', signal)
}

export function fetchPipelineQueueSummary(signal?: AbortSignal): Promise<PipelineQueueSummaryDto> {
  return getJson<PipelineQueueSummaryDto>('/api/pipeline/queue/summary', signal)
}

export function fetchPipelineQueueItems(q: PipelineQueueQuery = {}, signal?: AbortSignal): Promise<PipelineQueueItemsDto> {
  const p = new URLSearchParams()
  if (q.workType) p.set('workType', q.workType)
  if (q.status) p.set('status', q.status)
  if (q.q) p.set('q', q.q)
  if (q.take != null) p.set('take', q.take.toString())
  const qs = p.toString()
  return getJson<PipelineQueueItemsDto>(`/api/pipeline/queue/items${qs ? `?${qs}` : ''}`, signal)
}

export function fetchPipelineQueueAttempts(id: string, signal?: AbortSignal): Promise<PipelineQueueAttemptsDto> {
  return getJson<PipelineQueueAttemptsDto>(`/api/pipeline/queue/items/${encodeURIComponent(id)}/attempts?take=8`, signal)
}

export function retryPipelineQueueItem(id: string, signal?: AbortSignal): Promise<PipelineQueueActionResult> {
  return requestJson<PipelineQueueActionResult>(
    `/api/pipeline/queue/items/${encodeURIComponent(id)}/retry`,
    { method: 'POST' },
    signal,
  )
}

export function cancelPipelineQueueItem(id: string, signal?: AbortSignal): Promise<PipelineQueueActionResult> {
  return requestJson<PipelineQueueActionResult>(
    `/api/pipeline/queue/items/${encodeURIComponent(id)}/cancel`,
    { method: 'POST' },
    signal,
  )
}

export function purgeCompletedPipelineQueue(olderThanHours = 168, take = 1000, signal?: AbortSignal): Promise<PipelineQueuePurgeResult> {
  return requestJson<PipelineQueuePurgeResult>(
    `/api/pipeline/queue/purge-completed?olderThanHours=${olderThanHours}&take=${take}`,
    { method: 'POST' },
    signal,
  )
}

export type SymbolSearchResult = {
  symbol: string
  description: string
  watched?: boolean
  source?: 'watchlist' | 'registry' | 'known' | 'preview' | (string & {})
}

export type WatchedSymbol = {
  id: string
  symbol: string
  name: string
  createdAt: string
  last?: number | null
  change?: number | null
  changePercent?: number | null
  quoteStatus?: string | null
  quoteAsOf?: string | null
  quoteIngestedAt?: string | null
}

export type SymbolCoverage = {
  interval: string
  earliest: string
  latest: string
  count: number
}

export type SymbolInfo = {
  symbol: string
  description: string
  kind: string | null
  timezone: string
  sessionStart: string
  sessionEnd: string
  coverage: SymbolCoverage[]
}

export type PriceBarRow = {
  t: number
  o: number
  h: number
  l: number
  c: number
  v: number | null
}

export type PriceBarsResponse = {
  symbol: string
  interval: string
  bars: PriceBarRow[]
}

export type ChartMark = {
  clusterId: string
  t: number
  eventType: string
  importance: number
  sentiment: number
  summary: string
  sourceTier: string
  memberCount: number
}

export type ChartThesisOverlay = {
  clusterId: string
  stance: string | null
  stanceConfidence: number | null
  reviewStatus: string | null
  isPinned: boolean
}

export type ChartMarksResponse = {
  symbol: string
  marks: ChartMark[]
  thesisOverlay: ChartThesisOverlay[] | null
}

export type CalendarEntry = {
  id: string
  eventType: string
  symbol: string | null
  label: string
  t: number
  isTimeSpecific: boolean
  status: string
  notes: string | null
  source: string
  clusterId: string | null
}

export type ChartBarsQuery = {
  symbol: string
  interval: string
  from?: Date
  to?: Date
  take?: number
}

export type ChartMarksQuery = {
  symbol: string
  from?: Date
  to?: Date
  thesisId?: string
  take?: number
}

export type ChartCalendarQuery = {
  symbol?: string
  from?: Date
  to?: Date
  eventType?: string
  take?: number
}

function isoOf(d?: Date): string | undefined {
  return d ? d.toISOString() : undefined
}

export function searchChartSymbols(query: string, signal?: AbortSignal): Promise<SymbolSearchResult[]> {
  const p = new URLSearchParams()
  if (query) p.set('query', query)
  return getJson<SymbolSearchResult[]>(`/api/chart/symbols/search?${p.toString()}`, signal)
}

export function fetchChartSymbolInfo(symbol: string, signal?: AbortSignal): Promise<SymbolInfo> {
  return getJson<SymbolInfo>(`/api/chart/symbols/${encodeURIComponent(symbol)}`, signal)
}

export function fetchChartBars(q: ChartBarsQuery, signal?: AbortSignal): Promise<PriceBarsResponse> {
  const p = new URLSearchParams({ symbol: q.symbol, interval: q.interval })
  const from = isoOf(q.from); if (from) p.set('from', from)
  const to = isoOf(q.to); if (to) p.set('to', to)
  if (q.take != null) p.set('take', q.take.toString())
  return getJson<PriceBarsResponse>(`/api/chart/bars?${p.toString()}`, signal)
}

export function fetchChartMarks(q: ChartMarksQuery, signal?: AbortSignal): Promise<ChartMarksResponse> {
  const p = new URLSearchParams({ symbol: q.symbol })
  const from = isoOf(q.from); if (from) p.set('from', from)
  const to = isoOf(q.to); if (to) p.set('to', to)
  if (q.thesisId) p.set('thesisId', q.thesisId)
  if (q.take != null) p.set('take', q.take.toString())
  return getJson<ChartMarksResponse>(`/api/chart/marks?${p.toString()}`, signal)
}

export function fetchWatchlist(signal?: AbortSignal): Promise<WatchedSymbol[]> {
  return getJson<WatchedSymbol[]>('/api/chart/watchlist', signal)
}

export function watchSymbol(symbol: string, name?: string, signal?: AbortSignal): Promise<{ id: string; symbol: string; name: string; watched: true }> {
  return requestJson('/api/chart/watch', {
    method: 'POST',
    body: JSON.stringify({ symbol, name }),
  }, signal)
}

export function unwatchSymbol(symbol: string, signal?: AbortSignal): Promise<void> {
  return requestJson(`/api/chart/watch/${encodeURIComponent(symbol)}`, { method: 'DELETE' }, signal)
}

export function fetchChartCalendar(q: ChartCalendarQuery = {}, signal?: AbortSignal): Promise<CalendarEntry[]> {
  const p = new URLSearchParams()
  if (q.symbol) p.set('symbol', q.symbol)
  const from = isoOf(q.from); if (from) p.set('from', from)
  const to = isoOf(q.to); if (to) p.set('to', to)
  if (q.eventType) p.set('eventType', q.eventType)
  if (q.take != null) p.set('take', q.take.toString())
  const qs = p.toString()
  return getJson<CalendarEntry[]>(`/api/chart/calendar${qs ? `?${qs}` : ''}`, signal)
}
