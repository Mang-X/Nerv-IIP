import { type JsonRecord, type createWalkthroughEvidence } from '../issue1912-walkthrough-evidence'
import { queryPath as canonicalQueryPath } from '../issue1912-walkthrough-query'
import { type Browser, type Page } from '@playwright/test'
import type { createWalkthroughSession } from '../issue1912-walkthrough-session'
import type { createWalkthroughPageProofs } from '../issue1912-walkthrough-pages'

export const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL

export const RFQ_NO = 'RFQ-WALK-001'
export const SUPPLIER_QUOTATION_NO = 'SQ-WALK-001'
export const SALES_QUOTATION_NO = 'QUO-WALK-001'
export const PURCHASE_ORDER_NO = 'PO-WALK-001'
export const PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE = 'purchase-order-release'
export const PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION = 1
export const SALES_ORDER_NO = 'SO-WALK-001'
export const DELIVERY_ORDER_NO = 'DO-WALK-001'
export const INBOUND_INTENT_KEY = 'IN-WALK-001'
export const FINISHED_SKU = 'FG-QJ-P1-L'
export const SITE_CODE = 'SITE-001'
export const INBOUND_LOCATION = 'loc-raw-01'
export const LINE_SIDE_LOCATION = 'loc-line-01'
export const FINISHED_GOODS_LOCATION = 'loc-fg-01'
export const QUANTITY = 1

export const queryPath = (path: string, query: JsonRecord) =>
  canonicalQueryPath(path, query, baseURL!)

export type WalkthroughContext = {
  browser: Browser
  page: Page
  organizationId: string
  environmentId: string
  principalId: string
  workerPassword: string
  generatedAtUtc: Date
  evidenceDirectory: string
  screenshotDirectory: string
  session: Awaited<ReturnType<typeof createWalkthroughSession>>
  ledger: ReturnType<typeof createWalkthroughEvidence>
  proofs: ReturnType<typeof createWalkthroughPageProofs>
  erpListQuery: (query?: JsonRecord) => JsonRecord
  setPurchaseReceiptNo: (value: string) => void
}
