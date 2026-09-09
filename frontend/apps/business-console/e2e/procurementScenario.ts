import { expect, type Page } from '@playwright/test'
import { mkdir, writeFile } from 'node:fs/promises'
import path from 'node:path'
import type {
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleApprovalChainItem as ApprovalChain,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateBusinessPartnerRequest as CreatePartnerRequest,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateErpPurchaseOrderRequest as CreateOrderRequest,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateErpPurchaseOrderResponse as CreateOrderResponse,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleErpPurchaseOrderItem as PurchaseOrder,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleErpSupplierQuotationItem as SupplierQuotation,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleManufacturingBomItem as ManufacturingBom,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleRecordErpPurchaseReceiptRequest as RecordReceiptRequest,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleRecordErpPurchaseReceiptResponse as RecordReceiptResponse,
  NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleResolveApprovalStepRequest as ResolveApprovalRequest,
} from '../../../packages/api-client/src/generated/business-console/types.gen'
import type { NervIipPlatformGatewayWebApplicationAuthConsoleAuthResponse as AuthResponse } from '../../../packages/api-client/src/generated/types.gen'
import {
  assertExpectedMaterialSkuCodes,
  calculateRequiredQuantity,
  selectConcreteMaterialLines,
  type MbomMaterialLineFact,
} from '../src/issue1851MbomInventoryBaseline'

export type Row = Record<string, unknown>
export type PublicCall = <T>(method: 'GET' | 'POST', endpoint: string, body?: Row) => Promise<T>
export type ProcurementSupplyOrder = {
  requirement: MbomMaterialLineFact
  quantity: number
  quotationNo: SupplierQuotation['quotationNo']
  purchaseOrderNo: string
  purchaseReceiptNo: string
  purchaseReceiptId: RecordReceiptResponse['purchaseReceiptId']
  approvalChainId: ApprovalChain['chainId']
  readback: PurchaseOrder
  wms?: Row
}
export type ProcurementOptions = {
  page: Page
  issue: string
  scenario: string
  evidencePath: string
  headSha: string
  sessionId: string
  includeRodRawMaterial?: boolean
  beforeCall?: () => Promise<void>
  afterReceipt?: (context: {
    call: PublicCall
    query: (endpoint: string, extra?: Row) => string
    order: ProcurementSupplyOrder
    report: Row
  }) => Promise<void>
}
const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
const procurement = '/api/business-console/v1/erp/procurement'
const partners = [
  ['SUP-WALK-MET-01', '常州精工减振部件有限公司'],
  ['SUP-WALK-VLV-01', '无锡精密阀系有限公司'],
  ['SUP-WALK-ACC-01', '常州减振附件有限公司'],
  ['SUP-WB-PKG-01', '昆山鑫达包装材料有限公司'],
  ['SUP-WB-PKG-02', '太仓绿印包装制品有限公司'],
] as const

export async function runProcurement(options: ProcurementOptions) {
  const { page, issue, scenario, evidencePath } = options
  const baseURL = process.env.NERV_IIP_PLAYWRIGHT_BASE_URL
  const adminPassword = process.env.NERV_IIP_FULLSTACK_ADMIN_PASSWORD
  expect(baseURL).toBeTruthy()
  expect(adminPassword).toBeTruthy()
  expect(process.env.NERV_IIP_FULLSTACK_STATE_ROOT).toBeTruthy()
  expect(['purchased', 'mixed']).toContain(scenario)
  const calls: Row[] = []
  const orders: Row[] = []
  const uiPages: Row[] = []
  const report: Row = {
    issue,
    scenario,
    scope,
    siteCode: 'SITE-001',
    headSha: options.headSha,
    sessionId: options.sessionId,
    conclusion: 'not-verified',
    calls,
    orders,
    uiPages,
    inventoryFormed: false,
    receiptReadback: 'purchase-order receivedQuantity projection; no public receipt-detail GET',
  }
  let token = ''
  const query = (endpoint: string, extra: Row = {}) => {
    const url = new URL(endpoint, baseURL)
    for (const [key, value] of Object.entries({ ...scope, ...extra })) {
      url.searchParams.set(key, String(value))
    }
    return url.pathname + url.search
  }
  const call = async <T>(method: 'GET' | 'POST', endpoint: string, body?: Row): Promise<T> => {
    await options.beforeCall?.()
    const response = await page.request.fetch(new URL(endpoint, baseURL).toString(), {
      method,
      data: body,
      headers: { authorization: token },
    })
    calls.push({ method, path: endpoint, status: response.status(), body })
    if (!response.ok()) throw new Error(`Public ${method} ${endpoint} HTTP ${response.status()}`)
    return ((await response.json()) as { data: T }).data
  }
  const list = async <T>(endpoint: string, extra: Row = {}): Promise<T[]> => {
    const result = await call<{ items?: T[] }>(
      'GET',
      query(endpoint, { skip: 0, take: 100, ...extra }),
    )
    expect(Array.isArray(result.items)).toBe(true)
    return result.items!
  }
  try {
    await page.goto('/login')
    await page.getByLabel('登录名').fill('admin')
    await page.getByLabel('密码').fill(adminPassword!)
    const loginResponse = page.waitForResponse(
      (r) => new URL(r.url()).pathname === '/api/console/v1/auth/login',
    )
    await page.getByRole('button', { name: '登录' }).click()
    const login = await loginResponse
    expect(login.ok()).toBe(true)
    const auth = ((await login.json()) as { data: AuthResponse }).data
    expect(auth.principal!.organizationId).toBe(scope.organizationId)
    expect(auth.principal!.environmentId).toBe(scope.environmentId)
    token = `Bearer ${auth.accessToken}`
    await expect(page).toHaveURL(new URL('/', baseURL).toString())
    report.userAgent = await page.evaluate(() => navigator.userAgent)

    for (const [code, name] of partners) {
      await call('POST', '/api/business-console/v1/master-data/business-partners', {
        ...scope,
        code,
        name,
        partnerType: 'supplier',
        partnerRoles: ['supplier'],
        idempotencyKey: `${issue.toLowerCase().replace('nerv-', 'n')}-${scenario}-supplier-${code}`,
      } satisfies CreatePartnerRequest)
    }
    const mbom = await call<ManufacturingBom>(
      'GET',
      query('/api/business-console/v1/engineering/manufacturing-boms/MBOM-FG-QJ-P1-L/2'),
    )
    expect(mbom.skuCode).toBe('FG-QJ-P1-L')
    expect(mbom.status!.toLowerCase()).toBe('published')
    const lines = mbom.materialLines as MbomMaterialLineFact[]
    assertExpectedMaterialSkuCodes(lines)
    const requirements = selectConcreteMaterialLines(lines).filter(
      (line) => scenario !== 'mixed' || line.skuCode !== 'SF-ROD-01',
    )
    expect(requirements.length).toBe(scenario === 'mixed' ? 10 : 11)
    if (options.includeRodRawMaterial && scenario === 'mixed') {
      const rodLine = selectConcreteMaterialLines(lines).find(
        (line) => line.skuCode === 'SF-ROD-01',
      )!
      const rodQuantity = calculateRequiredQuantity(rodLine, 1)
      const rodBoms = await list<ManufacturingBom>(
        '/api/business-console/v1/engineering/manufacturing-boms',
        { skuCode: 'SF-ROD-01' },
      )
      const rodBom = rodBoms.filter(
        (bom) => bom.skuCode === 'SF-ROD-01' && bom.status?.toLowerCase() === 'published',
      )
      expect(rodBom).toHaveLength(1)
      const rawLines = selectConcreteMaterialLines(
        rodBom[0].materialLines as MbomMaterialLineFact[],
      )
      expect(rawLines.map((line) => line.skuCode)).toEqual(['RM-BAR-01'])
      requirements.push(
        ...rawLines.map((line) => ({
          ...line,
          quantity: calculateRequiredQuantity(line, rodQuantity),
          scrapRate: 0,
          yieldRate: 1,
        })),
      )
      report.rodEngineering = { rodQuantity, mbom: rodBom[0] }
    }
    const quotes = await list<SupplierQuotation>(`${procurement}/supplier-quotations`)
    const rawQuote = quotes
      .flatMap((quote) => quote.lines!)
      .filter((line) => line.skuCode === 'RM-BAR-01')
    expect(rawQuote).toHaveLength(1)
    expect(rawQuote[0].uomCode).toBe('kg')
    expect(rawQuote[0].unitPrice).toBe(8)
    report.rawMaterialCatalog = {
      skuCode: 'RM-BAR-01',
      uomCode: 'kg',
      unitPrice: 8,
      actualQuantityOwner: 'ProductEngineering downstream',
    }

    for (const [index, requirement] of requirements.entries()) {
      const candidates = quotes.flatMap((quote) =>
        quote
          .lines!.filter((line) => line.skuCode === requirement.skuCode)
          .map((line) => ({
            ...line,
            supplierCode: quote.supplierCode,
            quotationNo: quote.quotationNo,
          })),
      )
      expect(candidates).toHaveLength(1)
      const quote = candidates[0]
      expect(quote.uomCode).toBe(requirement.unitOfMeasureCode)
      const quantity = calculateRequiredQuantity(requirement, 1)
      const purchaseOrderNo = `PO-${issue.replace('NERV-', 'N')}-${scenario}-${index + 1}`
      const request = {
        ...scope,
        purchaseOrderNo,
        supplierCode: quote.supplierCode,
        siteCode: 'SITE-001',
        lines: [
          {
            lineNo: '10',
            skuCode: requirement.skuCode,
            uomCode: quote.uomCode,
            quantity,
            unitPrice: quote.unitPrice,
            promisedDate: '2099-12-31',
          },
        ],
        idempotencyKey: `${issue.toLowerCase().replace('nerv-', 'n')}-${scenario}-po-${index + 1}`,
      } satisfies CreateOrderRequest
      const order = await call<CreateOrderResponse>(
        'POST',
        `${procurement}/purchase-orders`,
        request,
      )
      expect(order.purchaseOrderId).toMatch(/\S/)
      const replay = await call<CreateOrderResponse>(
        'POST',
        `${procurement}/purchase-orders`,
        request,
      )
      expect(replay.purchaseOrderId).toBe(order.purchaseOrderId)
      let approval: ApprovalChain | undefined
      await expect
        .poll(
          async () => {
            approval = (
              await list<ApprovalChain>('/api/business-console/v1/approval/chains', {
                documentType: 'purchase-order',
                documentId: purchaseOrderNo,
              })
            ).find((row) => row.documentId === purchaseOrderNo && row.status === 'pending')
            return Boolean(approval)
          },
          { timeout: 60_000 },
        )
        .toBe(true)
      await call(
        'POST',
        `/api/business-console/v1/approval/chains/${encodeURIComponent(approval!.chainId!)}/steps/1/resolve`,
        {
          ...scope,
          actorType: auth.principal!.principalType,
          actorRef: auth.principal!.principalId,
          decision: 'approve',
          comment: '双来源场景采购下达',
        } satisfies ResolveApprovalRequest,
      )
      await expect
        .poll(
          async () =>
            (
              await list<PurchaseOrder>(`${procurement}/purchase-orders`, {
                keyword: purchaseOrderNo,
              })
            )
              .find((row) => row.purchaseOrderNo === purchaseOrderNo)
              ?.status?.toLowerCase(),
          { timeout: 60_000 },
        )
        .toBe('released')
      const receiptRequest = {
        ...scope,
        purchaseReceiptNo: `PR-${issue.replace('NERV-', 'N')}-${scenario}-${index + 1}`,
        purchaseOrderNo,
        lines: [
          { purchaseOrderLineNo: '10', receivedQuantity: quantity, qualityStatus: 'unrestricted' },
        ],
        idempotencyKey: `${issue.toLowerCase().replace('nerv-', 'n')}-${scenario}-receipt-${index + 1}`,
      } satisfies RecordReceiptRequest
      const receipt = await call<RecordReceiptResponse>(
        'POST',
        `${procurement}/purchase-receipts`,
        receiptRequest,
      )
      expect(receipt.purchaseReceiptId).toMatch(/\S/)
      const receiptReplay = await call<RecordReceiptResponse>(
        'POST',
        `${procurement}/purchase-receipts`,
        receiptRequest,
      )
      expect(receiptReplay.purchaseReceiptId).toBe(receipt.purchaseReceiptId)
      const readback = (
        await list<PurchaseOrder>(`${procurement}/purchase-orders`, { keyword: purchaseOrderNo })
      ).filter((row) => row.purchaseOrderNo === purchaseOrderNo)
      expect(readback).toHaveLength(1)
      expect(readback[0].supplierCode).toBe(quote.supplierCode)
      expect(readback[0].siteCode).toBe('SITE-001')
      expect(readback[0].lines).toHaveLength(1)
      expect(readback[0].lines![0]).toMatchObject({
        skuCode: requirement.skuCode,
        uomCode: quote.uomCode,
        orderedQuantity: quantity,
        receivedQuantity: quantity,
        unitPrice: quote.unitPrice,
      })
      const supplyOrder: ProcurementSupplyOrder = {
        requirement,
        quantity,
        quotationNo: quote.quotationNo,
        purchaseOrderNo,
        purchaseReceiptNo: receiptRequest.purchaseReceiptNo,
        purchaseReceiptId: receipt.purchaseReceiptId,
        approvalChainId: approval!.chainId,
        readback: readback[0],
      }
      orders.push(supplyOrder)
      await options.afterReceipt?.({ call, query, order: supplyOrder, report })
    }
    // API 操作完成后才导航，避免浏览器恢复会话导致已捕获 token 轮换。
    for (const route of ['/erp/procurement/purchase-orders', '/erp/procurement/receipts']) {
      const responsePromise = page.waitForResponse(
        (r) =>
          new URL(r.url()).pathname === `${procurement}/purchase-orders` &&
          r.request().method() === 'GET',
      )
      await page.goto(route)
      const response = await responsePromise
      expect(response.status()).toBe(200)
      await page
        .getByPlaceholder(
          route.endsWith('/receipts') ? '采购单 / 供应商 / 物料' : '采购单 / 供应商 / 物料 / 工厂',
        )
        .fill(`PO-${issue.replace('NERV-', 'N')}-${scenario}-1`)
      await expect(
        page
          .getByRole('row')
          .filter({ hasText: `PO-${issue.replace('NERV-', 'N')}-${scenario}-1` })
          .first(),
      ).toBeVisible()
      const screenshot = path.join(
        path.dirname(evidencePath!),
        `${scenario}-${route.split('/').at(-1)}.png`,
      )
      await page.screenshot({ path: screenshot, fullPage: true })
      uiPages.push({ route, publicGetStatus: response.status(), screenshot })
    }
    report.conclusion = 'procurement-and-receipt-projection-confirmed'
  } finally {
    await mkdir(path.dirname(evidencePath!), { recursive: true })
    await writeFile(evidencePath!, JSON.stringify(report, null, 2))
  }
}
