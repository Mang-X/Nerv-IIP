import { expect, test } from '@playwright/test'
import path from 'node:path'
import type * as Api from '@nerv-iip/api-client'
import { runWarehouseSupply } from './warehouseSupplyScenario'
import type { Row } from './procurementScenario'
import {
  calculateRequiredQuantity,
  selectConcreteMaterialLines,
  type MbomMaterialLineFact,
} from '../src/issue1851MbomInventoryBaseline'

const evidencePath = process.env.NERV_IIP_NERV2115_EVIDENCE_PATH
const scenario = process.env.NERV_IIP_NERV2115_SCENARIO!
const scope = { organizationId: 'org-001', environmentId: 'env-dev' }
const workScope = { scopeKind: 'organization', scopeId: 'org-001' }
const mes = '/api/business-console/v1/mes'
const planning = '/api/business-console/v1/planning'
test.skip(!evidencePath, 'NERV-2115 requires an explicitly selected managed FullStack session')
test.use({ trace: 'off', screenshot: 'off' })
test.setTimeout(15 * 60 * 1000)

test('NERV-2115 隔离外购与活塞杆自制供给满足同一冻结需求', async ({ page, browser }) => {
  await runWarehouseSupply({
    page,
    browser,
    issue: 'NERV-2115',
    scenario,
    evidencePath: evidencePath!,
    headSha: process.env.NERV_IIP_NERV2115_HEAD_SHA!,
    sessionId: process.env.NERV_IIP_NERV2115_SESSION_ID!,
    storageLocation: (sku) => (sku === 'RM-BAR-01' ? 'loc-raw-01' : 'loc-semi-01'),
    afterSupply: async ({ call, query, report, orders }) => {
      const mbom = await call<Api.BusinessConsoleManufacturingBomItem>(
        'GET',
        query('/api/business-console/v1/engineering/manufacturing-boms/MBOM-FG-QJ-P1-L/2'),
      )
      const requirements = selectConcreteMaterialLines(mbom.materialLines as MbomMaterialLineFact[])
      const rod = requirements.find((line) => line.skuCode === 'SF-ROD-01')!
      const rodQuantity = calculateRequiredQuantity(rod, 1)
      let receiptNo: string | undefined
      if (scenario === 'mixed') {
        expect(orders.filter((order) => order.requirement.skuCode === rod.skuCode)).toHaveLength(0)
        const raw = orders.filter((order) => order.requirement.skuCode === 'RM-BAR-01')
        expect(raw).toHaveLength(1)
        const dueDate = new Date(Date.now() + 7 * 86400000).toISOString().slice(0, 10)
        const demand = await call<Api.BusinessConsoleDemandSourceItem>(
          'POST',
          `${planning}/demands`,
          {
            ...scope,
            demandType: 'manual',
            sourceReference: 'DEMAND-N2115-mixed-ROD',
            skuCode: rod.skuCode,
            uomCode: rod.unitOfMeasureCode,
            siteCode: 'SITE-001',
            quantity: rodQuantity,
            dueDate,
            idempotencyKey: 'n2115-mixed-rod-demand',
          } satisfies Api.BusinessConsoleCreateOrUpdateDemandSourceRequest,
        )
        report.rodDemand = demand
        const run = await call<Api.BusinessConsoleRunMrpResponse>('POST', `${planning}/mrp-runs`, {
          ...scope,
          horizonStart: new Date().toISOString().slice(0, 10),
          horizonEnd: dueDate,
        } satisfies Api.BusinessConsoleRunMrpRequest)
        report.mrpRun = run
        await expect
          .poll(
            async () => {
              const runs = await call<{ items: Api.BusinessConsoleMrpRunItem[] }>(
                'GET',
                query(`${planning}/mrp-runs`),
              )
              const current = runs.items.find((item) => item.runId === run.runId)
              report.mrpRunReadback = current
              return current?.status?.toLowerCase()
            },
            { timeout: 60000 },
          )
          .toBe('completed')
        const suggestions = await call<{ items: Api.BusinessConsolePlanningSuggestionItem[] }>(
          'GET',
          query(`${planning}/suggestions`, { runId: run.runId! }),
        )
        report.suggestions = suggestions
        const rods = suggestions.items.filter((item) => item.skuCode === rod.skuCode)
        expect(rods).toHaveLength(1)
        expect(rods[0]).toMatchObject({
          suggestionType: 'planned-work-order',
          quantity: rodQuantity,
        })
        const accepted = await call<Api.BusinessConsolePlanningSuggestionItem>(
          'POST',
          query(`${planning}/suggestions/${rods[0].suggestionId}/accept`),
          {
            downstreamService: 'BusinessMes',
            downstreamDocumentType: 'WorkOrder',
            idempotencyKey: 'n2115-mixed-rod-plan',
          } satisfies Api.BusinessConsoleAcceptPlanningSuggestionRequest,
        )
        report.acceptedPlan = accepted
        const workOrderId = accepted.downstreamDocumentId!
        expect(workOrderId).toMatch(/\S/)
        const detail = () =>
          call<Api.BusinessConsoleMesWorkOrderDetailResponse>(
            'GET',
            query(`${mes}/work-orders/${workOrderId}`, workScope),
          )
        const frozen = await detail()
        report.rodWorkOrder = frozen
        const versions = await call<{ items: Api.BusinessConsoleProductionVersionItem[] }>(
          'GET',
          query('/api/business-console/v1/engineering/production-versions', {
            skuCode: rod.skuCode,
          }),
        )
        const version = versions.items.filter(
          (item) => item.productionVersionId === frozen.productionVersionId,
        )
        expect(version).toHaveLength(1)
        expect(version[0]).toMatchObject({
          skuCode: rod.skuCode,
          mbomVersionId: 'MBOM-SF-ROD-01:1',
          routingVersionId: 'ROUTING-SF-ROD-01:1',
        })
        report.frozenEngineeringVersion = version[0]
        expect(frozen).toMatchObject({ skuId: rod.skuCode, quantity: rodQuantity })
        expect(frozen.operationTasks).toHaveLength(3)
        expect(frozen.sourcePlanReference?.sourceDocumentId).toBe(rods[0].suggestionId)
        await call('POST', query(`${mes}/work-orders/${workOrderId}/release`, workScope), {})
        report.releasedRod = await detail()
        const material = await call<Api.BusinessConsoleMesMaterialReadinessResponse>(
          'GET',
          query(`${mes}/work-orders/${workOrderId}/material-readiness`, workScope),
        )
        report.rodMaterialRequirement = material
        expect(material.items).toHaveLength(1)
        expect(material.items![0]).toMatchObject({
          materialId: 'RM-BAR-01',
          requiredQuantity: raw[0].quantity,
        })
        const tasks = [...frozen.operationTasks!].sort(
          (a, b) => a.operationSequence! - b.operationSequence!,
        )
        expect(tasks.map((task) => task.workCenterId)).toEqual([
          'WC-TUB-01',
          'WC-ROD-01',
          'WC-GRD-01',
        ])
        const rawLot = `LOT-${raw[0].purchaseOrderNo.replace('PO-', '')}`
        const issueRequest = {
          materialId: 'RM-BAR-01',
          uomCode: 'kg',
          quantity: raw[0].quantity,
          idempotencyKey: 'n2115-mixed-raw-issue',
        } satisfies Api.BusinessConsoleMesCreateMaterialIssueRequest
        const issue = await call<Api.BusinessConsoleAcceptedResponse>(
          'POST',
          query(`${mes}/work-orders/${workOrderId}/material-issue-requests`, workScope),
          issueRequest,
        )
        expect(issue.downstreamDocumentId).toMatch(/\S/)
        expect(
          (
            await call<Api.BusinessConsoleAcceptedResponse>(
              'POST',
              query(`${mes}/work-orders/${workOrderId}/material-issue-requests`, workScope),
              issueRequest,
            )
          ).downstreamDocumentId,
        ).toBe(issue.downstreamDocumentId)
        const readIssue = () =>
          call<Api.BusinessConsoleMesMaterialIssueRequestRow>(
            'GET',
            query(`${mes}/material-issue-requests/${issue.downstreamDocumentId}`, workScope),
          )
        report.materialIssue = await readIssue()
        await call(
          'POST',
          query(
            `${mes}/material-issue-requests/${issue.downstreamDocumentId}/line-side-receipts`,
            workScope,
          ),
          {
            materialLotId: rawLot,
            receivedQuantity: raw[0].quantity,
            idempotencyKey: 'n2115-mixed-line-receipt',
          },
        )
        await expect
          .poll(async () => (await readIssue()).receivedQuantity, { timeout: 60000 })
          .toBe(raw[0].quantity)
        report.lineSideReceipt = await readIssue()
        const productionReports: Api.BusinessConsoleRecordProductionReportResponse[] = []
        report.productionReports = productionReports
        const producedLotNo = 'LOT-N2115-mixed-ROD'
        // 用户批准的隔离演示数据；不是行业工资或客户生产费率。
        const rateWindow = {
          effectiveFromUtc: new Date(Date.now() - 86_400_000).toISOString(),
          effectiveToUtc: new Date(Date.now() + 86_400_000).toISOString(),
        }
        const preparation: Row[] = []
        report.productionPreparation = preparation
        for (const [index, task] of tasks.entries()) {
          const rates = '/api/business-console/v1/erp/finance/work-center-cost-rates'
          const rate = {
            ...scope,
            workCenterId: task.workCenterId!,
            hourlyRate: [60, 90, 75][index],
            currencyCode: 'CNY',
            ...rateWindow,
            reason: 'NERV-2115 隔离走查测试专用模拟人工费率，不用于生产核算',
          } satisfies Api.BusinessConsoleConfigureErpWorkCenterCostRateRequest
          await call('POST', rates, rate)
          const rateReadback = await call<Api.BusinessConsoleErpWorkCenterCostRateListResponse>(
            'GET',
            query(rates, { workCenterId: task.workCenterId, atUtc: new Date().toISOString() }),
          )
          expect(rateReadback.items).toHaveLength(1)
          expect(rateReadback.items![0]).toMatchObject({
            hourlyRate: rate.hourlyRate,
            currencyCode: 'CNY',
            isCurrentEffectiveRevision: true,
          })
          const centers = await call<Api.BusinessConsoleResourceListResponse>(
            'GET',
            query('/api/business-console/v1/master-data/resources', {
              resourceType: 'work-center',
            }),
          )
          const center = centers.resources!.filter((item) => item.code === task.workCenterId)
          expect(center).toHaveLength(1)
          const deviceCode = `DEV-N2115-${index + 1}`
          await call('POST', '/api/business-console/v1/master-data/device-assets', {
            ...scope,
            code: deviceCode,
            model: ['走查切断机', '走查数控车床', '走查外圆磨床'][index],
            lineCode: center[0].lineCode!,
            workCenterCode: task.workCenterId!,
            assetClassCode: 'machine',
            manufacturer: '隔离走查模拟设备',
            serialNo: `SN-N2115-${index + 1}`,
            capacityUomCode: 'pcs',
            criticality: 'normal',
            maintainable: true,
            telemetryEnabled: false,
            siteCode: 'SITE-001',
            workshopCode: center[0].workshopCode,
            externalReferences: { purpose: 'NERV-2115 测试专用' },
            idempotencyKey: `n2115-device-${index}`,
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
          expect(device.deviceAssetId).toMatch(/\S/)
          await call(
            'POST',
            query(`${mes}/dispatch-tasks/${task.operationTaskId}/assign`, workScope),
            {
              deviceAssetId: device.deviceAssetId,
              idempotencyKey: `n2115-dispatch-${index}`,
            } satisfies Api.BusinessConsoleMesAssignDispatchTaskRequest,
          )
          const assigned = (await detail()).operationTasks!.find(
            (item) => item.operationTaskId === task.operationTaskId,
          )!
          expect(assigned.deviceAssetId).toBe(device.deviceAssetId)
          preparation.push({ rate, rateReadback, device, assigned })
          await call(
            'POST',
            query(`${mes}/operation-tasks/${task.operationTaskId}/start`, workScope),
            { idempotencyKey: `n2115-mixed-start-${index}` },
          )
          const request = {
            ...scope,
            ...workScope,
            workOrderId,
            operationTaskId: task.operationTaskId!,
            goodQuantity: rodQuantity,
            scrapQuantity: 0,
            reworkQuantity: 0,
            completesOperation: true,
            reportedAtUtc: new Date().toISOString(),
            idempotencyKey: `n2115-mixed-report-${index}`,
            consumedMaterialLots:
              index === 0
                ? [
                    {
                      materialId: 'RM-BAR-01',
                      materialLotId: rawLot,
                      consumedQuantity: raw[0].quantity,
                      materialIssueRequestNo: issue.downstreamDocumentId!,
                    },
                  ]
                : [],
            producedLotNo: index === tasks.length - 1 ? producedLotNo : undefined,
          } satisfies Api.BusinessConsoleRecordProductionReportRequest
          const result = await call<Api.BusinessConsoleRecordProductionReportResponse>(
            'POST',
            `${mes}/production-reports`,
            request,
          )
          expect(
            (
              await call<Api.BusinessConsoleRecordProductionReportResponse>(
                'POST',
                `${mes}/production-reports`,
                request,
              )
            ).productionReportId,
          ).toBe(result.productionReportId)
          productionReports.push(result)
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
        report.completedRod = await detail()
        expect((await readIssue()).consumedQuantity).toBe(raw[0].quantity)
        const rawAvailability = () =>
          call<Api.BusinessConsoleInventoryAvailabilityResponse>(
            'GET',
            query('/api/business-console/v1/inventory/availability', {
              siteCode: 'SITE-001',
              skuCode: 'RM-BAR-01',
              uomCode: 'kg',
              lotNo: rawLot,
            }),
          )
        await expect
          .poll(async () => (await rawAvailability()).onHandQuantity, { timeout: 60000 })
          .toBe(0)
        report.consumedRawBalance = await rawAvailability()
        const rawMovements = await call<Api.BusinessConsoleInventoryMovementListResponse>(
          'GET',
          query('/api/business-console/v1/inventory/movements', {
            siteCode: 'SITE-001',
            skuCode: 'RM-BAR-01',
            uomCode: 'kg',
            lotNo: rawLot,
            page: 1,
            pageSize: 100,
          }),
        )
        expect(rawMovements.totalCount).toBe(4)
        expect(rawMovements.items!.map((item) => item.quantity).sort()).toEqual(
          [-raw[0].quantity, -raw[0].quantity, raw[0].quantity, raw[0].quantity].sort(),
        )
        report.rawMovements = rawMovements
        const reportReadback = await call<Api.BusinessConsoleMesProductionReportListResponse>(
          'GET',
          query(`${mes}/production-reports`, { ...workScope, workOrderId, take: 100 }),
        )
        expect(reportReadback.items).toHaveLength(3)
        report.productionReportReadback = reportReadback
        const receiptRequest = {
          ...scope,
          workOrderId,
          skuId: rod.skuCode,
          quantity: rodQuantity,
          uomCode: rod.unitOfMeasureCode,
          requestedAtUtc: new Date().toISOString(),
          idempotencyKey: 'n2115-mixed-rod-receipt',
          producedLotNo,
        } satisfies Api.BusinessConsoleMesCreateReceiptRequest
        const receipt = await call<Api.BusinessConsoleMesCreateReceiptResponse>(
          'POST',
          `${mes}/finished-goods-receipt-requests`,
          receiptRequest,
        )
        report.rodReceipt = receipt
        receiptNo = receipt.requestNo!
        expect(
          await call<Api.BusinessConsoleMesCreateReceiptResponse>(
            'POST',
            `${mes}/finished-goods-receipt-requests`,
            receiptRequest,
          ),
        ).toEqual(receipt)
        const inventoryLink = () =>
          call<Api.BusinessConsoleMesFinishedGoodsInventoryLinkResponse>(
            'GET',
            query(`${mes}/finished-goods-receipt-requests/${receipt.requestNo}/inventory-link`),
          )
        await expect
          .poll(async () => (await inventoryLink()).receiptStatus?.toLowerCase(), {
            timeout: 60000,
          })
          .toBe('posted')
        const posted = await inventoryLink()
        report.rodInventoryLink = posted
        expect(posted).toMatchObject({
          workOrderId,
          skuId: rod.skuCode,
          producedLotNo,
          requestedQuantity: rodQuantity,
          postedQuantity: rodQuantity,
          isInventoryLinkEstablished: true,
        })
        expect(posted.movements).toHaveLength(1)
        expect(posted.movements![0]).toMatchObject({
          sourceDocumentId: receipt.requestNo,
          skuCode: rod.skuCode,
          uomCode: rod.unitOfMeasureCode,
          siteCode: 'SITE-001',
          lotNo: producedLotNo,
          quantity: rodQuantity,
        })
        expect(posted.movements![0].movementId).toMatch(/\S/)
        expect(posted.movements![0].postedAtUtc).toMatch(/\S/)
        expect(
          await call<Api.BusinessConsoleMesCreateReceiptResponse>(
            'POST',
            `${mes}/finished-goods-receipt-requests`,
            receiptRequest,
          ),
        ).toEqual(receipt)
        expect(await inventoryLink()).toEqual(posted)
      }
      report.supplyRequirements = requirements.map((line) => ({
        skuCode: line.skuCode,
        quantity: calculateRequiredQuantity(line, 1),
      }))
      const balances: Api.BusinessConsoleInventoryAvailabilityResponse[] = []
      for (const line of requirements) {
        const balance = await call<Api.BusinessConsoleInventoryAvailabilityResponse>(
          'GET',
          query('/api/business-console/v1/inventory/availability', {
            siteCode: 'SITE-001',
            skuCode: line.skuCode,
            uomCode: line.unitOfMeasureCode,
          }),
        )
        balances.push(balance)
        expect(balance).toMatchObject({
          onHandQuantity: calculateRequiredQuantity(line, 1),
          availableQuantity: calculateRequiredQuantity(line, 1),
          reservedQuantity: 0,
        })
        expect(balance.items).toHaveLength(1)
      }
      expect(balances).toHaveLength(11)
      report.completeSupplyBalances = balances
      report.supplyConfirmed = true
      if (receiptNo) {
        const receiptResponse = page.waitForResponse(
          (response) =>
            new URL(response.url()).pathname === `${mes}/finished-goods-receipt-requests` &&
            response.request().method() === 'GET',
        )
        await page.goto('/mes/receipts')
        expect((await receiptResponse).status()).toBe(200)
        await expect(page.getByRole('row').filter({ hasText: receiptNo })).toBeVisible()
        await page.screenshot({
          path: path.join(path.dirname(evidencePath!), 'mixed-mes-receipt.png'),
          fullPage: true,
        })
      }
    },
  })
})
