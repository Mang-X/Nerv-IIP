export type WmsWalkthroughPageWindowInput = Readonly<{
  skip: 0
  take: number
}>

/**
 * Independent NERV-1571 scenario vector.
 *
 * The semantic source is docs/architecture/nerv-1571-wms-walkthrough-facts.md §场景事实,
 * backed by Linear NERV-1571 and GitHub #1912. This module is fixture input only; it is not
 * derived from a page response or from the implementation under test.
 */
export type WmsWalkthroughScenarioFacts = Readonly<{
  organizationId: string
  environmentId: string
  scopeKind: string
  scopeId: string
  keyword: string
  /**
   * Explicit walkthrough input for the first page window. This is deliberately not a page
   * implementation default: callers must carry the scenario's selected/declared window into the
   * expected query facts.
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

/** The fixture vector's explicit page-window input; it is not the page's implicit default. */
export const NERV_1571_WMS_PAGE_WINDOW_INPUT: WmsWalkthroughPageWindowInput = {
  skip: 0,
  take: 20,
}

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
