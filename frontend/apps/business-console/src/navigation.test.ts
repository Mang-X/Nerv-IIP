import { describe, expect, it } from 'vitest'

import { DOMAIN_SIDE_NAV, resolveDomainId } from './navigation'
import { BUSINESS_PERMISSION_CODES as P } from './permissions'

describe('business console quality navigation', () => {
  it('puts the inspection task workbench first and aligns its read permission', () => {
    const qualityItems = DOMAIN_SIDE_NAV.quality?.flatMap((section) => section.items) ?? []
    const taskWorkbench = qualityItems.find(
      (item) => pathOf(item.to) === '/quality/inspection-tasks',
    )

    expect(resolveDomainId('/quality/inspection-tasks')).toBe('quality')
    expect(qualityItems[0]?.title).toBe('待检工作台')
    expect(taskWorkbench?.requiredPermissions).toEqual([P.qualityInspectionRecordsRead])
  })
})

describe('business console scheduling navigation', () => {
  it('places the APS workbench under demand and planning', () => {
    const planningItems = DOMAIN_SIDE_NAV.planning?.flatMap((section) => section.items) ?? []
    const scheduling = planningItems.find((item) => pathOf(item.to) === '/scheduling')

    expect(resolveDomainId('/scheduling')).toBe('planning')
    expect(scheduling?.title).toBe('排产工作台')
    expect(scheduling?.requiredPermissions).toEqual([P.schedulingPlansRead])
  })

  it('keeps MES rule scheduling labeled as a transitional entry', () => {
    const mesItems = DOMAIN_SIDE_NAV.mes?.flatMap((section) => section.items) ?? []
    const mesRuleScheduling = mesItems.find((item) => pathOf(item.to) === '/mes/schedules')

    expect(resolveDomainId('/mes/schedules')).toBe('mes')
    expect(mesRuleScheduling?.title).toBe('规则排程（过渡）')
    expect(mesRuleScheduling?.requiredPermissions).toEqual([
      P.mesSchedulesRead,
      P.mesSchedulesManage,
    ])
  })
})

describe('business console maintenance navigation', () => {
  it('keeps CMMS deep pages under the equipment domain side navigation', () => {
    const equipmentItems = DOMAIN_SIDE_NAV.equipment?.flatMap((section) => section.items) ?? []
    const maintenancePaths = equipmentItems
      .map((item) => pathOf(item.to))
      .filter((path) => path.startsWith('/maintenance'))

    expect(resolveDomainId('/maintenance/inspections')).toBe('equipment')
    expect(resolveDomainId('/maintenance/spare-parts')).toBe('equipment')
    expect(resolveDomainId('/maintenance/reliability')).toBe('equipment')
    expect(resolveDomainId('/maintenance/availability')).toBe('equipment')
    expect(maintenancePaths).toEqual([
      '/maintenance/work-orders',
      '/maintenance/plans',
      '/maintenance/inspections',
      '/maintenance/spare-parts',
      '/maintenance/reliability',
      '/maintenance/availability',
    ])
  })
})

describe('business console telemetry navigation', () => {
  it('groups formal telemetry pages under equipment monitoring', () => {
    const equipmentItems = DOMAIN_SIDE_NAV.equipment?.flatMap((section) => section.items) ?? []
    const paths = equipmentItems.map((item) => pathOf(item.to))

    expect(resolveDomainId('/equipment/telemetry/tags')).toBe('equipment')
    expect(resolveDomainId('/equipment/telemetry/alarm-rules')).toBe('equipment')
    expect(resolveDomainId('/equipment/telemetry/history')).toBe('equipment')
    expect(resolveDomainId('/equipment/telemetry/oee')).toBe('equipment')
    expect(paths).toContain('/equipment/telemetry/tags')
    expect(paths).toContain('/equipment/telemetry/alarm-rules')
    expect(paths).toContain('/equipment/telemetry/history')
    expect(paths).toContain('/equipment/telemetry/oee')
  })

  it('uses read permissions for telemetry analysis and manage permission for alarm-rule maintenance', () => {
    const equipmentItems = DOMAIN_SIDE_NAV.equipment?.flatMap((section) => section.items) ?? []
    const tags = equipmentItems.find((item) => pathOf(item.to) === '/equipment/telemetry/tags')
    const alarmRules = equipmentItems.find(
      (item) => pathOf(item.to) === '/equipment/telemetry/alarm-rules',
    )
    const history = equipmentItems.find(
      (item) => pathOf(item.to) === '/equipment/telemetry/history',
    )
    const oee = equipmentItems.find((item) => pathOf(item.to) === '/equipment/telemetry/oee')

    expect(tags?.requiredPermissions).toEqual([P.iiotTelemetryRead])
    expect(alarmRules?.requiredPermissions).toEqual([P.iiotAlarmsRead, P.iiotAlarmRulesManage])
    expect(history?.requiredPermissions).toEqual([P.iiotTelemetryRead])
    expect(oee?.requiredPermissions).toEqual([P.iiotTelemetryRead])
  })
})

describe('business console approval navigation', () => {
  it('adds a dedicated approval center domain with read/manage permissions', () => {
    const approvalItems = DOMAIN_SIDE_NAV.approval?.flatMap((section) => section.items) ?? []
    const center = approvalItems.find((item) => pathOf(item.to) === '/approval')

    expect(resolveDomainId('/approval')).toBe('approval')
    expect(resolveDomainId('/approval/chains/chain-001')).toBe('approval')
    expect(center?.title).toBe('审批中心')
    expect(center?.requiredPermissions).toEqual([P.approvalsRead, P.approvalsManage])
  })
})

describe('business console ERP navigation', () => {
  it('splits ERP into formal procurement, sales, and finance business-object pages', () => {
    const erpSections = DOMAIN_SIDE_NAV.erp ?? []
    const items = erpSections.flatMap((section) => section.items)
    const paths = items.map((item) => pathOf(item.to))

    expect(resolveDomainId('/erp/procurement/rfqs')).toBe('erp')
    expect(resolveDomainId('/erp/sales/quotations')).toBe('erp')
    expect(resolveDomainId('/erp/finance/cost-candidates')).toBe('erp')
    expect(paths).toEqual([
      '/erp',
      '/erp/procurement/rfqs',
      '/erp/procurement/supplier-quotations',
      '/erp/procurement/purchase-orders',
      '/erp/procurement/receipts',
      '/erp/sales',
      '/erp/sales/quotations',
      '/erp/sales/orders',
      '/erp/sales/deliveries',
      '/erp/finance',
      '/erp/finance/ar-ap',
      '/erp/finance/vouchers',
      '/erp/finance/cost-candidates',
    ])
  })

  it('uses ERP domain permission codes for every ERP object page', () => {
    const erpSections = DOMAIN_SIDE_NAV.erp ?? []
    const byPath = new Map(
      erpSections
        .flatMap((section) => section.items)
        .map((item) => [pathOf(item.to), item.requiredPermissions]),
    )

    for (const path of [
      '/erp',
      '/erp/procurement/rfqs',
      '/erp/procurement/supplier-quotations',
      '/erp/procurement/purchase-orders',
      '/erp/procurement/receipts',
    ]) {
      expect(byPath.get(path)).toEqual([P.erpProcurementRead])
    }

    for (const path of [
      '/erp/sales',
      '/erp/sales/quotations',
      '/erp/sales/orders',
      '/erp/sales/deliveries',
    ]) {
      expect(byPath.get(path)).toEqual([P.erpSalesRead])
    }

    for (const path of [
      '/erp/finance',
      '/erp/finance/ar-ap',
      '/erp/finance/vouchers',
      '/erp/finance/cost-candidates',
    ]) {
      expect(byPath.get(path)).toEqual([P.erpFinanceRead])
    }
  })
})

describe('business console inventory navigation', () => {
  it('adds the facade-backed lot and reservation page under inventory', () => {
    const inventoryItems = DOMAIN_SIDE_NAV.inventory?.flatMap((section) => section.items) ?? []
    const lots = inventoryItems.find((item) => pathOf(item.to) === '/inventory/lots')

    expect(resolveDomainId('/inventory/lots')).toBe('inventory')
    expect(lots?.title).toBe('批次与预留')
    expect(lots?.requiredPermissions).toEqual([P.inventoryLedgerRead])
  })
})

function pathOf(to: unknown) {
  return typeof to === 'object' && to !== null && 'path' in to ? String(to.path) : ''
}
