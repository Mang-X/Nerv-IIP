export type WmsWalkthroughPageWindowInput =
  | Readonly<{
      mode: 'default'
      skip: 0
      take: 10
    }>
  | Readonly<{
      mode: 'selected'
      skip: 0
      take: number
    }>

/**
 * Independent NERV-1571 scenario vector.
 *
 * The semantic source is the NERV-1571 acceptance recorded in GitHub #1912 and its confirmed
 * regression sample. This module is fixture input only; it is not derived from a page response or
 * from the implementation under test.
 */
export type WmsWalkthroughScenarioFacts = Readonly<{
  organizationId: string
  environmentId: string
  scopeKind: string
  scopeId: string
  keyword: string
  /**
   * Walkthrough input for the first page window. The mode records whether the scenario relies on
   * the page's default window or performs an explicit page-size action; callers must carry that
   * distinction into the page proof instead of inferring it from a response.
   */
  pageWindow: WmsWalkthroughPageWindowInput
}>

export type WmsInboundWalkthroughScenarioFacts = WmsWalkthroughScenarioFacts &
  Readonly<{
    siteCode: string
  }>

export type WmsWalkthroughQueryFacts = Omit<
  WmsWalkthroughScenarioFacts,
  'scopeKind' | 'pageWindow'
> &
  Readonly<{
    scopeKind: 'work-pool'
    skip: 0
    take: number
  }>

export type WmsInboundWalkthroughQueryFacts = Omit<
  WmsInboundWalkthroughScenarioFacts,
  'scopeKind' | 'pageWindow'
> &
  Readonly<{
    scopeKind: 'work-pool'
    skip: 0
    take: number
  }>

/** The real low-cardinality walkthrough uses the page's documented default window. */
export const NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT = {
  mode: 'default',
  skip: 0,
  take: 10,
} as const satisfies WmsWalkthroughPageWindowInput

/** The fixture vector's explicit page-window input; it is not the page's implicit default. */
export const NERV_1571_WMS_PAGE_WINDOW_INPUT = {
  mode: 'selected',
  skip: 0,
  take: 20,
} as const satisfies WmsWalkthroughPageWindowInput

export const NERV_1571_WMS_INBOUND_FACTS: WmsInboundWalkthroughScenarioFacts = {
  organizationId: 'org-live',
  environmentId: 'env-live',
  scopeKind: 'work-pool',
  scopeId: 'pool-receiving-001',
  keyword: 'IN-WALK-001',
  pageWindow: NERV_1571_WMS_PAGE_WINDOW_INPUT,
  siteCode: 'SITE-001',
}

export const NERV_1571_WMS_OUTBOUND_FACTS: WmsWalkthroughScenarioFacts = {
  organizationId: 'org-live',
  environmentId: 'env-live',
  scopeKind: 'work-pool',
  scopeId: 'pool-shipping-001',
  keyword: 'DO-WALK-001',
  pageWindow: NERV_1571_WMS_PAGE_WINDOW_INPUT,
}

export const NERV_1571_WMS_INBOUND_QUERY_FACTS: WmsInboundWalkthroughQueryFacts = {
  organizationId: NERV_1571_WMS_INBOUND_FACTS.organizationId,
  environmentId: NERV_1571_WMS_INBOUND_FACTS.environmentId,
  scopeKind: 'work-pool',
  skip: NERV_1571_WMS_PAGE_WINDOW_INPUT.skip,
  take: NERV_1571_WMS_PAGE_WINDOW_INPUT.take,
  scopeId: NERV_1571_WMS_INBOUND_FACTS.scopeId,
  keyword: NERV_1571_WMS_INBOUND_FACTS.keyword,
  siteCode: NERV_1571_WMS_INBOUND_FACTS.siteCode,
}

export const NERV_1571_WMS_OUTBOUND_QUERY_FACTS: WmsWalkthroughQueryFacts = {
  organizationId: NERV_1571_WMS_OUTBOUND_FACTS.organizationId,
  environmentId: NERV_1571_WMS_OUTBOUND_FACTS.environmentId,
  scopeKind: 'work-pool',
  skip: NERV_1571_WMS_PAGE_WINDOW_INPUT.skip,
  take: NERV_1571_WMS_PAGE_WINDOW_INPUT.take,
  scopeId: NERV_1571_WMS_OUTBOUND_FACTS.scopeId,
  keyword: NERV_1571_WMS_OUTBOUND_FACTS.keyword,
}
