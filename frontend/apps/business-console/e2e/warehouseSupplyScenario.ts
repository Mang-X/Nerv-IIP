import { expect, type Browser } from '@playwright/test'
import path from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import type * as Api from '@nerv-iip/api-client'
import type { ConsoleAuthResponse as Auth } from '@nerv-iip/api-client'
import {
  runProcurement,
  type PublicCall,
  type Row,
  type ProcurementOptions,
} from './procurementScenario'

type Inbound = Api.BusinessConsoleWmsInboundOrderItem
type Task = Api.BusinessConsoleWmsWarehouseTaskItem
type MovementList = Api.BusinessConsoleInventoryMovementListResponse
type Availability = Api.BusinessConsoleInventoryAvailabilityResponse
type Completion = Api.BusinessConsoleCompleteWmsMovementResponse
type Action = Api.BusinessConsoleWmsWarehouseTaskActionResult
const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
const wms = '/api/business-console/v1/wms'
export async function runWarehouseSupply(
  options: Omit<ProcurementOptions, 'afterReceipt'> & {
    browser: Browser
    storageLocation?: (skuCode: string) => string
  },
) {
  const { browser, scenario, evidencePath, issue } = options
  expect(process.env.NERV_IIP_LEADER_DEMO_WORKER_PASSWORD).toBeTruthy()
  const workerContext = await browser.newContext({
    baseURL: process.env.NERV_IIP_PLAYWRIGHT_BASE_URL,
  })
  const worker = await workerContext.newPage()
  await worker.goto('/login')
  await worker.getByLabel('登录名').fill('emp049')
  await worker.getByLabel('密码').fill(process.env.NERV_IIP_LEADER_DEMO_WORKER_PASSWORD!)
  const loginPromise = worker.waitForResponse(
    (response) => new URL(response.url()).pathname === '/api/console/v1/auth/login',
  )
  await worker.getByRole('button', { name: '登录' }).click()
  const login = await loginPromise
  expect(login.status()).toBe(200)
  const auth = ((await login.json()) as { data: Auth }).data
  expect(auth.principal).toMatchObject(scope)
  expect(auth.principal!.principalId).toBe('user-emp-049')
  await expect(worker).toHaveURL(new URL('/', process.env.NERV_IIP_PLAYWRIGHT_BASE_URL).toString())
  // 公开动作期间卸载首页，避免后台刷新使已捕获的会话失效；结束后再回真实业务页。
  await worker.goto('about:blank')
  const workerCalls: Row[] = []
  // BusinessGateway 默认每个 IP 每 60 秒最多 300 次；本批逐项读回以低于该速率执行，429 仍失败。
  const pace = () => delay(250)
  const workerCall: PublicCall = async <T>(
    method: 'GET' | 'POST' | 'PATCH',
    endpoint: string,
    body?: Row,
  ) => {
    await pace()
    const response = await worker.request.fetch(endpoint, {
      method,
      data: body,
      headers: { authorization: `Bearer ${auth.accessToken}` },
    })
    workerCalls.push({ method, path: endpoint, status: response.status(), body })
    if (!response.ok()) throw new Error(`Worker ${method} ${endpoint} HTTP ${response.status()}`)
    return ((await response.json()) as { data: T }).data
  }
  try {
    await runProcurement({
      ...options,
      includeRodRawMaterial: true,
      inventoryPostingRoute: 'wms',
      beforeCall: pace,
      afterReceipt: async ({ call, query, order, report }) => {
        report.worker = { principalId: auth.principal!.principalId, calls: workerCalls }
        const catalog = await workerCall<Api.BusinessConsoleWmsWorkScopeCatalog>(
          'GET',
          query(`${wms}/work-scopes/receipts`),
        )
        const pool = catalog.items!.filter(
          (item) => item.scopeKind === 'work-pool' && item.siteCode === 'SITE-001',
        )
        expect(pool).toHaveLength(1)
        expect(catalog.actorPrincipalId).toBe(auth.principal!.principalId)
        const workScope = { scopeKind: 'work-pool', scopeId: pool[0].scopeId! }
        const requirement = order.requirement
        const storageLocation = options.storageLocation?.(requirement.skuCode) ?? 'loc-wip-01'
        const stagingLocation = storageLocation === 'loc-raw-01' ? 'loc-wip-01' : 'loc-raw-01'
        const quantity = order.quantity
        const suffix = order.purchaseOrderNo.replace('PO-', '')
        const inboundOrderNo = `IB-${suffix}`
        const lotNo = `LOT-${suffix}`
        const inventoryQuery = {
          siteCode: 'SITE-001',
          skuCode: requirement.skuCode,
          uomCode: requirement.unitOfMeasureCode,
          lotNo,
        }
        const availability = () =>
          call<Availability>(
            'GET',
            query('/api/business-console/v1/inventory/availability', inventoryQuery),
          )
        expect((await availability()).availableQuantity).toBe(0)
        const inbound = await call<Api.BusinessConsoleCreateWmsInboundOrderResponse>(
          'POST',
          query(`${wms}/inbound-orders`),
          {
            ...scope,
            inboundOrderNo,
            sourceDocumentType: 'purchase-receipt',
            sourceDocumentId: order.purchaseReceiptNo,
            siteCode: 'SITE-001',
            lines: [
              {
                lineNo: '10',
                skuCode: requirement.skuCode,
                uomCode: requirement.unitOfMeasureCode,
                receivedQuantity: quantity,
                stagingLocationCode: stagingLocation,
                lotNo,
                qualityStatus: 'unrestricted',
                ownerType: 'company',
              },
            ],
          } satisfies Api.BusinessConsoleCreateWmsInboundOrderRequest,
        )
        expect(inbound.inboundOrderId).toMatch(/\S/)
        const assignment = await call<Api.BusinessConsoleWmsAssignmentResult>(
          'POST',
          query(`${wms}/inbound-orders/${inbound.inboundOrderId}/assignment`),
          {
            poolCode: pool[0].poolCode!,
            operatorPrincipalId: auth.principal!.principalId,
            expectedVersion: 1,
            idempotencyKey: `${suffix}-assign`,
          } satisfies Api.BusinessConsoleAssignWmsResourceRequest,
        )
        expect(assignment.operatorPrincipalId).toBe(auth.principal!.principalId)
        const inboundRead = async () => {
          const result = await workerCall<{ items: Inbound[] }>(
            'GET',
            query(`${wms}/inbound-orders`, { ...workScope, keyword: inboundOrderNo, take: 100 }),
          )
          const rows = result.items.filter((row) => row.inboundOrderId === inbound.inboundOrderId)
          expect(rows).toHaveLength(1)
          return rows[0]
        }
        const assigned = await inboundRead()
        expect(assigned).toMatchObject({
          siteCode: 'SITE-001',
          assignedOperatorUserId: auth.principal!.principalId,
          assignedPoolCode: pool[0].poolCode,
        })
        const task = await workerCall<Api.BusinessConsoleCreateWmsWarehouseTaskResponse>(
          'POST',
          query(`${wms}/inbound-orders/${inbound.inboundOrderId}/putaway-tasks`),
          {
            taskNo: `PT-${suffix}`,
            lineNo: '10',
            fromLocationCode: stagingLocation,
            toLocationCode: storageLocation,
            quantity,
          } satisfies Api.BusinessConsoleCreateWmsPutawayTaskRequest,
        )
        expect(task.warehouseTaskId).toMatch(/\S/)
        const taskRead = async () => {
          const result = await workerCall<{ items: Task[] }>(
            'GET',
            query(`${wms}/putaway-tasks`, { ...workScope, keyword: `PT-${suffix}`, take: 100 }),
          )
          const rows = result.items.filter((row) => row.warehouseTaskId === task.warehouseTaskId)
          expect(rows).toHaveLength(1)
          return rows[0]
        }
        const queued = await taskRead()
        expect(queued).toMatchObject({
          ...scope,
          siteCode: 'SITE-001',
          skuCode: requirement.skuCode,
          uomCode: requirement.unitOfMeasureCode,
          plannedQuantity: quantity,
          lotNo,
        })
        const started = await workerCall<Action>(
          'POST',
          query(`${wms}/putaway-tasks/${task.warehouseTaskId}/start`),
          { expectedVersion: queued.version!, idempotencyKey: `${suffix}-start` },
        )
        const taskComplete = {
          expectedVersion: started.version!,
          idempotencyKey: `${suffix}-putaway`,
          executedQuantity: quantity,
        } satisfies Api.BusinessConsoleCompleteWmsWarehouseTaskRequest
        const completedTask = await workerCall<Action>(
          'POST',
          query(`${wms}/putaway-tasks/${task.warehouseTaskId}/complete`),
          taskComplete,
        )
        expect(
          await workerCall<Action>(
            'POST',
            query(`${wms}/putaway-tasks/${task.warehouseTaskId}/complete`),
            taskComplete,
          ),
        ).toEqual(completedTask)
        const putaway = await taskRead()
        expect(putaway.status?.toLowerCase()).toBe('completed')
        expect(putaway.executedQuantity).toBe(quantity)
        expect(putaway.completedAtUtc).toMatch(/\S/)
        const completeRequest = {
          ...workScope,
          expectedVersion: (await inboundRead()).version!,
          idempotencyKey: `${suffix}-complete`,
        } satisfies Api.BusinessConsoleCompleteWmsInboundOrderRequest
        const complete = () =>
          workerCall<Completion>(
            'POST',
            query(`${wms}/inbound-orders/${inbound.inboundOrderId}/complete`),
            completeRequest,
          )
        const accepted = await complete()
        expect(accepted.requestId).toMatch(/\S/)
        expect((await complete()).requestId).toBe(accepted.requestId)
        await expect
          .poll(async () => (await inboundRead()).status?.toLowerCase(), { timeout: 60_000 })
          .toBe('completed')
        await expect
          .poll(async () => (await availability()).availableQuantity, { timeout: 60_000 })
          .toBe(quantity)
        const balance = await availability()
        expect(balance).toMatchObject({
          ...scope,
          ...inventoryQuery,
          onHandQuantity: quantity,
          availableQuantity: quantity,
          reservedQuantity: 0,
        })
        expect(balance.items).toHaveLength(1)
        expect(balance.items![0]).toMatchObject({
          locationCode: storageLocation,
          lotNo,
          availableQuantity: quantity,
        })
        const movements = () =>
          call<MovementList>(
            'GET',
            query('/api/business-console/v1/inventory/movements', {
              ...inventoryQuery,
              movementType: 'inbound',
              page: 1,
              pageSize: 100,
            }),
          )
        const first = await movements()
        expect(first.totalCount).toBe(1)
        expect(first.items).toHaveLength(1)
        expect(first.items![0]).toMatchObject({
          sourceDocumentId: inboundOrderNo,
          sourceDocumentLineId: '10',
          skuCode: requirement.skuCode,
          uomCode: requirement.unitOfMeasureCode,
          siteCode: 'SITE-001',
          lotNo,
          locationCode: storageLocation,
          quantity,
        })
        expect(first.items![0].movementId).toMatch(/\S/)
        expect(first.items![0].postedAtUtc).toMatch(/\S/)
        expect((await complete()).requestId).toBe(accepted.requestId)
        expect(await movements()).toEqual(first)
        const erp = await call<{
          items: Api.BusinessConsoleErpPurchaseOrderItem[]
        }>(
          'GET',
          query('/api/business-console/v1/erp/procurement/purchase-orders', {
            keyword: order.purchaseOrderNo,
          }),
        )
        const received = erp.items.filter((item) => item.purchaseOrderNo === order.purchaseOrderNo)
        expect(received).toHaveLength(1)
        expect(received[0].lines![0].receivedQuantity).toBe(quantity)
        order.wms = {
          inboundOrderNo,
          inboundOrderId: inbound.inboundOrderId,
          sourceDocumentType: 'purchase-receipt',
          sourceDocumentId: order.purchaseReceiptNo,
          lotNo,
          assignment,
          putaway,
          accepted,
          completed: await inboundRead(),
          balance,
          movements: first,
          erpReadback: received[0],
        }
        report.inventoryFormed = true
      },
    })
    const responsePromise = worker.waitForResponse(
      (response) =>
        new URL(response.url()).pathname === `${wms}/putaway-tasks` &&
        response.request().method() === 'GET',
    )
    await worker.goto('/wms/putaway')
    expect((await responsePromise).status()).toBe(200)
    await worker
      .getByPlaceholder('任务号/来源单/物料')
      .fill(`PT-${issue.replace('NERV-', 'N')}-${scenario}-1`)
    await expect(
      worker
        .getByRole('row')
        .filter({ hasText: `PT-${issue.replace('NERV-', 'N')}-${scenario}-1` })
        .first(),
    ).toBeVisible()
    await worker.screenshot({
      path: path.join(path.dirname(evidencePath!), `${scenario}-putaway.png`),
      fullPage: true,
    })
  } finally {
    await workerContext.close()
  }
}
