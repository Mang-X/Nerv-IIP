import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import { mount } from '@vue/test-utils'
import { shallowRef, type ComputedRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AuthoritativeMaintenanceWorkOrderDetail } from '@/composables/useMaintenanceSelfWorkOrders'

const state = vi.hoisted(() => ({
  scopeReady: true,
  pending: false,
  failed: false,
  error: undefined as unknown,
  workOrder: undefined as AuthoritativeMaintenanceWorkOrderDetail | undefined,
  device: undefined as BusinessConsoleResourceItem | undefined,
  requestedId: undefined as ComputedRef<string> | undefined,
  refresh: vi.fn(),
}))

vi.mock('@/composables/useMaintenanceSelfWorkOrders', () => ({
  useMaintenanceSelfWorkOrderDetail: (requestedId: ComputedRef<string>) => {
    state.requestedId = requestedId
    return {
      scopeReady: shallowRef(state.scopeReady),
      pending: shallowRef(state.pending),
      error: shallowRef(state.error),
      hasFailedResponse: shallowRef(state.failed),
      workOrder: shallowRef(state.workOrder),
      device: shallowRef(state.device),
      refresh: state.refresh,
    }
  },
}))

import WorkOrderDetailPage from './[workOrderId].vue'

function authoritativeWorkOrder(
  overrides: Partial<AuthoritativeMaintenanceWorkOrderDetail> = {},
): AuthoritativeMaintenanceWorkOrderDetail {
  return {
    workOrderId: 'WO-DETAIL',
    deviceAssetId: 'device-1',
    priority: 'medium',
    status: 'open',
    openedAtUtc: '2026-08-02T01:00:00.000Z',
    version: 1,
    allowedActions: [],
    blockReasons: [],
    lifecycle: [],
    assignedTechnicianUserId: 'principal-1',
    assignedTeamId: null,
    ...overrides,
  }
}

async function mountPage(path = '/equipment/work-orders/WO-DETAIL') {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/equipment/work-orders', component: { template: '<div>list</div>' } },
      { path: '/equipment/work-orders/:workOrderId', component: WorkOrderDetailPage },
    ],
  })
  await router.push(path)
  await router.isReady()
  return mount(WorkOrderDetailPage, { global: { plugins: [router] } })
}

describe('maintenance work-order authoritative detail page', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    Object.assign(state, {
      scopeReady: true,
      pending: false,
      failed: false,
      error: undefined,
      workOrder: undefined,
      device: undefined,
      requestedId: undefined,
    })
  })

  it('revalidates route context and renders every required authoritative read field', async () => {
    state.workOrder = authoritativeWorkOrder({
      sourceReferenceId: 'MWO-2026-0042',
      priority: 'high',
      status: 'accepted',
      assignedTechnicianUserId: 'principal-1',
      assignedTeamId: 'team-a',
      sourceAlarmId: 'ALM-9',
      version: 7,
      allowedActions: ['start', 'cancel'],
      blockReasons: ['manage-permission-required'],
      lifecycle: [
        {
          action: 'accept',
          fromStatus: 'open',
          toStatus: 'accepted',
          actorPrincipalId: 'principal-1',
          technicianUserId: 'principal-1',
          teamId: 'team-a',
          reason: '现场接单',
          resultingVersion: 7,
          occurredAtUtc: '2026-08-02T01:02:03.000Z',
        },
      ],
    })
    state.device = {
      deviceAssetId: 'device-1',
      code: 'CNC-01',
      displayName: '一号数控机床',
      workshopCode: 'WS-1',
      lineCode: 'LINE-A',
      stationCode: 'ST-9',
    }

    const wrapper = await mountPage(
      '/equipment/work-orders/WO-DETAIL?source=repair&sourceAlarmId=ALM-9',
    )

    expect(state.requestedId?.value).toBe('WO-DETAIL')
    expect(wrapper.text()).toContain('一号数控机床')
    expect(wrapper.text()).toContain('WS-1 · LINE-A · ST-9')
    expect(wrapper.text()).toContain('高')
    expect(wrapper.text()).toContain('维修人员 principal-1')
    expect(wrapper.text()).toContain('班组 team-a')
    expect(wrapper.text()).toContain('操作人 principal-1')
    expect(wrapper.text()).toContain('技师快照 principal-1')
    expect(wrapper.text()).toContain('班组快照 team-a')
    expect(wrapper.text()).toContain('来源：报警报修创建结果')
    expect(wrapper.text()).not.toContain('WO-DETAIL')
    expect(wrapper.text()).not.toContain('device-1')
    expect(wrapper.text()).toContain('版本 7')
    expect(wrapper.text()).toContain('开工')
    expect(wrapper.text()).toContain('取消')
    expect(wrapper.text()).toContain('当前账号没有维护动作权限')
    expect(wrapper.text()).toContain('现场接单')
    expect(wrapper.text()).toContain('待处理 → 已接单')
    expect(wrapper.findAll('button').some((button) => button.text() === '开工')).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === '取消')).toBe(false)
  })

  it.each([
    [{}, '维修人员 principal-1 · 未指派班组'],
    [{ assignedTeamId: 'team-a' }, '维修人员 principal-1 · 班组 team-a'],
  ])('renders stable self assignment identifiers', async (assignment, label) => {
    state.workOrder = authoritativeWorkOrder({
      ...assignment,
    })

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain(label)
  })

  it('does not trust an editable source alarm query that differs from the work order', async () => {
    state.workOrder = authoritativeWorkOrder({ sourceAlarmId: 'ALM-AUTHORITATIVE' })

    const wrapper = await mountPage(
      '/equipment/work-orders/WO-DETAIL?source=repair&sourceAlarmId=ALM-EDITED',
    )

    expect(wrapper.find('[data-testid="maintenance-source-context"]').exists()).toBe(false)
  })

  it('does not claim ordinary repair source context without an authoritative alarm link', async () => {
    state.workOrder = authoritativeWorkOrder()

    const wrapper = await mountPage('/equipment/work-orders/WO-DETAIL?source=repair')

    expect(wrapper.find('[data-testid="maintenance-source-context"]').exists()).toBe(false)
  })

  it('shows a validated terminal work order as read-only with no stale actions', async () => {
    state.workOrder = authoritativeWorkOrder({
      status: 'closed',
      version: 12,
      allowedActions: [],
      blockReasons: ['terminal-status'],
    })

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('终态只读')
    expect(wrapper.text()).toContain('工单已进入终态，仅可查看')
    expect(wrapper.findAll('button').some((button) => button.text() === '开工')).toBe(false)
  })

  it('uses actionable account guidance when the detail cannot be queried', async () => {
    state.scopeReady = false

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain('当前账号暂无法查看，请重新登录或联系管理员')
    for (const diagnostic of [
      'Self',
      '服务端',
      '已授权',
      '不可解析',
      '未发起查询',
      '组织/环境',
      '读取权限',
    ]) {
      expect(wrapper.text()).not.toContain(diagnostic)
    }
  })

  it('fails closed for forbidden, invalid, or unmatched IDs without rendering old detail', async () => {
    state.failed = true
    state.error = { status: 403 }

    const wrapper = await mountPage('/equipment/work-orders/invalid-id')

    expect(state.requestedId?.value).toBe('invalid-id')
    expect(wrapper.text()).toContain('工单不可查看')
    expect(wrapper.text()).toContain('当前账号不可查看')
    expect(wrapper.text()).not.toContain('Self')
    expect(wrapper.text()).not.toContain('服务端')
    expect(wrapper.text()).not.toContain('MWO-2026-0042')
  })
})
