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
}>

export type WmsInboundWalkthroughScenarioFacts = WmsWalkthroughScenarioFacts &
  Readonly<{
    siteCode: string
  }>

export const NERV_1571_WMS_INBOUND_FACTS: WmsInboundWalkthroughScenarioFacts = {
  organizationId: 'org-live',
  environmentId: 'env-live',
  scopeKind: 'work-pool',
  scopeId: 'pool-receiving-001',
  siteCode: 'SITE-001',
}

export const NERV_1571_WMS_OUTBOUND_FACTS: WmsWalkthroughScenarioFacts = {
  organizationId: 'org-live',
  environmentId: 'env-live',
  scopeKind: 'work-pool',
  scopeId: 'pool-shipping-001',
}
