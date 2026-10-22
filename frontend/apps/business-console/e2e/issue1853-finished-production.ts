import { expect, type Browser } from '@playwright/test'
import { execFileSync } from 'node:child_process'
import { basename, dirname } from 'node:path'
import type * as Api from '@nerv-iip/api-client'
import { runWarehouseSupply } from './warehouseSupplyScenario'
import type { Row } from './procurementScenario'
import {
  calculateRequiredQuantity,
  selectConcreteMaterialLines,
  type MbomMaterialLineFact,
} from '../src/issue1851MbomInventoryBaseline'

const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
const workScope = { scopeKind: 'organization', scopeId: 'org-001' }
const mes = '/api/business-console/v1/mes'

/** 在当前真实 session 重建外购供给，验证最终 FG 下达及物料消耗。 */
export async function runFinishedProduction(options: {
  browser: Browser
  workOrderId: string
  producedLotNo: string
  evidencePath: string
}) {
  const { browser, workOrderId, producedLotNo, evidencePath } = options
  const context = await browser.newContext({ baseURL: process.env.NERV_IIP_PLAYWRIGHT_BASE_URL })
  const page = await context.newPage()
  const facts: Row = {}
  try {
    await runWarehouseSupply({
      page,
      browser,
      issue: 'NERV-1853',
      scenario: 'purchased',
      evidencePath,
      headSha: execFileSync('git', ['rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
      sessionId: basename(dirname(evidencePath)),
      storageLocation: () => 'loc-semi-01',
      afterSupply: async ({ call, query, report, orders }) => {
        report.finalProduction = facts
        const detail = () =>
          call<Api.BusinessConsoleMesWorkOrderDetailResponse>(
            'GET',
            query(`${mes}/work-orders/${workOrderId}`, workScope),
          )
        const frozen = await detail()
        facts.frozenWorkOrder = frozen
        expect(frozen).toMatchObject({ skuId: 'FG-QJ-P1-L', quantity: 1 })
        const skuPath = `/api/business-console/v1/master-data/resources/sku/${frozen.skuId}`
        const sku = await call<Api.BusinessConsoleMasterDataResourceDetail>('GET', query(skuPath))
        facts.sku = sku
        // 本场景不赋序。种子 SKU 出厂即是公开码表里的 none，报工前不改任何主数据（#3725）。
        expect(sku).toMatchObject({ active: true, serialTrackingPolicy: 'none' })
        const versions = await call<{ items: Api.BusinessConsoleProductionVersionItem[] }>(
          'GET',
          query('/api/business-console/v1/engineering/production-versions', {
            skuCode: frozen.skuId,
          }),
        )
        const version = versions.items.filter(
          (item) => item.productionVersionId === frozen.productionVersionId,
        )
        expect(version).toHaveLength(1)
        expect(version[0]).toMatchObject({ mbomVersionId: 'MBOM-FG-QJ-P1-L:2' })
        facts.productionVersion = version[0]
        const mbom = await call<Api.BusinessConsoleManufacturingBomItem>(
          'GET',
          query('/api/business-console/v1/engineering/manufacturing-boms/MBOM-FG-QJ-P1-L/2'),
        )
        facts.frozenMbom = mbom
        const requirements = selectConcreteMaterialLines(
          mbom.materialLines as MbomMaterialLineFact[],
        )
        expect(requirements).toHaveLength(11)
        const balances = async () => {
          const result: Api.BusinessConsoleInventoryAvailabilityResponse[] = []
          for (const line of requirements) {
            const balance = await call<Api.BusinessConsoleInventoryAvailabilityResponse>(
              'GET',
              query('/api/business-console/v1/inventory/availability', {
                siteCode: 'SITE-001',
                skuCode: line.skuCode,
                uomCode: line.unitOfMeasureCode,
              }),
            )
            expect(balance).toMatchObject({
              onHandQuantity: calculateRequiredQuantity(line, frozen.quantity!),
              availableQuantity: calculateRequiredQuantity(line, frozen.quantity!),
              reservedQuantity: 0,
            })
            result.push(balance)
          }
          return result
        }
        facts.warehouseInventory = await balances()

        const issues: Row[] = []
        facts.materialIssues = issues
        const consumedMaterialLots: Api.BusinessConsoleMesConsumedMaterialLot[] = []
        for (const order of orders) {
          const line = order.requirement
          const materialLotId = `LOT-${order.purchaseOrderNo.replace('PO-', '')}`
          const issue = await call<Api.BusinessConsoleAcceptedResponse>(
            'POST',
            query(`${mes}/work-orders/${workOrderId}/material-issue-requests`, workScope),
            {
              materialId: line.skuCode,
              uomCode: line.unitOfMeasureCode,
              quantity: order.quantity,
              idempotencyKey: `n1853-issue-${line.skuCode}`,
            } satisfies Api.BusinessConsoleMesCreateMaterialIssueRequest,
          )
          const issueNo = issue.downstreamDocumentId!
          await call(
            'POST',
            query(`${mes}/material-issue-requests/${issueNo}/line-side-receipts`, workScope),
            {
              materialLotId,
              receivedQuantity: order.quantity,
              idempotencyKey: `n1853-line-${line.skuCode}`,
            },
          )
          const readIssue = () =>
            call<Api.BusinessConsoleMesMaterialIssueRequestRow>(
              'GET',
              query(`${mes}/material-issue-requests/${issueNo}`, workScope),
            )
          await expect
            .poll(async () => (await readIssue()).receivedQuantity, { timeout: 60000 })
            .toBe(order.quantity)
          issues.push({
            issue: await readIssue(),
            purchaseReceiptNo: order.purchaseReceiptNo,
            materialLotId,
          })
          consumedMaterialLots.push({
            materialId: line.skuCode,
            materialLotId,
            consumedQuantity: order.quantity,
            uomCode: line.unitOfMeasureCode,
            materialIssueRequestNo: issueNo,
          })
        }
        facts.beforeReleaseInventory = await balances()
        const readiness = () =>
          call<Api.BusinessConsoleMesMaterialReadinessResponse>(
            'GET',
            query(`${mes}/work-orders/${workOrderId}/material-readiness`, workScope),
          )
        facts.beforeReleaseReadiness = await readiness()
        facts.release = await call(
          'POST',
          query(`${mes}/work-orders/${workOrderId}/release`, workScope),
          {},
        )
        const released = await detail()
        expect(released.status?.toLowerCase()).toBe('released')
        facts.releasedWorkOrder = released
        facts.afterReleaseInventory = await balances()
        const materialReadiness = await readiness()
        facts.afterReleaseReadiness = materialReadiness
        expect(materialReadiness.items).toHaveLength(requirements.length)
        for (const line of requirements) {
          expect(
            materialReadiness.items!.filter((item) => item.materialId === line.skuCode),
          ).toEqual([
            expect.objectContaining({
              requiredQuantity: calculateRequiredQuantity(line, frozen.quantity!),
              shortageQuantity: 0,
            }),
          ])
        }
        const tasks = [...released.operationTasks!].sort(
          (a, b) => a.operationSequence! - b.operationSequence!,
        )
        const preparation: Row[] = []
        const reports: Api.BusinessConsoleRecordProductionReportResponse[] = []
        facts.preparation = preparation
        facts.reports = reports
        for (const [index, task] of tasks.entries()) {
          // 与 NERV-2115 相同的公开配置路径；隔离演示人工费率，不代表客户工资标准。
          const rates = '/api/business-console/v1/erp/finance/work-center-cost-rates'
          await call('POST', rates, {
            ...scope,
            workCenterId: task.workCenterId!,
            hourlyRate: 60,
            currencyCode: 'CNY',
            effectiveFromUtc: new Date(Date.now() - 86400000).toISOString(),
            effectiveToUtc: new Date(Date.now() + 86400000).toISOString(),
            reason: 'NERV-1853 隔离走查测试专用模拟人工费率，不用于生产核算',
          } satisfies Api.BusinessConsoleConfigureErpWorkCenterCostRateRequest)
          const rate = await call<Api.BusinessConsoleErpWorkCenterCostRateListResponse>(
            'GET',
            query(rates, { workCenterId: task.workCenterId, atUtc: new Date().toISOString() }),
          )
          expect(rate.items).toHaveLength(1)
          expect(rate.items![0]).toMatchObject({
            hourlyRate: 60,
            currencyCode: 'CNY',
            isCurrentEffectiveRevision: true,
          })
          const centers = await call<Api.BusinessConsoleResourceListResponse>(
            'GET',
            query('/api/business-console/v1/master-data/resources', {
              resourceType: 'work-center',
            }),
          )
          const centersForTask = centers.resources!.filter(
            (item) => item.code === task.workCenterId,
          )
          expect(centersForTask).toHaveLength(1)
          const center = centersForTask[0]
          const deviceCode = `DEV-N1853-${index + 1}`
          await call('POST', '/api/business-console/v1/master-data/device-assets', {
            ...scope,
            code: deviceCode,
            model: '成品走查模拟设备',
            lineCode: center.lineCode!,
            workCenterCode: task.workCenterId!,
            assetClassCode: 'machine',
            manufacturer: '隔离走查模拟设备',
            serialNo: `SN-N1853-${index + 1}`,
            capacityUomCode: 'pcs',
            criticality: 'normal',
            maintainable: true,
            telemetryEnabled: false,
            siteCode: 'SITE-001',
            workshopCode: center.workshopCode,
            externalReferences: { purpose: 'NERV-1853 测试专用' },
            idempotencyKey: `n1853-device-${index}`,
          } satisfies Api.BusinessConsoleRegisterDeviceAssetRequest)
          const devices = await call<Api.BusinessConsoleResourceListResponse>(
            'GET',
            query('/api/business-console/v1/master-data/device-assets', {
              workCenterCode: task.workCenterId,
              keyword: deviceCode,
              take: 100,
            }),
          )
          expect(devices.resources).toHaveLength(1)
          const device = devices.resources![0]
          await call(
            'POST',
            query(`${mes}/dispatch-tasks/${task.operationTaskId}/assign`, workScope),
            {
              deviceAssetId: device.deviceAssetId,
            } satisfies Api.BusinessConsoleMesAssignDispatchTaskRequest,
          )
          const assigned = (await detail()).operationTasks!.find(
            (item) => item.operationTaskId === task.operationTaskId,
          )!
          expect(assigned.deviceAssetId).toBe(device.deviceAssetId)
          preparation.push({ rate, device, assigned })
          await call(
            'POST',
            query(`${mes}/operation-tasks/${task.operationTaskId}/start`, workScope),
            {
              idempotencyKey: `n1853-start-${index}`,
            },
          )
          const reportRequest = {
            ...scope,
            ...workScope,
            workOrderId,
            operationTaskId: task.operationTaskId!,
            goodQuantity: frozen.quantity!,
            scrapQuantity: 0,
            reworkQuantity: 0,
            completesOperation: true,
            reportedAtUtc: new Date().toISOString(),
            idempotencyKey: `n1853-report-${index}`,
            consumedMaterialLots: index === 0 ? consumedMaterialLots : [],
            producedLotNo: index === tasks.length - 1 ? producedLotNo : undefined,
          } satisfies Api.BusinessConsoleRecordProductionReportRequest
          reports.push(
            await call<Api.BusinessConsoleRecordProductionReportResponse>(
              'POST',
              `${mes}/production-reports`,
              reportRequest,
            ),
          )
          await expect
            .poll(
              async () =>
                (await detail()).operationTasks!.find(
                  (item) => item.operationTaskId === task.operationTaskId,
                )?.status,
              { timeout: 60000 },
            )
            .toBe('Completed')
        }
        facts.completedWorkOrder = await detail()
        for (const material of consumedMaterialLots) {
          const issue = await call<Api.BusinessConsoleMesMaterialIssueRequestRow>(
            'GET',
            query(`${mes}/material-issue-requests/${material.materialIssueRequestNo}`, workScope),
          )
          expect(issue.consumedQuantity).toBe(material.consumedQuantity)
        }
      },
    })
    return facts
  } finally {
    await context.close()
  }
}
