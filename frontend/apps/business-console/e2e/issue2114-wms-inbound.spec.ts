import { expect, test } from '@playwright/test'
import path from 'node:path'
import { setTimeout as delay } from 'node:timers/promises'
import type * as Api from '../../../packages/api-client/src/generated/business-console/types.gen'
import type { NervIipPlatformGatewayWebApplicationAuthConsoleAuthResponse as Auth } from '../../../packages/api-client/src/generated/types.gen'
import { runProcurement, type PublicCall, type Row } from './procurementScenario'

type Inbound =
  Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleWmsInboundOrderItem
type Task =
  Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleWmsWarehouseTaskItem
type MovementList =
  Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleInventoryMovementListResponse
type Availability =
  Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleInventoryAvailabilityResponse
type Completion =
  Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCompleteWmsMovementResponse
type Action =
  Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleWmsWarehouseTaskActionResult
const evidencePath = process.env.NERV_IIP_NERV2114_EVIDENCE_PATH
const scenario = process.env.NERV_IIP_NERV2114_SCENARIO!
const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
const wms = '/api/business-console/v1/wms'
test.skip(!evidencePath, 'NERV-2114 requires an explicitly selected managed FullStack session')
test.use({ trace: 'off', screenshot: 'off' })
test.setTimeout(15 * 60 * 1000)

test('NERV-2114 真实采购收货经仓管上架形成唯一批次库存', async ({ page, browser }) => {
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
    method: 'GET' | 'POST',
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
      page,
      issue: 'NERV-2114',
      scenario,
      evidencePath: evidencePath!,
      headSha: process.env.NERV_IIP_NERV2114_HEAD_SHA!,
      sessionId: process.env.NERV_IIP_NERV2114_SESSION_ID!,
      includeRodRawMaterial: true,
      beforeCall: pace,
      afterReceipt: async ({ call, query, order, report }) => {
        report.worker = { principalId: auth.principal!.principalId, calls: workerCalls }
        const catalog =
          await workerCall<Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleWmsWorkScopeCatalog>(
            'GET',
            query(`${wms}/work-scopes/receipts`),
          )
        const pool = catalog.items!.filter(
          (item) => item.scopeKind === 'work-pool' && item.siteCode === 'SITE-001',
        )
        expect(pool).toHaveLength(1)
        expect(catalog.actorPrincipalId).toBe(auth.principal!.principalId)
        const workScope = { scopeKind: 'work-pool', scopeId: pool[0].scopeId! }
        const requirement = order.requirement as { skuCode: string; unitOfMeasureCode: string }
        const quantity = order.quantity as number
        const suffix = (order.purchaseOrderNo as string).replace('PO-', '')
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
        const inbound =
          await call<Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateWmsInboundOrderResponse>(
            'POST',
            query(`${wms}/inbound-orders`),
            {
              ...scope,
              inboundOrderNo,
              sourceDocumentType: 'purchase-receipt',
              sourceDocumentId: order.purchaseReceiptNo as string,
              siteCode: 'SITE-001',
              lines: [
                {
                  lineNo: '10',
                  skuCode: requirement.skuCode,
                  uomCode: requirement.unitOfMeasureCode,
                  receivedQuantity: quantity,
                  stagingLocationCode: 'loc-raw-01',
                  lotNo,
                  qualityStatus: 'unrestricted',
                  ownerType: 'company',
                },
              ],
            } satisfies Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateWmsInboundOrderRequest,
          )
        expect(inbound.inboundOrderId).toMatch(/\S/)
        const assignment =
          await call<Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleWmsAssignmentResult>(
            'POST',
            query(`${wms}/inbound-orders/${inbound.inboundOrderId}/assignment`),
            {
              poolCode: pool[0].poolCode!,
              operatorPrincipalId: auth.principal!.principalId,
              expectedVersion: 1,
              idempotencyKey: `${suffix}-assign`,
            } satisfies Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleAssignWmsResourceRequest,
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
        const task =
          await workerCall<Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateWmsWarehouseTaskResponse>(
            'POST',
            query(`${wms}/inbound-orders/${inbound.inboundOrderId}/putaway-tasks`),
            {
              taskNo: `PT-${suffix}`,
              lineNo: '10',
              fromLocationCode: 'loc-raw-01',
              toLocationCode: 'loc-wip-01',
              quantity,
            } satisfies Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCreateWmsPutawayTaskRequest,
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
        } satisfies Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCompleteWmsWarehouseTaskRequest
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
        } satisfies Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleCompleteWmsInboundOrderRequest
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
          locationCode: 'loc-wip-01',
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
          locationCode: 'loc-wip-01',
          quantity,
        })
        expect(first.items![0].movementId).toMatch(/\S/)
        expect(first.items![0].postedAtUtc).toMatch(/\S/)
        expect((await complete()).requestId).toBe(accepted.requestId)
        expect(await movements()).toEqual(first)
        const erp = await call<{
          items: Api.NervIipBusinessGatewayWebApplicationBusinessServicesBusinessConsoleErpPurchaseOrderItem[]
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
    await worker.getByPlaceholder('任务号/来源单/物料').fill(`PT-N2114-${scenario}-1`)
    await expect(
      worker
        .getByRole('row')
        .filter({ hasText: `PT-N2114-${scenario}-1` })
        .first(),
    ).toBeVisible()
    await worker.screenshot({
      path: path.join(path.dirname(evidencePath!), `${scenario}-putaway.png`),
      fullPage: true,
    })
  } finally {
    await workerContext.close()
  }
})
