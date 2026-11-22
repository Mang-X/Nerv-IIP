import {
  type WalkthroughContext,
  queryPath,
  RFQ_NO,
  SUPPLIER_QUOTATION_NO,
  PURCHASE_ORDER_NO,
  SITE_CODE,
  PURCHASE_ORDER_APPROVAL_TEMPLATE_CODE,
  PURCHASE_ORDER_APPROVAL_TEMPLATE_VERSION,
  INBOUND_LOCATION,
} from './context'
import { rowsOf, textOf, asRecord, publicJson, dataOf } from '../issue1912-walkthrough-evidence'
import { expect } from '@playwright/test'

export async function runProcurement(context: WalkthroughContext) {
  const { organizationId, environmentId, principalId, erpListQuery, setPurchaseReceiptNo } = context
  const { call, pollRows, adminRuntime } = context.session
  const { record, setup } = context.ledger
  const { provePageSafely } = context.proofs

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
  if (!purchaseOrderId) throw new Error(`Purchase order ${PURCHASE_ORDER_NO} did not return an ID.`)
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
    demoWording: '通过公开 ERP 采购接口以供应商报价行创建了固定采购订单，随后将用真实审批链释放。',
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
    purchaseOrderNo: PURCHASE_ORDER_NO,
    lines: [
      {
        purchaseOrderLineNo: textOf(quoteLine.lineNo || '10'),
        receivedQuantity: materialQuantity,
        qualityStatus: 'unrestricted',
        locationCode: INBOUND_LOCATION,
      },
    ],
    idempotencyKey: `issue1912-receipt-${PURCHASE_ORDER_NO}`,
  }
  const receipt = await call(
    'POST',
    '/api/business-console/v1/erp/procurement/purchase-receipts',
    receiptRequest,
  )
  setup.push({ request: receipt.summary, response: receipt.publicPayload })
  const purchaseReceiptNo = textOf(asRecord(dataOf(receipt.payload)).purchaseReceiptNo)
  setPurchaseReceiptNo(purchaseReceiptNo)
  expect(purchaseReceiptNo).toMatch(/\S/)
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
    downstreamObject: purchaseReceiptNo,
    stableKey: `${PURCHASE_ORDER_NO} -> ${purchaseReceiptNo}`,
    automationMode: 'manual',
    request: receipt.summary,
    responseOrLog: {
      receipt: receipt.publicPayload,
      purchaseOrder: publicJson(receiptOrder.match),
      ui: receiptUi,
    },
    conclusion: 'runtime-confirmed',
    demoWording: `采购收货公开接口返回 ${purchaseReceiptNo}，采购收货页面渲染同一采购订单行和已收数量。`,
    responsibilityIssue: null,
  })

  return { quoteLine, materialSku, materialUom, materialQuantity, purchaseReceiptNo }
}
