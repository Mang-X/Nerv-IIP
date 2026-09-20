import { expect, test, type Response } from '@playwright/test'
import { mkdir } from 'node:fs/promises'
import { dirname, join } from 'node:path'
import { withSessionCredentialCleanup } from './session-credential-tracker'
import {
  buildAuthorizedWorkPoolAssignment,
  executeWalkthroughPicking,
  selectAuthorizedWorkPoolScope,
  selectAuthorizedWorkSiteScope,
  type AuthorizedWorkPoolScope,
  type AuthorizedWorkSiteScope,
} from './issue1912-walkthrough-runtime'
import { classifyRequestFailure } from './issue1912-walkthrough-policy'
import {
  buildWmsInboundSelectionQueryFacts,
  buildWmsInboundListQueryFacts,
  buildWmsOutboundSelectionQueryFacts,
  buildWmsOutboundListQueryFacts,
} from './issue1912-wms-walkthrough-facts'
import { NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT } from './issue1912-wms-walkthrough-authority'
import { queryPath as canonicalQueryPath } from './issue1912-walkthrough-query'
import { runFinishedProduction } from './issue1853-finished-production'
import {
  createWalkthroughEvidence,
  REQUIRED_NODES,
  asRecord,
  dataOf,
  rowsOf,
  textOf,
  safeText,
  publicJson,
  dateOnly,
  inventoryStateFingerprint,
  inventoryMovementFingerprint,
  type JsonRecord,
} from './issue1912-walkthrough-evidence'
import { createWalkthroughSession } from './issue1912-walkthrough-session'
import { createWalkthroughPageProofs } from './issue1912-walkthrough-pages'

const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
const workerPassword = process.env.NERV_IIP_LEADER_DEMO_WORKER_PASSWORD
const evidencePath = process.env.NERV_IIP_ISSUE_1912_EVIDENCE_PATH
const runtimeProfileSource = process.env.NERV_IIP_ISSUE_1912_RUNTIME_PROFILE_SOURCE
const transport = process.env.NERV_IIP_ISSUE_1912_TRANSPORT
const persistence = process.env.NERV_IIP_ISSUE_1912_PERSISTENCE
const worldEnabled = process.env.NERV_IIP_ISSUE_1912_WORLD_ENABLED
const historyEnabled = process.env.NERV_IIP_ISSUE_1912_HISTORY_ENABLED
const scaleOrderCount = process.env.NERV_IIP_ISSUE_1912_SCALE_ORDER_COUNT

const requiresManagedSession = !baseURL || !adminPassword || !workerPassword || !evidencePath

test.setTimeout(25 * 60 * 1000)
test.describe.configure({ mode: 'serial' })

const RFQ_NO = 'RFQ-WALK-001'
const SUPPLIER_QUOTATION_NO = 'SQ-WALK-001'
const SALES_QUOTATION_NO = 'QUO-WALK-001'
const PURCHASE_ORDER_NO = 'PO-WALK-001'
const PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE = 'purchase-order-release'
const PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION = 1
const PURCHASE_RECEIPT_NO = 'PR-WALK-001'
const SALES_ORDER_NO = 'SO-WALK-001'
const DELIVERY_ORDER_NO = 'DO-WALK-001'
const INBOUND_ORDER_NO = 'IN-WALK-001'
const PUTAWAY_TASK_NO = 'PUT-WALK-001'
const PRODUCED_LOT_NO = 'LOT-WALK-001'
const PACK_REVIEW_NO = 'PACK-WALK-001'
const FINISHED_SKU = 'FG-QJ-P1-L'
const SITE_CODE = 'SITE-001'
const INBOUND_LOCATION = 'loc-raw-01'
const LINE_SIDE_LOCATION = 'loc-line-01'
const FINISHED_GOODS_LOCATION = 'loc-fg-01'
const QUANTITY = 1

const queryPath = (path: string, query: JsonRecord) => canonicalQueryPath(path, query, baseURL!)

test('request failure policy keeps superseded navigation aborts but records API failures', async ({
  page,
}) => {
  test.skip(
    test.info().project.name !== 'desktop',
    'the request policy probe is intentionally desktop-only',
  )

  const expectedCancellations: JsonRecord[] = []
  const unexpectedFailures: JsonRecord[] = []
  const apiFailures: JsonRecord[] = []
  page.on('requestfailed', (request) => {
    const classified = classifyRequestFailure({
      method: request.method(),
      url: request.url(),
      failure: safeText(request.failure()?.errorText ?? 'unknown request failure'),
      resourceType: request.resourceType(),
      isNavigationRequest: request.isNavigationRequest(),
    })
    if (classified.expected) expectedCancellations.push(classified.record)
    else unexpectedFailures.push(classified.record)
  })
  page.on('response', (response: Response) => {
    const url = new URL(response.url())
    if (url.pathname === '/api/issue1912-policy-failure' && response.status() >= 400) {
      apiFailures.push({
        kind: 'http-error',
        method: response.request().method(),
        path: url.pathname + url.search,
        status: response.status(),
        classification: 'api-http-error',
      })
    }
  })

  await page.route('**/issue1912-policy-navigation*', async (route) => {
    const url = new URL(route.request().url())
    if (url.searchParams.get('phase') === 'first')
      await new Promise((resolve) => setTimeout(resolve, 250))
    try {
      await route.fulfill({
        status: 200,
        contentType: 'text/html',
        body: '<!doctype html><p id="policy-success">success</p>',
      })
    } catch {
      // The first navigation is intentionally superseded and may be gone by the time its route resolves.
    }
  })
  await page.route('**/api/issue1912-policy-failure', async (route) => {
    await route.fulfill({
      status: 503,
      contentType: 'application/json',
      body: '{"error":"intentional policy probe"}',
    })
  })

  try {
    const firstRequest = page.waitForRequest(
      (request) => new URL(request.url()).pathname === '/issue1912-policy-navigation',
    )
    const firstNavigation = page
      .goto('/issue1912-policy-navigation?phase=first', {
        waitUntil: 'domcontentloaded',
        timeout: 30_000,
      })
      .catch(() => null)
    await firstRequest
    const secondNavigation = await page.goto('/issue1912-policy-navigation?phase=second', {
      waitUntil: 'domcontentloaded',
      timeout: 30_000,
    })
    await firstNavigation
    expect(secondNavigation?.status()).toBe(200)
    await expect(page.locator('#policy-success')).toHaveText('success')

    await page.evaluate(() => fetch('/api/issue1912-policy-failure').catch(() => undefined))
    await expect.poll(() => apiFailures.length).toBe(1)
    expect(
      expectedCancellations.some(
        (item) =>
          item.classification === 'expected-superseded-document-or-resource' &&
          textOf(item.resourceType) === 'document',
      ),
    ).toBe(true)
    expect(unexpectedFailures).toEqual([])
    expect(apiFailures[0]).toMatchObject({ status: 503, classification: 'api-http-error' })
  } finally {
    await page.unroute('**/issue1912-policy-navigation*')
    await page.unroute('**/api/issue1912-policy-failure')
  }
})

test('NERV-1127 / GitHub #1912 verifies the isolated walkthrough in real browser pages', async ({
  page,
  browser,
}) => {
  test.skip(
    requiresManagedSession,
    'requires a managed full-stack session and an evidence destination',
  )
  test.skip(
    test.info().project.name !== 'desktop',
    'the evidence run is intentionally desktop-only',
  )

  const generatedAtUtc = new Date()
  const evidenceDirectory = dirname(evidencePath!)
  const screenshotDirectory = join(
    evidenceDirectory,
    'issue1912-real-machine-walkthrough-screenshots',
  )
  await mkdir(screenshotDirectory, { recursive: true })

  let organizationId = ''
  let environmentId = ''
  let principalId = ''
  let workerPrincipalId = ''
  const ledger = createWalkthroughEvidence()
  const {
    evidence,
    record,
    markFailure,
    setup,
    uiEvidence,
    failedRequests,
    expectedRequestCancellations,
    expectedBusinessRejections,
    pageErrors,
  } = ledger

  const session = await createWalkthroughSession({
    page,
    browser,
    baseURL: baseURL!,
    setup,
    failedRequests,
    expectedRequestCancellations,
    expectedBusinessRejections,
    pageErrors,
  })
  const {
    adminRuntime,
    workerRuntime,
    sessionCredentialTracker,
    workerSessionCredentialTracker,
    workerContext,
    call,
    workerCall,
    workerCallExpecting,
    pollRows,
    workerPollRows,
    workerPollData,
  } = session

  const erpListQuery = (query: JsonRecord = {}): JsonRecord => ({
    organizationId,
    environmentId,
    skip: 0,
    take: 10,
    ...query,
  })

  const { provePageSafely, proveWmsPageSafely } = createWalkthroughPageProofs({
    adminRuntime,
    workerRuntime,
    screenshotDirectory,
    uiEvidence,
    markFailure,
    baseURL: baseURL!,
  })

  try {
    ;({ organizationId, environmentId, principalId, workerPrincipalId } = await session.login(
      adminPassword!,
      workerPassword!,
    ))

    // The seed is intentionally read-only here. The test proves the reserved facts exist and never
    // creates or overwrites an approval template; CreatePurchaseOrderCommand starts the seeded chain.
    const rfq = await call(
      'GET',
      queryPath('/api/business-console/v1/erp/procurement/rfqs', {
        organizationId,
        environmentId,
        keyword: RFQ_NO,
        skip: 0,
        take: 100,
      }),
    )
    const rfqRow = rowsOf(rfq.payload).find((row) => textOf(row.rfqNo) === RFQ_NO)
    if (!rfqRow) throw new Error(`Seed RFQ ${RFQ_NO} was not returned by the public facade.`)
    const supplierQuotes = await call(
      'GET',
      queryPath('/api/business-console/v1/erp/procurement/supplier-quotations', {
        organizationId,
        environmentId,
        rfqNo: RFQ_NO,
        keyword: SUPPLIER_QUOTATION_NO,
        skip: 0,
        take: 100,
      }),
    )
    const supplierQuote = rowsOf(supplierQuotes.payload).find(
      (row) => textOf(row.quotationNo) === SUPPLIER_QUOTATION_NO,
    )
    if (!supplierQuote)
      throw new Error(
        `Seed supplier quotation ${SUPPLIER_QUOTATION_NO} was not returned by the public facade.`,
      )
    const quoteLine = asRecord((Array.isArray(supplierQuote.lines) ? supplierQuote.lines : [])[0])
    const supplierCode = textOf(supplierQuote.supplierCode)
    const materialSku = textOf(quoteLine.skuCode)
    const materialUom = textOf(quoteLine.uomCode)
    const materialQuantity = Number(quoteLine.quantity ?? 0)
    const materialUnitPrice = Number(quoteLine.unitPrice ?? 0)
    if (
      !supplierCode ||
      !materialSku ||
      !materialUom ||
      materialQuantity <= 0 ||
      materialUnitPrice <= 0
    ) {
      throw new Error(
        `Seed supplier quotation ${SUPPLIER_QUOTATION_NO} did not expose a complete line.`,
      )
    }
    const rfqUi = await provePageSafely('rfq-supplier-quotation', {
      route: '/erp/procurement/rfqs',
      listPath: '/api/business-console/v1/erp/procurement/rfqs',
      filterLabel: 'RFQ 关键字',
      expectedListQuery: erpListQuery(),
      stableText: RFQ_NO,
      emptyText: '还没有询价单。可从采购申请或供应商策略发起真实询价。',
      screenshotName: '01-rfq.png',
    })
    const quoteUi = await provePageSafely('rfq-supplier-quotation', {
      route: '/erp/procurement/supplier-quotations',
      listPath: '/api/business-console/v1/erp/procurement/supplier-quotations',
      filterLabel: '供应商报价关键字',
      expectedListQuery: erpListQuery(),
      stableText: SUPPLIER_QUOTATION_NO,
      emptyText: '还没有供应商报价。先在询价单页面发起询价，供应商回价后在此汇总比价。',
      screenshotName: '02-supplier-quotation.png',
    })
    record({
      node: 'rfq-supplier-quotation',
      sourceObject: RFQ_NO,
      downstreamObject: SUPPLIER_QUOTATION_NO,
      stableKey: `${RFQ_NO} -> ${SUPPLIER_QUOTATION_NO}`,
      automationMode: 'automatic',
      request: supplierQuotes.summary,
      responseOrLog: {
        rfq: publicJson(rfqRow),
        supplierQuotation: publicJson(supplierQuote),
        ui: [rfqUi, quoteUi],
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '浏览器页面以 HTTP 200 返回 RFQ 与供应商报价，并渲染了稳定业务编号行；报价事实来自隔离 walkthrough seed。',
      responsibilityIssue: null,
    })

    const purchaseOrderRequest = {
      organizationId,
      environmentId,
      purchaseOrderNo: PURCHASE_ORDER_NO,
      supplierCode,
      siteCode: SITE_CODE,
      lines: [
        {
          lineNo: textOf(quoteLine.lineNo || '10'),
          skuCode: materialSku,
          uomCode: materialUom,
          quantity: materialQuantity,
          unitPrice: materialUnitPrice,
          promisedDate: textOf(quoteLine.promisedDate || '2099-12-31'),
        },
      ],
      idempotencyKey: `issue1912-${PURCHASE_ORDER_NO}`,
    }
    const purchaseOrder = await call(
      'POST',
      '/api/business-console/v1/erp/procurement/purchase-orders',
      purchaseOrderRequest,
    )
    setup.push({ request: purchaseOrder.summary, response: purchaseOrder.publicPayload })
    const purchaseOrderId = textOf(asRecord(dataOf(purchaseOrder.payload)).purchaseOrderId)
    if (!purchaseOrderId)
      throw new Error(`Purchase order ${PURCHASE_ORDER_NO} did not return an ID.`)
    const poUi = await provePageSafely('supplier-quotation-purchase-order', {
      route: '/erp/procurement/purchase-orders',
      listPath: '/api/business-console/v1/erp/procurement/purchase-orders',
      filterLabel: '采购订单关键字',
      expectedListQuery: erpListQuery(),
      stableText: PURCHASE_ORDER_NO,
      emptyText: '还没有采购订单。已批准的供应商报价或采购申请转单后会在这里出现。',
      screenshotName: '03-purchase-order-pending.png',
    })
    record({
      node: 'supplier-quotation-purchase-order',
      sourceObject: SUPPLIER_QUOTATION_NO,
      downstreamObject: PURCHASE_ORDER_NO,
      stableKey: `${SUPPLIER_QUOTATION_NO} -> ${PURCHASE_ORDER_NO}`,
      automationMode: 'manual',
      request: purchaseOrder.summary,
      responseOrLog: { purchaseOrder: purchaseOrder.publicPayload, ui: poUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '通过公开 ERP 采购接口以供应商报价行创建了固定采购订单，随后将用真实审批链释放。',
      responsibilityIssue: null,
    })

    const pendingApproval = await pollRows(
      '/api/business-console/v1/approval/chains',
      {
        organizationId,
        environmentId,
        status: 'pending',
        sourceService: 'business-erp',
        documentType: 'purchase-order',
        documentId: PURCHASE_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.documentId) === PURCHASE_ORDER_NO &&
        textOf(row.status).toLowerCase() === 'pending',
    )
    const chainId = textOf(pendingApproval.match.chainId)
    if (!chainId)
      throw new Error(`Purchase order ${PURCHASE_ORDER_NO} did not expose an approval chain.`)
    const approvalTemplateCode = textOf(pendingApproval.match.templateCode)
    const approvalTemplateVersion = Number(pendingApproval.match.templateVersion)
    if (
      approvalTemplateCode !== PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE ||
      approvalTemplateVersion !== PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION
    ) {
      throw new Error(
        `Purchase order ${PURCHASE_ORDER_NO} matched unexpected approval template ${approvalTemplateCode}@${approvalTemplateVersion}.`,
      )
    }
    const approvalChainDetail = await call(
      'GET',
      queryPath(`/api/business-console/v1/approval/chains/${encodeURIComponent(chainId)}`, {
        organizationId,
        environmentId,
      }),
    )
    const approvalChain = asRecord(dataOf(approvalChainDetail.payload))
    if (
      textOf(approvalChain.templateCode) !== PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE ||
      Number(approvalChain.templateVersion) !== PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION ||
      textOf(approvalChain.sourceService) !== 'business-erp' ||
      textOf(approvalChain.documentType) !== 'purchase-order' ||
      textOf(approvalChain.documentId) !== PURCHASE_ORDER_NO
    ) {
      throw new Error(
        `Approval chain ${chainId} did not preserve the seeded purchase-order-release identity.`,
      )
    }
    const approvalStep = (Array.isArray(approvalChain.steps) ? approvalChain.steps : [])
      .map(asRecord)
      .find((step) => Number(step.stepNo) === 1)
    if (
      !approvalStep ||
      textOf(approvalStep.stepName).trim() === '' ||
      textOf(approvalStep.approverType) !== 'user' ||
      textOf(approvalStep.approverRef) !== 'user-admin' ||
      textOf(approvalStep.status).toLowerCase() !== 'pending'
    ) {
      throw new Error(
        `Approval chain ${chainId} did not expose the seeded user-admin step 1 as pending.`,
      )
    }
    const approvalDecision = await call(
      'POST',
      queryPath(
        `/api/business-console/v1/approval/chains/${encodeURIComponent(chainId)}/steps/1/resolve`,
        {
          organizationId,
          environmentId,
        },
      ),
      {
        organizationId,
        environmentId,
        actorType: adminRuntime.principalType,
        actorRef: principalId,
        decision: 'approve',
        comment: 'NERV-1127 real-machine walkthrough approval',
      },
    )
    const releasedOrder = await pollRows(
      '/api/business-console/v1/erp/procurement/purchase-orders',
      {
        organizationId,
        environmentId,
        keyword: PURCHASE_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.purchaseOrderNo) === PURCHASE_ORDER_NO &&
        textOf(row.status).toLowerCase() === 'released',
    )
    const approvalUi = await provePageSafely('purchase-order-approval', {
      route: `/approval?sourceService=business-erp&documentType=purchase-order&documentId=${encodeURIComponent(PURCHASE_ORDER_NO)}`,
      listPath: '/api/business-console/v1/approval/chains',
      stableText: PURCHASE_ORDER_NO,
      tabText: /审批中的单据/,
      emptyText: '还没有审批链。审批模板匹配后，业务单据会在这里留下流程实例。',
      screenshotName: '04-purchase-order-approval.png',
    })
    const releasedPoUi = await provePageSafely('purchase-order-approval', {
      route: '/erp/procurement/purchase-orders',
      listPath: '/api/business-console/v1/erp/procurement/purchase-orders',
      filterLabel: '采购订单关键字',
      expectedListQuery: erpListQuery(),
      stableText: PURCHASE_ORDER_NO,
      emptyText: '还没有采购订单。已批准的供应商报价或采购申请转单后会在这里出现。',
      screenshotName: '05-purchase-order-released.png',
    })
    record({
      node: 'purchase-order-approval',
      sourceObject: PURCHASE_ORDER_NO,
      downstreamObject: chainId,
      stableKey: `${PURCHASE_ORDER_NO} -> ${chainId} -> approved -> released`,
      automationMode: 'manual',
      request: pendingApproval.call.summary,
      responseOrLog: {
        chain: publicJson(pendingApproval.match),
        templateCode: approvalTemplateCode,
        templateVersion: approvalTemplateVersion,
        chainDetail: approvalChainDetail.publicPayload,
        step: publicJson(approvalStep),
        decision: approvalDecision.publicPayload,
        releasedOrder: publicJson(releasedOrder.match),
        ui: [approvalUi, releasedPoUi],
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '采购订单走过 ERP 创建的真实 purchase-order-release 审批模板，公开审批中心返回批准后订单行显示 released；测试没有写入或覆盖模板。',
      responsibilityIssue: null,
    })

    const receiptRequest = {
      organizationId,
      environmentId,
      purchaseReceiptNo: PURCHASE_RECEIPT_NO,
      purchaseOrderNo: PURCHASE_ORDER_NO,
      lines: [
        {
          purchaseOrderLineNo: textOf(quoteLine.lineNo || '10'),
          receivedQuantity: materialQuantity,
          qualityStatus: 'unrestricted',
        },
      ],
      idempotencyKey: `issue1912-${PURCHASE_RECEIPT_NO}`,
    }
    const receipt = await call(
      'POST',
      '/api/business-console/v1/erp/procurement/purchase-receipts',
      receiptRequest,
    )
    setup.push({ request: receipt.summary, response: receipt.publicPayload })
    const receiptOrder = await pollRows(
      '/api/business-console/v1/erp/procurement/purchase-orders',
      {
        organizationId,
        environmentId,
        keyword: PURCHASE_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.purchaseOrderNo) === PURCHASE_ORDER_NO &&
        (Array.isArray(row.lines) ? row.lines : []).some((line) => {
          const item = asRecord(line)
          return (
            textOf(item.lineNo) === textOf(quoteLine.lineNo || '10') &&
            Number(item.receivedQuantity ?? 0) >= materialQuantity
          )
        }),
    )
    const receiptUi = await provePageSafely('purchase-order-receipt', {
      route: '/erp/procurement/receipts',
      listPath: '/api/business-console/v1/erp/procurement/purchase-orders',
      filterLabel: '采购收货关键字',
      expectedListQuery: erpListQuery(),
      stableText: PURCHASE_ORDER_NO,
      emptyText: '还没有可收货的采购订单。采购订单释放后会在这里跟进入库。',
      screenshotName: '06-purchase-receipt.png',
    })
    record({
      node: 'purchase-order-receipt',
      sourceObject: PURCHASE_ORDER_NO,
      downstreamObject: PURCHASE_RECEIPT_NO,
      stableKey: `${PURCHASE_ORDER_NO} -> ${PURCHASE_RECEIPT_NO}`,
      automationMode: 'manual',
      request: receipt.summary,
      responseOrLog: {
        receipt: receipt.publicPayload,
        purchaseOrder: publicJson(receiptOrder.match),
        ui: receiptUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '采购收货公开接口以固定 PR-WALK-001 记账，采购收货页面以 HTTP 200 渲染同一 PO 行和已收数量。',
      responsibilityIssue: null,
    })

    const receiptScopes = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/wms/work-scopes/receipts', {
        organizationId,
        environmentId,
      }),
    )
    const receiptScope: AuthorizedWorkPoolScope | undefined = selectAuthorizedWorkPoolScope(
      receiptScopes.payload,
      SITE_CODE,
    )
    const receiptReadScope: AuthorizedWorkSiteScope | undefined = selectAuthorizedWorkSiteScope(
      receiptScopes.payload,
      SITE_CODE,
    )
    if (!receiptScope || !receiptReadScope)
      throw new Error('WMS receipt scope catalog returned no authorized read or work-pool scope.')
    expect(textOf(asRecord(dataOf(receiptScopes.payload)).actorPrincipalId)).toBe(
      workerRuntime.principalId,
    )
    const receiptScopeKind = textOf(receiptScope.scopeKind).trim()
    const receiptScopeId = textOf(receiptScope.scopeId).trim()
    const receiptPoolCode = textOf(receiptScope.poolCode).trim()
    const receiptReadScopeKind = textOf(receiptReadScope.scopeKind).trim()
    const receiptReadScopeId = textOf(receiptReadScope.scopeId).trim()
    const receiptReadSiteCode = textOf(receiptReadScope.siteCode).trim()
    expect(receiptPoolCode).not.toBe('')
    expect(textOf(receiptScope.siteCode)).toBe(SITE_CODE)
    expect(receiptReadScopeKind.toLowerCase()).toBe('site')
    expect(receiptReadScopeId).toBe(receiptReadSiteCode)
    expect(receiptReadSiteCode).toBe(SITE_CODE)
    setup.push({
      kind: 'wms-scope-catalog',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'receipts',
      source: 'authorized WarehouseWorkScopeCatalogItem',
      scope: publicJson(receiptScope),
      readScope: publicJson(receiptReadScope),
      request: receiptScopes.summary,
    })
    const inbound = await workerCall('POST', '/api/business-console/v1/wms/inbound-orders', {
      organizationId,
      environmentId,
      inboundOrderNo: INBOUND_ORDER_NO,
      sourceDocumentType: 'purchase-order',
      sourceDocumentId: PURCHASE_ORDER_NO,
      siteCode: SITE_CODE,
      lines: [
        {
          lineNo: textOf(quoteLine.lineNo || '10'),
          skuCode: materialSku,
          uomCode: materialUom,
          receivedQuantity: materialQuantity,
          stagingLocationCode: INBOUND_LOCATION,
          lotNo: 'LOT-WALK-RM-001',
          serialNo: null,
          qualityStatus: 'unrestricted',
          ownerType: 'company',
          ownerId: null,
        },
      ],
    })
    const inboundOrderId = textOf(asRecord(dataOf(inbound.payload)).inboundOrderId)
    if (!inboundOrderId) throw new Error(`WMS inbound ${INBOUND_ORDER_NO} did not return an ID.`)
    const inboundRow = await workerPollRows(
      '/api/business-console/v1/wms/inbound-orders',
      {
        organizationId,
        environmentId,
        keyword: INBOUND_ORDER_NO,
        scopeKind: receiptReadScopeKind,
        scopeId: receiptReadScopeId,
        siteCode: receiptReadSiteCode,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.inboundOrderNo) === INBOUND_ORDER_NO,
    )
    const inboundVersion = Number(inboundRow.match.version ?? 1)
    const noScopeInventoryQuery = {
      organizationId,
      environmentId,
      skuCode: materialSku,
      uomCode: materialUom,
      siteCode: SITE_CODE,
      locationCode: LINE_SIDE_LOCATION,
      lotNo: 'LOT-WALK-RM-001',
      qualityStatus: 'unrestricted',
      ownerType: 'company',
    }
    const inventoryBeforeNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/availability', noScopeInventoryQuery),
    )
    const movementsBeforeNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/movements', {
        organizationId,
        environmentId,
        sourceDocumentId: INBOUND_ORDER_NO,
        page: 1,
        pageSize: 100,
      }),
    )
    const noScopeCompletion = await workerCallExpecting(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
        { organizationId, environmentId },
      ),
      {
        idempotencyKey: `issue1912-${INBOUND_ORDER_NO}-missing-scope`,
        lines: [{ lineNo: textOf(quoteLine.lineNo || '10'), lotNo: 'LOT-WALK-RM-001' }],
        expectedVersion: inboundVersion,
      },
      { code: 'missing-work-pool-assignment', message: 'missing-work-pool-assignment' },
    )
    const inboundAfterNoScope = await workerPollRows(
      '/api/business-console/v1/wms/inbound-orders',
      {
        organizationId,
        environmentId,
        keyword: INBOUND_ORDER_NO,
        scopeKind: receiptReadScopeKind,
        scopeId: receiptReadScopeId,
        siteCode: receiptReadSiteCode,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.inboundOrderNo) === INBOUND_ORDER_NO,
    )
    expect(Number(inboundAfterNoScope.match.version ?? 0)).toBe(inboundVersion)
    expect(textOf(inboundAfterNoScope.match.status).toLowerCase()).not.toBe('completed')
    const inventoryAfterNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/availability', noScopeInventoryQuery),
    )
    const movementsAfterNoScope = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/inventory/movements', {
        organizationId,
        environmentId,
        sourceDocumentId: INBOUND_ORDER_NO,
        page: 1,
        pageSize: 100,
      }),
    )
    const inventoryBeforeNoScopeFingerprint = inventoryStateFingerprint(
      inventoryBeforeNoScope.payload,
    )
    const inventoryAfterNoScopeFingerprint = inventoryStateFingerprint(
      inventoryAfterNoScope.payload,
    )
    const movementsBeforeNoScopeFingerprint = inventoryMovementFingerprint(
      movementsBeforeNoScope.payload,
    )
    const movementsAfterNoScopeFingerprint = inventoryMovementFingerprint(
      movementsAfterNoScope.payload,
    )
    expect(inventoryAfterNoScopeFingerprint).toEqual(inventoryBeforeNoScopeFingerprint)
    expect(movementsAfterNoScopeFingerprint).toEqual(movementsBeforeNoScopeFingerprint)
    const noScopeSideEffectProbe = {
      unchanged:
        JSON.stringify(inventoryAfterNoScopeFingerprint) ===
          JSON.stringify(inventoryBeforeNoScopeFingerprint) &&
        JSON.stringify(movementsAfterNoScopeFingerprint) ===
          JSON.stringify(movementsBeforeNoScopeFingerprint),
      inventoryAvailability: {
        path: inventoryBeforeNoScope.summary.path,
        before: inventoryBeforeNoScope.publicPayload,
        after: inventoryAfterNoScope.publicPayload,
        beforeFingerprint: inventoryBeforeNoScopeFingerprint,
        afterFingerprint: inventoryAfterNoScopeFingerprint,
      },
      inventoryMovements: {
        path: movementsBeforeNoScope.summary.path,
        before: movementsBeforeNoScope.publicPayload,
        after: movementsAfterNoScope.publicPayload,
        beforeFingerprint: movementsBeforeNoScopeFingerprint,
        afterFingerprint: movementsAfterNoScopeFingerprint,
      },
    }
    setup.push({
      kind: 'wms-no-scope-fail-closed',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'inbound-complete',
      request: noScopeCompletion.summary,
      response: noScopeCompletion.publicPayload,
      publicError: noScopeCompletion.publicError,
      before: { version: inboundVersion, status: textOf(inboundRow.match.status) },
      after: {
        version: Number(inboundAfterNoScope.match.version ?? 0),
        status: textOf(inboundAfterNoScope.match.status),
      },
      sideEffect: false,
      sideEffectProbe: noScopeSideEffectProbe,
      scope: 'not-supplied',
    })
    const inboundAssignmentPlan = buildAuthorizedWorkPoolAssignment(
      {
        actor: workerRuntime.actor,
        principalId: workerRuntime.principalId,
      },
      receiptScope,
      inboundOrderId,
      `issue1912-${INBOUND_ORDER_NO}-assignment`,
      inboundVersion,
    )
    if (!inboundAssignmentPlan.called) {
      throw new Error(
        `WMS inbound ${INBOUND_ORDER_NO} cannot be assigned without an authorized work-pool scope: ${inboundAssignmentPlan.reason}`,
      )
    }
    const inboundAssignment = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundAssignmentPlan.request.resourceId)}/assignment`,
        { organizationId, environmentId },
      ),
      inboundAssignmentPlan.request.body,
    )
    const inboundAssignmentData = asRecord(dataOf(inboundAssignment.payload))
    expect(textOf(inboundAssignmentData.resourceCategory)).toBe('inbound')
    expect(textOf(inboundAssignmentData.resourceId)).toBe(inboundOrderId)
    expect(textOf(inboundAssignmentData.siteCode)).toBe(receiptScope.siteCode)
    expect(textOf(inboundAssignmentData.poolCode)).toBe(receiptPoolCode)
    expect(textOf(inboundAssignmentData.operatorPrincipalId)).toBe(workerRuntime.principalId)
    expect(textOf(inboundAssignmentData.assignedByPrincipalId)).toBe(workerRuntime.principalId)
    const assignedInbound = await workerPollRows(
      '/api/business-console/v1/wms/inbound-orders',
      {
        organizationId,
        environmentId,
        keyword: INBOUND_ORDER_NO,
        scopeKind: receiptScopeKind,
        scopeId: receiptScopeId,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.inboundOrderNo) === INBOUND_ORDER_NO &&
        textOf(row.assignedPoolCode) === receiptPoolCode &&
        textOf(row.assignedOperatorUserId) === workerRuntime.principalId,
    )
    const assignedInboundVersion = Number(assignedInbound.match.version ?? 0)
    expect(assignedInboundVersion).toBeGreaterThan(inboundVersion)
    setup.push({
      kind: 'wms-assignment',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'inbound-order',
      resourceId: inboundOrderId,
      scope: publicJson(receiptScope),
      request: inboundAssignment.summary,
      response: inboundAssignment.publicPayload,
      bound: {
        poolCode: textOf(assignedInbound.match.assignedPoolCode),
        operatorPrincipalId: textOf(assignedInbound.match.assignedOperatorUserId),
        version: assignedInboundVersion,
      },
    })
    const putaway = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/putaway-tasks`,
        { organizationId, environmentId },
      ),
      {
        taskNo: PUTAWAY_TASK_NO,
        lineNo: textOf(quoteLine.lineNo || '10'),
        fromLocationCode: INBOUND_LOCATION,
        toLocationCode: LINE_SIDE_LOCATION,
        quantity: materialQuantity,
      },
    )
    const completedInbound = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/inbound-orders/${encodeURIComponent(inboundOrderId)}/complete`,
        { organizationId, environmentId },
      ),
      {
        idempotencyKey: `issue1912-${INBOUND_ORDER_NO}-complete`,
        lines: [{ lineNo: textOf(quoteLine.lineNo || '10'), lotNo: 'LOT-WALK-RM-001' }],
        scopeKind: receiptScopeKind,
        scopeId: receiptScopeId,
        expectedVersion: assignedInboundVersion,
      },
    )
    const inventory = await workerPollData(
      '/api/business-console/v1/inventory/availability',
      {
        organizationId,
        environmentId,
        skuCode: materialSku,
        uomCode: materialUom,
        siteCode: SITE_CODE,
        locationCode: LINE_SIDE_LOCATION,
        lotNo: 'LOT-WALK-RM-001',
        qualityStatus: 'unrestricted',
        ownerType: 'company',
      },
      (data) => Number(data.availableQuantity ?? data.onHandQuantity ?? 0) >= materialQuantity,
    )
    const inboundQueryFacts = {
      organizationId,
      environmentId,
      scopeKind: receiptScopeKind,
      scopeId: receiptScopeId,
      siteCode: receiptReadSiteCode,
      keyword: INBOUND_ORDER_NO,
      pageWindow: NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT,
    }
    const inboundKeywordQuery = buildWmsInboundListQueryFacts(inboundQueryFacts)
    const inboundListQuery = buildWmsInboundSelectionQueryFacts(inboundQueryFacts)
    const inboundUi = await proveWmsPageSafely('receipt-inbound-inventory', {
      actor: 'wms-worker',
      route: '/wms/inbound',
      filterLabel: '关键字搜索',
      emptyText: '暂无入库单。收货作业产生入库单后会出现在这里。',
      screenshotName: '07-wms-inbound.png',
      wms: {
        kind: 'inbound',
        selection: {
          scope: {
            label: '作业范围',
            option: receiptScope.displayName,
            scopeKind: inboundListQuery.scopeKind,
            scopeId: inboundListQuery.scopeId,
          },
          site: { label: '工厂', optionCode: inboundListQuery.siteCode },
        },
        pageWindow: inboundQueryFacts.pageWindow,
        query: {
          kind: 'inbound',
          listPath: '/api/business-console/v1/wms/inbound-orders',
          selectionQuery: inboundListQuery,
          keywordQuery: inboundKeywordQuery,
          forbiddenQueryKeys: [],
        },
      },
    })
    record({
      node: 'receipt-inbound-inventory',
      sourceObject: PURCHASE_RECEIPT_NO,
      downstreamObject: INBOUND_ORDER_NO,
      stableKey: `${PURCHASE_RECEIPT_NO} -> ${INBOUND_ORDER_NO} -> ${textOf(inventory.data.movementId ?? inventory.data.ledgerVersion)}`,
      automationMode: 'mixed',
      request: inbound.summary,
      responseOrLog: {
        inbound: inbound.publicPayload,
        putaway: putaway.publicPayload,
        completion: completedInbound.publicPayload,
        inventory: publicJson(inventory.data),
        ui: inboundUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '真实 WMS 入库单在授权作业范围内完成，页面渲染 IN-WALK-001，库存公开可用量随后在 SITE-001/loc-line-01 出现。',
      responsibilityIssue: null,
    })

    const salesQuotation = await call(
      'GET',
      queryPath('/api/business-console/v1/erp/sales/quotations', {
        organizationId,
        environmentId,
        keyword: SALES_QUOTATION_NO,
        skip: 0,
        take: 100,
      }),
    )
    const quotationRow = rowsOf(salesQuotation.payload).find(
      (row) => textOf(row.quotationNo) === SALES_QUOTATION_NO,
    )
    if (!quotationRow || textOf(quotationRow.status).toLowerCase() !== 'approved')
      throw new Error(`Seed sales quotation ${SALES_QUOTATION_NO} was not approved.`)
    const salesQuotationUi = await provePageSafely('sales-quotation-sales-order', {
      route: '/erp/sales/quotations',
      listPath: '/api/business-console/v1/erp/sales/quotations',
      filterLabel: '报价单关键字',
      expectedListQuery: erpListQuery(),
      stableText: SALES_QUOTATION_NO,
      emptyText: '还没有报价单。可从销售机会或客户需求创建报价。',
      screenshotName: '08-sales-quotation.png',
    })
    const salesOrder = await call('POST', '/api/business-console/v1/erp/sales/sales-orders', {
      organizationId,
      environmentId,
      salesOrderNo: SALES_ORDER_NO,
      quotationNo: SALES_QUOTATION_NO,
      siteCode: SITE_CODE,
      idempotencyKey: `issue1912-${SALES_ORDER_NO}`,
    })
    const salesOrderRow = await pollRows(
      '/api/business-console/v1/erp/sales/sales-orders',
      {
        organizationId,
        environmentId,
        keyword: SALES_ORDER_NO,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.salesOrderNo) === SALES_ORDER_NO,
    )
    const salesOrderUi = await provePageSafely('sales-quotation-sales-order', {
      route: `/erp/sales/orders?keyword=${encodeURIComponent(SALES_ORDER_NO)}`,
      listPath: '/api/business-console/v1/erp/sales/sales-orders',
      filterLabel: '销售订单关键字',
      filterResponseMode: 'server',
      expectedListQuery: erpListQuery(),
      stableText: SALES_ORDER_NO,
      emptyText: '还没有销售订单。批准报价后可在这里生成订单。',
      screenshotName: '09-sales-order.png',
    })
    record({
      node: 'sales-quotation-sales-order',
      sourceObject: SALES_QUOTATION_NO,
      downstreamObject: SALES_ORDER_NO,
      stableKey: `${SALES_QUOTATION_NO} -> ${SALES_ORDER_NO}`,
      automationMode: 'manual',
      request: salesOrder.summary,
      responseOrLog: {
        quotation: publicJson(quotationRow),
        salesOrder: publicJson(salesOrderRow.match),
        ui: [salesQuotationUi, salesOrderUi],
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '销售报价页面证明 QUO-WALK-001 已批准，销售订单页面以 HTTP 200 渲染稳定 SO-WALK-001 行。',
      responsibilityIssue: null,
    })

    const demand = await pollRows(
      '/api/business-console/v1/planning/demands',
      { organizationId, environmentId },
      (row) => textOf(row.sourceReference) === SALES_ORDER_NO,
    )
    const demandUi = await provePageSafely('sales-order-demand', {
      route: '/planning',
      listPath: '/api/business-console/v1/planning/demands',
      filterLabel: '需求池关键字',
      filterResponseMode: 'client',
      stableText: SALES_ORDER_NO,
      emptyText: '当前范围没有计划需求。',
      screenshotName: '10-planning-demand.png',
    })
    record({
      node: 'sales-order-demand',
      sourceObject: SALES_ORDER_NO,
      downstreamObject: textOf(demand.match.demandSourceId),
      stableKey: `${SALES_ORDER_NO} -> ${textOf(demand.match.demandSourceId)}`,
      automationMode: 'automatic',
      request: demand.call.summary,
      responseOrLog: { demand: publicJson(demand.match), ui: demandUi },
      conclusion: 'runtime-confirmed',
      demoWording: '销售订单跨 Redis 后在 Planning 页面需求池出现同一 SO-WALK-001 来源行。',
      responsibilityIssue: null,
    })

    const horizonStart = dateOnly(new Date(generatedAtUtc.getTime() - 86_400_000))
    const mrp = await call('POST', '/api/business-console/v1/planning/mrp-runs', {
      organizationId,
      environmentId,
      horizonStart,
      horizonEnd: '2100-01-01',
    })
    const runId = textOf(asRecord(dataOf(mrp.payload)).runId)
    if (!runId) throw new Error('MRP run did not return a runId.')
    const pegging = await pollRows(
      `/api/business-console/v1/planning/mrp-runs/${encodeURIComponent(runId)}/pegging`,
      {
        organizationId,
        environmentId,
      },
      (row) => textOf(row.demandSourceReference) === SALES_ORDER_NO,
      120_000,
    )
    const suggestion = await pollRows(
      '/api/business-console/v1/planning/suggestions',
      { organizationId, environmentId },
      (row) =>
        textOf(row.runId) === runId &&
        textOf(row.suggestionType) === 'planned-work-order' &&
        textOf(row.skuCode) === FINISHED_SKU,
      120_000,
    )
    const suggestionUi = await provePageSafely('demand-mrp-suggestion', {
      route: '/planning',
      listPath: '/api/business-console/v1/planning/suggestions',
      stableText: FINISHED_SKU,
      tabText: /计划建议/,
      reuseCurrentRoute: true,
      refreshListBeforeProof: true,
      emptyText: '当前范围没有计划建议。',
      screenshotName: '11-planning-suggestion.png',
    })
    record({
      node: 'demand-mrp-suggestion',
      sourceObject: SALES_ORDER_NO,
      downstreamObject: textOf(suggestion.match.suggestionId),
      stableKey: `${SALES_ORDER_NO} -> ${runId} -> ${FINISHED_SKU}`,
      automationMode: 'manual',
      request: mrp.summary,
      responseOrLog: {
        mrp: mrp.publicPayload,
        pegging: publicJson(pegging.match),
        suggestion: publicJson(suggestion.match),
        ui: suggestionUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        'MRP 使用包含 2099-12-31 种子需求日期的公开窗口，保留 SO-WALK-001 pegging 并在真实计划建议页展示成品生产建议。',
      responsibilityIssue: null,
    })

    const mesReadContext = await call(
      'GET',
      queryPath('/api/business-console/v1/me/work-context', {
        organizationId,
        environmentId,
        permissionCode: 'business.mes.work-orders.read',
      }),
    )
    const mesReadData = asRecord(dataOf(mesReadContext.payload))
    const mesReadScope = asRecord(
      mesReadData.selectedScope ??
        (Array.isArray(mesReadData.authorizedScopes) ? mesReadData.authorizedScopes[0] : null),
    )
    const mesScopeKind = textOf(mesReadScope.kind ?? mesReadScope.scopeKind)
    const mesScopeId = textOf(mesReadScope.id ?? mesReadScope.scopeId)
    if (!mesScopeKind || !mesScopeId)
      throw new Error('MES work-order read context returned no authorized scope.')
    const accepted = await call(
      'POST',
      queryPath(
        `/api/business-console/v1/planning/suggestions/${encodeURIComponent(textOf(suggestion.match.suggestionId))}/accept`,
        { organizationId, environmentId },
      ),
      {
        downstreamService: 'BusinessMes',
        downstreamDocumentType: 'WorkOrder',
        downstreamDocumentId: null,
        idempotencyKey: `issue1912-accept-${textOf(suggestion.match.suggestionId)}`,
      },
    )
    const acceptedData = asRecord(dataOf(accepted.payload))
    const workOrderId = textOf(acceptedData.downstreamDocumentId)
    if (!workOrderId)
      throw new Error('Planning suggestion acceptance returned no MES work-order ID.')
    const workOrderDetail = await call(
      'GET',
      queryPath(`/api/business-console/v1/mes/work-orders/${encodeURIComponent(workOrderId)}`, {
        organizationId,
        environmentId,
        scopeKind: mesScopeKind,
        scopeId: mesScopeId,
      }),
    )
    const workOrder = asRecord(dataOf(workOrderDetail.payload))
    const sourcePlanReference = asRecord(workOrder.sourcePlanReference)
    if (textOf(sourcePlanReference.sourceDemandReference) !== SALES_ORDER_NO)
      throw new Error(`MES work order ${workOrderId} did not preserve ${SALES_ORDER_NO}.`)
    const workOrderNo = textOf(workOrder.workOrderNo || workOrderId)
    const workOrderUi = await provePageSafely('mrp-suggestion-mes-work-order', {
      route: `/mes/work-orders?keyword=${encodeURIComponent(workOrderNo)}`,
      listPath: '/api/business-console/v1/mes/work-orders',
      stableText: workOrderNo,
      emptyText: '当前筛选下没有工单。正常生产请先进入生产计划转工单，急单只处理临时插单。',
      screenshotName: '12-mes-work-order.png',
    })
    record({
      node: 'mrp-suggestion-mes-work-order',
      sourceObject: textOf(suggestion.match.suggestionId),
      downstreamObject: workOrderId,
      stableKey: `${SALES_ORDER_NO} -> ${workOrderNo} -> ${workOrderId}`,
      automationMode: 'automatic',
      request: accepted.summary,
      responseOrLog: { workOrder: publicJson(workOrder), ui: workOrderUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '计划建议接受后真实 MES 工单页渲染了生成的工单业务号，并通过详情公开关联回 SO-WALK-001。',
      responsibilityIssue: null,
    })

    const finishedProduction = await runFinishedProduction({
      browser,
      workOrderId,
      producedLotNo: PRODUCED_LOT_NO,
      evidencePath: join(evidenceDirectory, 'nerv1853-finished-production.json'),
    })
    const productionReports = await pollRows(
      '/api/business-console/v1/mes/production-reports',
      { organizationId, environmentId, keyword: workOrderNo, skip: 0, take: 100 },
      (row) =>
        textOf(row.workOrderId) === workOrderId && textOf(row.producedLotNo) === PRODUCED_LOT_NO,
      120_000,
    )
    const productionUi = await provePageSafely('mes-work-order-production', {
      route: '/mes/production-reports',
      listPath: '/api/business-console/v1/mes/production-reports',
      stableText: workOrderNo,
      emptyText: '还没有报工记录。报工后这里会出现对应记录，去工序执行报工。',
      screenshotName: '13-mes-production-reports.png',
    })
    record({
      node: 'mes-work-order-production',
      sourceObject: workOrderNo,
      downstreamObject: textOf(
        productionReports.match.reportNo ?? productionReports.match.productionReportId,
      ),
      stableKey: `${SALES_ORDER_NO} -> ${workOrderNo} -> ${PRODUCED_LOT_NO}`,
      automationMode: 'mixed',
      request: null,
      responseOrLog: {
        reports: publicJson(productionReports.match),
        finalProduction: finishedProduction,
        ui: productionUi,
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        'MES 真实工单的全部工序通过公开 start/report 生命周期完成，最终报工产生固定成品批次；报工页面渲染工单业务号。',
      responsibilityIssue: null,
    })

    const finishedReceipt = await call(
      'POST',
      '/api/business-console/v1/mes/finished-goods-receipt-requests',
      {
        organizationId,
        environmentId,
        workOrderId,
        skuId: FINISHED_SKU,
        quantity: QUANTITY,
        uomCode: 'pcs',
        requestedAtUtc: new Date().toISOString(),
        idempotencyKey: `issue1912-receipt-${PRODUCED_LOT_NO}`,
        producedLotNo: PRODUCED_LOT_NO,
      },
    )
    const receiptRequestNo = textOf(asRecord(dataOf(finishedReceipt.payload)).requestNo)
    if (!receiptRequestNo)
      throw new Error(`Finished goods receipt for ${workOrderNo} did not return requestNo.`)
    const finishedReceiptRow = await pollRows(
      '/api/business-console/v1/mes/finished-goods-receipt-requests',
      { organizationId, environmentId, workOrderId, keyword: receiptRequestNo, skip: 0, take: 100 },
      (row) =>
        textOf(row.requestNo) === receiptRequestNo && textOf(row.producedLotNo) === PRODUCED_LOT_NO,
      180_000,
    )
    const receiptPageUi = await provePageSafely('production-finished-goods-receipt', {
      route: '/mes/receipts',
      listPath: '/api/business-console/v1/mes/finished-goods-receipt-requests',
      stableText: receiptRequestNo,
      emptyText: '还没有完工入库登记。末道工序报完工后，在此把成品登记入库即会出现对应记录。',
      screenshotName: '14-finished-goods-receipt.png',
    })
    const finishedInventory = await workerPollData(
      '/api/business-console/v1/inventory/availability',
      {
        organizationId,
        environmentId,
        skuCode: FINISHED_SKU,
        uomCode: 'pcs',
        siteCode: SITE_CODE,
        locationCode: FINISHED_GOODS_LOCATION,
        lotNo: PRODUCED_LOT_NO,
      },
      (data) => Number(data.onHandQuantity ?? 0) >= QUANTITY,
      180_000,
    )
    record({
      node: 'production-finished-goods-receipt',
      sourceObject: textOf(
        productionReports.match.reportNo ?? productionReports.match.productionReportId,
      ),
      downstreamObject: receiptRequestNo,
      stableKey: `${workOrderNo} -> ${receiptRequestNo} -> ${PRODUCED_LOT_NO}`,
      automationMode: 'manual',
      request: finishedReceipt.summary,
      responseOrLog: { receipt: publicJson(finishedReceiptRow.match), ui: receiptPageUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '最终报工通过公开完工入库请求登记固定批次，MES 完工入库页面以 HTTP 200 渲染 requestNo；随后 Inventory 公开可用量为正。',
      responsibilityIssue: null,
    })
    record({
      node: 'finished-goods-inventory',
      sourceObject: receiptRequestNo,
      downstreamObject: PRODUCED_LOT_NO,
      stableKey: `${receiptRequestNo} -> ${SITE_CODE}/${FINISHED_GOODS_LOCATION}/${PRODUCED_LOT_NO}`,
      automationMode: 'automatic',
      request: finishedInventory.call.summary,
      responseOrLog: {
        availability: publicJson(finishedInventory.data),
        receipt: publicJson(finishedReceiptRow.match),
        ui: await provePageSafely('finished-goods-inventory', {
          actor: 'wms-worker',
          route: queryPath('/inventory/availability', {
            skuCode: FINISHED_SKU,
            siteCode: SITE_CODE,
            locationCode: FINISHED_GOODS_LOCATION,
            lotNo: PRODUCED_LOT_NO,
          }),
          listPath: '/api/business-console/v1/inventory/availability',
          stableText: FINISHED_SKU,
          selectOptions: [
            { label: '质量状态', option: '全部状态' },
            { label: '货主类型', option: '生产领用' },
          ],
          emptyText: '没有查到库存明细。换个物料、工厂或库位再查一次。',
          screenshotName: '15-finished-goods-inventory.png',
        }),
      },
      conclusion: 'runtime-confirmed',
      demoWording:
        '完工入库跨边界落到 Inventory 的固定成品批次分区，公开 availability 证明库存为正。',
      responsibilityIssue: null,
    })

    const delivery = await call('POST', '/api/business-console/v1/erp/sales/delivery-orders', {
      organizationId,
      environmentId,
      deliveryOrderNo: DELIVERY_ORDER_NO,
      salesOrderNo: SALES_ORDER_NO,
      lines: [
        {
          salesOrderLineNo: '10',
          quantity: QUANTITY,
          locationCode: FINISHED_GOODS_LOCATION,
          lotNo: PRODUCED_LOT_NO,
        },
      ],
      idempotencyKey: `issue1912-${DELIVERY_ORDER_NO}`,
    })
    const deliveryRow = await pollRows(
      '/api/business-console/v1/erp/sales/delivery-orders',
      { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) => textOf(row.deliveryOrderNo) === DELIVERY_ORDER_NO,
    )
    const deliveryUi = await provePageSafely('sales-order-delivery', {
      route: '/erp/sales/deliveries',
      listPath: '/api/business-console/v1/erp/sales/delivery-orders',
      filterLabel: '发货关键字',
      expectedListQuery: erpListQuery(),
      stableText: DELIVERY_ORDER_NO,
      emptyText: '还没有发货单',
      screenshotName: '16-sales-delivery.png',
    })
    record({
      node: 'sales-order-delivery',
      sourceObject: SALES_ORDER_NO,
      downstreamObject: DELIVERY_ORDER_NO,
      stableKey: `${SALES_ORDER_NO} -> ${DELIVERY_ORDER_NO}`,
      automationMode: 'manual',
      request: delivery.summary,
      responseOrLog: { delivery: publicJson(deliveryRow.match), ui: deliveryUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '成品库存有正数后，公开 ERP 发货释放从同一 SO 生成固定 DO，销售发货页面渲染稳定业务编号。',
      responsibilityIssue: null,
    })

    const shipmentScopes = await workerCall(
      'GET',
      queryPath('/api/business-console/v1/wms/work-scopes/shipments', {
        organizationId,
        environmentId,
      }),
    )
    const shipmentScope: AuthorizedWorkPoolScope | undefined = selectAuthorizedWorkPoolScope(
      shipmentScopes.payload,
      SITE_CODE,
    )
    const shipmentReadScope: AuthorizedWorkSiteScope | undefined = selectAuthorizedWorkSiteScope(
      shipmentScopes.payload,
      SITE_CODE,
    )
    if (!shipmentScope || !shipmentReadScope)
      throw new Error('WMS shipment scope catalog returned no authorized read or work-pool scope.')
    expect(textOf(asRecord(dataOf(shipmentScopes.payload)).actorPrincipalId)).toBe(
      workerRuntime.principalId,
    )
    const shipmentScopeKind = textOf(shipmentScope.scopeKind).trim()
    const shipmentScopeId = textOf(shipmentScope.scopeId).trim()
    const shipmentPoolCode = textOf(shipmentScope.poolCode).trim()
    const shipmentReadScopeKind = textOf(shipmentReadScope.scopeKind).trim()
    const shipmentReadScopeId = textOf(shipmentReadScope.scopeId).trim()
    const shipmentReadSiteCode = textOf(shipmentReadScope.siteCode).trim()
    expect(shipmentPoolCode).not.toBe('')
    expect(textOf(shipmentScope.siteCode)).toBe(SITE_CODE)
    expect(shipmentReadScopeKind.toLowerCase()).toBe('site')
    expect(shipmentReadScopeId).toBe(shipmentReadSiteCode)
    expect(shipmentReadSiteCode).toBe(SITE_CODE)
    setup.push({
      kind: 'wms-scope-catalog',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'shipments',
      source: 'authorized WarehouseWorkScopeCatalogItem',
      scope: publicJson(shipmentScope),
      readScope: publicJson(shipmentReadScope),
      request: shipmentScopes.summary,
    })
    const outbound = await workerPollRows(
      '/api/business-console/v1/wms/outbound-orders',
      {
        organizationId,
        environmentId,
        keyword: DELIVERY_ORDER_NO,
        scopeKind: shipmentReadScopeKind,
        scopeId: shipmentReadScopeId,
        siteCode: shipmentReadSiteCode,
        skip: 0,
        take: 100,
      },
      (row) => textOf(row.outboundOrderNo) === DELIVERY_ORDER_NO,
      180_000,
    )
    const outboundId = textOf(outbound.match.outboundOrderId)
    const outboundVersion = Number(outbound.match.version ?? 1)
    const outboundAssignmentPlan = buildAuthorizedWorkPoolAssignment(
      {
        actor: workerRuntime.actor,
        principalId: workerRuntime.principalId,
      },
      shipmentScope,
      outboundId,
      `issue1912-${DELIVERY_ORDER_NO}-assignment`,
      outboundVersion,
    )
    if (!outboundAssignmentPlan.called) {
      throw new Error(
        `WMS outbound ${DELIVERY_ORDER_NO} cannot be assigned without an authorized work-pool scope: ${outboundAssignmentPlan.reason}`,
      )
    }
    const outboundAssignment = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(outboundAssignmentPlan.request.resourceId)}/assignment`,
        { organizationId, environmentId },
      ),
      outboundAssignmentPlan.request.body,
    )
    const outboundAssignmentData = asRecord(dataOf(outboundAssignment.payload))
    expect(textOf(outboundAssignmentData.resourceCategory)).toBe('outbound')
    expect(textOf(outboundAssignmentData.resourceId)).toBe(outboundId)
    expect(textOf(outboundAssignmentData.siteCode)).toBe(shipmentScope.siteCode)
    expect(textOf(outboundAssignmentData.poolCode)).toBe(shipmentPoolCode)
    expect(textOf(outboundAssignmentData.operatorPrincipalId)).toBe(workerRuntime.principalId)
    expect(textOf(outboundAssignmentData.assignedByPrincipalId)).toBe(workerRuntime.principalId)
    const assignedOutbound = await workerPollRows(
      '/api/business-console/v1/wms/outbound-orders',
      {
        organizationId,
        environmentId,
        keyword: DELIVERY_ORDER_NO,
        scopeKind: shipmentScopeKind,
        scopeId: shipmentScopeId,
        skip: 0,
        take: 100,
      },
      (row) =>
        textOf(row.outboundOrderNo) === DELIVERY_ORDER_NO &&
        textOf(row.assignedPoolCode) === shipmentPoolCode &&
        textOf(row.assignedOperatorUserId) === workerRuntime.principalId,
      180_000,
    )
    const assignedOutboundVersion = Number(assignedOutbound.match.version ?? 0)
    expect(assignedOutboundVersion).toBeGreaterThan(outboundVersion)
    setup.push({
      kind: 'wms-assignment',
      actor: workerRuntime.actor,
      principalId: workerRuntime.principalId,
      operation: 'outbound-order',
      resourceId: outboundId,
      scope: publicJson(shipmentScope),
      request: outboundAssignment.summary,
      response: outboundAssignment.publicPayload,
      bound: {
        poolCode: textOf(assignedOutbound.match.assignedPoolCode),
        operatorPrincipalId: textOf(assignedOutbound.match.assignedOperatorUserId),
        version: assignedOutboundVersion,
      },
    })
    const outboundQueryFacts = {
      organizationId,
      environmentId,
      scopeKind: shipmentScopeKind,
      scopeId: shipmentScopeId,
      keyword: DELIVERY_ORDER_NO,
      pageWindow: NERV_1571_WMS_DEFAULT_PAGE_WINDOW_INPUT,
    }
    const outboundKeywordQuery = buildWmsOutboundListQueryFacts(outboundQueryFacts)
    const outboundListQuery = buildWmsOutboundSelectionQueryFacts(outboundQueryFacts)
    const outboundUi = await proveWmsPageSafely('delivery-wms-outbound', {
      actor: 'wms-worker',
      route: '/wms/outbound',
      filterLabel: '关键字搜索',
      emptyText: '暂无出库单。发货作业产生出库单后会出现在这里。',
      screenshotName: '17-wms-outbound.png',
      wms: {
        kind: 'outbound',
        selection: {
          scope: {
            label: '作业范围',
            option: shipmentScope.displayName,
            scopeKind: outboundListQuery.scopeKind,
            scopeId: outboundListQuery.scopeId,
          },
        },
        pageWindow: outboundQueryFacts.pageWindow,
        query: {
          kind: 'outbound',
          listPath: '/api/business-console/v1/wms/outbound-orders',
          selectionQuery: outboundListQuery,
          keywordQuery: outboundKeywordQuery,
          forbiddenQueryKeys: ['siteCode'],
        },
      },
    })
    record({
      node: 'delivery-wms-outbound',
      sourceObject: DELIVERY_ORDER_NO,
      downstreamObject: textOf(assignedOutbound.match.outboundOrderId),
      stableKey: `${DELIVERY_ORDER_NO} -> ${textOf(assignedOutbound.match.outboundOrderNo)}`,
      automationMode: 'automatic',
      request: outbound.call.summary,
      responseOrLog: { outbound: publicJson(assignedOutbound.match), ui: outboundUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        'ERP 发货释放跨 Redis 生成 WMS 出库单，授权作业池绑定后真实出库页面以 HTTP 200 渲染 DO-WALK-001。',
      responsibilityIssue: null,
    })
    const outboundLines = assignedOutbound.match.lines as JsonRecord[]
    expect(outboundLines).toHaveLength(1)
    const pickingLine = outboundLines[0]!
    expect(pickingLine.skuCode).toBe(FINISHED_SKU)
    expect(pickingLine.requestedQuantity).toBe(QUANTITY)
    const pickingScopeQuery = {
      organizationId,
      environmentId,
      scopeKind: shipmentScopeKind,
      scopeId: shipmentScopeId,
    }
    const picking = await executeWalkthroughPicking(
      {
        outboundOrderId: outboundId,
        taskNo: `PICK-${DELIVERY_ORDER_NO}`,
        lineNo: textOf(pickingLine.lineNo),
        fromLocationCode: textOf(pickingLine.locationCode),
        toLocationCode: FINISHED_GOODS_LOCATION,
        quantity: QUANTITY,
        scopeKind: shipmentScopeKind,
        scopeId: shipmentScopeId,
      },
      async (path, body) => {
        const response = await workerCall('POST', queryPath(path, pickingScopeQuery), body)
        setup.push({
          kind: 'wms-picking-execution',
          request: response.summary,
          response: response.publicPayload,
        })
        return asRecord(dataOf(response.payload))
      },
      async (warehouseTaskId) => {
        const task = await workerPollRows(
          '/api/business-console/v1/wms/picking-tasks',
          { ...pickingScopeQuery, keyword: `PICK-${DELIVERY_ORDER_NO}`, skip: 0, take: 100 },
          (row) => textOf(row.warehouseTaskId) === warehouseTaskId,
        )
        expect(task.match.assignedPoolCode).toBe(shipmentPoolCode)
        expect(task.match.assignedOperatorUserId).toBe(workerRuntime.principalId)
        return { version: Number(task.match.version) }
      },
    )
    expect(textOf(picking.status).toLowerCase()).toBe('completed')
    expect(picking.executedQuantity).toBe(QUANTITY)
    const pickedOutbound = await workerPollRows(
      '/api/business-console/v1/wms/outbound-orders',
      { ...pickingScopeQuery, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) =>
        textOf(row.outboundOrderId) === outboundId && Number(row.version) > assignedOutboundVersion,
    )
    const completedOutbound = await workerCall(
      'POST',
      queryPath(
        `/api/business-console/v1/wms/outbound-orders/${encodeURIComponent(outboundId)}/complete`,
        { organizationId, environmentId },
      ),
      {
        packReviewNo: PACK_REVIEW_NO,
        passed: true,
        idempotencyKey: `issue1912-complete-${DELIVERY_ORDER_NO}`,
        scopeKind: shipmentScopeKind,
        scopeId: shipmentScopeId,
        expectedVersion: Number(pickedOutbound.match.version),
      },
    )
    const completedDelivery = await pollRows(
      '/api/business-console/v1/erp/sales/delivery-orders',
      { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) =>
        textOf(row.deliveryOrderNo) === DELIVERY_ORDER_NO &&
        textOf(row.status).toLowerCase() === 'completed',
      180_000,
    )
    record({
      node: 'wms-completed-erp-delivery',
      sourceObject: outboundId,
      downstreamObject: DELIVERY_ORDER_NO,
      stableKey: `${outboundId} -> ${DELIVERY_ORDER_NO} -> completed`,
      automationMode: 'automatic',
      request: completedOutbound.summary,
      responseOrLog: {
        picking: publicJson(picking),
        completedOutbound: completedOutbound.publicPayload,
        delivery: publicJson(completedDelivery.match),
        ui: await provePageSafely('wms-completed-erp-delivery', {
          route: '/erp/sales/deliveries',
          listPath: '/api/business-console/v1/erp/sales/delivery-orders',
          filterLabel: '发货关键字',
          expectedListQuery: erpListQuery(),
          stableText: DELIVERY_ORDER_NO,
          emptyText: '还没有发货单',
          screenshotName: '17b-completed-sales-delivery.png',
        }),
      },
      conclusion: 'runtime-confirmed',
      demoWording: 'WMS 出库在真实作业范围内完成并以公开 ERP 读面证明对应 DO 已 completed。',
      responsibilityIssue: null,
    })
    const receivable = await pollRows(
      '/api/business-console/v1/erp/finance/receivables',
      { organizationId, environmentId, keyword: DELIVERY_ORDER_NO, skip: 0, take: 100 },
      (row) => textOf(row.sourceDocumentNo) === DELIVERY_ORDER_NO,
      180_000,
    )
    const receivableNo = textOf(
      receivable.match.receivableNo || receivable.match.accountReceivableNo,
    )
    if (!receivableNo)
      throw new Error(`Delivery ${DELIVERY_ORDER_NO} produced no stable receivable number.`)
    const arUi = await provePageSafely('erp-account-receivable', {
      route: '/erp/finance/ar-ap',
      listPath: '/api/business-console/v1/erp/finance/receivables',
      filterLabel: '应收关键字',
      expectedListQuery: erpListQuery(),
      stableText: DELIVERY_ORDER_NO,
      emptyText: '还没有应收账款。销售出货或手工登记后会在这里形成应收。',
      screenshotName: '18-account-receivable.png',
    })
    record({
      node: 'erp-account-receivable',
      sourceObject: DELIVERY_ORDER_NO,
      downstreamObject: receivableNo,
      stableKey: `${DELIVERY_ORDER_NO} -> ${receivableNo}`,
      automationMode: 'automatic',
      request: receivable.call.summary,
      responseOrLog: { receivable: publicJson(receivable.match), ui: arUi },
      conclusion: 'runtime-confirmed',
      demoWording:
        '只有完成 WMS 出库后，ERP 应收读面以 HTTP 200 渲染由同一 DO 生成的稳定应收单号。',
      responsibilityIssue: null,
    })
  } catch (error) {
    const firstUnverified = REQUIRED_NODES.find(
      (node) => evidence.get(node)?.conclusion === 'not-verified',
    )
    if (firstUnverified) markFailure(firstUnverified, error, 'mixed')
    throw error
  } finally {
    try {
      await withSessionCredentialCleanup(
        () =>
          ledger.write(evidencePath!, {
            issue: 'GitHub #1912 / NERV-1127',
            generatedAtUtc: generatedAtUtc.toISOString(),
            organizationId,
            environmentId,
            adminPrincipalId: principalId,
            workerPrincipalId,
            rfqNo: RFQ_NO,
            supplierQuotationNo: SUPPLIER_QUOTATION_NO,
            salesQuotationNo: SALES_QUOTATION_NO,
            purchaseOrderNo: PURCHASE_ORDER_NO,
            purchaseReceiptNo: PURCHASE_RECEIPT_NO,
            salesOrderNo: SALES_ORDER_NO,
            deliveryOrderNo: DELIVERY_ORDER_NO,
            runtimeProfileSource: runtimeProfileSource ?? 'not-supplied',
            transport: transport ?? 'not-supplied',
            persistence: persistence ?? 'not-supplied',
            worldEnabled: worldEnabled ?? 'not-supplied',
            historyEnabled: historyEnabled ?? 'not-supplied',
            scaleOrderCount: scaleOrderCount ?? 'not-supplied',
            assertionBoundary:
              'public BusinessGateway HTTP plus rendered browser pages in two isolated ERP/WMS contexts; no database reads as business assertions',
            requestFailurePolicy:
              'Only ERR_ABORTED document/resource requests, plus fetch/xhr API requests observed before a confirmed navigation or a confirmed inactive/hidden tab panel whose prior slot content disappeared, are separated as expected cancellations; the evidence window closes immediately after the transition. API aborts without that evidence, including requests started after the transition or reported after completion, API HTTP errors, other navigation failures, and other resource failures remain fail-closed. Client-side planning demand filtering proves the rendered row without a second list wait, while same-route suggestion proof refreshes its completed list before confirming tab unmount.',
          }),
        () => {
          sessionCredentialTracker.clear()
          workerSessionCredentialTracker.clear()
        },
      )
    } finally {
      await workerContext?.close()
    }
  }

  const entries = REQUIRED_NODES.map((node) => evidence.get(node)!)
  expect(
    entries
      .filter((entry) => entry.conclusion !== 'runtime-confirmed')
      .map((entry) => ({ node: entry.node, conclusion: entry.conclusion })),
    'all #1912 walkthrough nodes must be runtime-confirmed through public HTTP and rendered pages',
  ).toEqual([])
  expect([...new Set(uiEvidence.map((proof) => proof.node))].sort()).toEqual(
    [...REQUIRED_NODES].sort(),
  )
  expect(failedRequests, 'the real browser run must not leave failed requests').toEqual([])
  expect(pageErrors, 'the real browser run must not leave page errors').toEqual([])
  expect(expectedBusinessRejections).toHaveLength(1)
  expect(expectedBusinessRejections[0]).toMatchObject({
    actor: 'wms-worker',
    principalId: 'user-emp-049',
    status: 403,
    publicError: {
      code: 'missing-work-pool-assignment',
      message: 'missing-work-pool-assignment',
    },
  })
})
