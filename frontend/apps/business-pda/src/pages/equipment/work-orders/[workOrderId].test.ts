import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import { flushPromises, mount } from '@vue/test-utils'
import { shallowRef, type ComputedRef } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import type { AuthoritativeMaintenanceWorkOrderDetail } from '@/composables/maintenance'

const state = vi.hoisted(() => ({
  scopeReady: true,
  enabled: true,
  pending: false,
  failed: false,
  error: undefined as unknown,
  workOrder: undefined as AuthoritativeMaintenanceWorkOrderDetail | undefined,
  device: undefined as BusinessConsoleResourceItem | undefined,
  identities: {
    users: { 'principal-1': '张维修', 'principal-2': '李班长' },
    teams: { 'team-a': '甲班', 'team-b': '乙班' },
  },
  identitiesUnavailable: false,
  requestedId: undefined as ComputedRef<string> | undefined,
  refresh: vi.fn(),
  transition: vi.fn(),
  confirm: vi.fn(),
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  transitionBusinessConsoleMaintenanceWorkOrder: state.transition,
  confirmBusinessConsoleOperation: state.confirm,
}))

vi.mock('@/composables/maintenance', () => ({
  useMaintenanceSelfWorkOrderDetail: (requestedId: ComputedRef<string>) => {
    state.requestedId = requestedId
    const workOrder = shallowRef(state.workOrder)
    return {
      scopeReady: shallowRef(state.scopeReady),
      enabled: shallowRef(state.enabled),
      pending: shallowRef(state.pending),
      error: shallowRef(state.error),
      hasFailedResponse: shallowRef(state.failed),
      workOrder,
      authoritativeWorkOrder: workOrder,
      workOrderId: requestedId,
      scopeKey: shallowRef('org:env:principal-1'),
      queryScope: () => ({
        organizationId: 'org',
        environmentId: 'env',
        scopeKind: 'self',
        scopeId: 'principal-1',
      }),
      device: shallowRef(state.device),
      identities: shallowRef(state.identities),
      identityPending: shallowRef(false),
      identitiesUnavailable: shallowRef(state.identitiesUnavailable),
      refresh: async () => {
        await state.refresh()
        workOrder.value = state.workOrder
      },
    }
  },
}))

import WorkOrderDetailPage from './[workOrderId].vue'

function authoritativeWorkOrder(
  overrides: Partial<AuthoritativeMaintenanceWorkOrderDetail> = {},
): AuthoritativeMaintenanceWorkOrderDetail {
  return {
    workOrderId: '019f0000-0000-7000-8000-000000000101',
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

async function mountPage(path = '/equipment/work-orders/019f0000-0000-7000-8000-000000000101') {
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
      enabled: true,
      pending: false,
      failed: false,
      error: undefined,
      workOrder: undefined,
      device: undefined,
      identitiesUnavailable: false,
      requestedId: undefined,
    })
  })

  it('revalidates route context and renders every required authoritative read field', async () => {
    state.workOrder = authoritativeWorkOrder({
      sourceReferenceId: 'MWO-2026-0042',
      priority: 'urgent',
      status: 'accepted',
      assignedTechnicianUserId: 'principal-1',
      assignedTeamId: 'team-a',
      sourceAlarmId: 'ALM-9',
      version: 7,
      allowedActions: ['start', 'cancel'],
      blockReasons: [],
      lifecycle: [
        {
          action: 'accept',
          fromStatus: 'open',
          toStatus: 'accepted',
          actorPrincipalId: 'principal-2',
          technicianUserId: 'principal-1',
          teamId: 'team-b',
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
      '/equipment/work-orders/019f0000-0000-7000-8000-000000000101?source=repair&sourceAlarmId=ALM-9',
    )

    expect(state.requestedId?.value).toBe('019f0000-0000-7000-8000-000000000101')
    expect(wrapper.text()).toContain('一号数控机床')
    expect(wrapper.text()).toContain('WS-1 · LINE-A · ST-9')
    expect(wrapper.text()).toContain('紧急')
    expect(wrapper.text()).toContain('维修人员 张维修')
    expect(wrapper.text()).toContain('班组 甲班')
    expect(wrapper.get('[data-testid="maintenance-lifecycle-event"]').text()).toContain(
      '操作人 李班长 · 维修人员 张维修 · 班组 乙班 ·',
    )
    expect(wrapper.text()).toContain('由报警报修创建')
    expect(wrapper.text()).not.toContain('019f0000-0000-7000-8000-000000000101')
    expect(wrapper.text()).not.toContain('device-1')
    expect(wrapper.text()).not.toContain('principal-1')
    expect(wrapper.text()).not.toContain('team-a')
    expect(wrapper.text()).toContain('开工')
    expect(wrapper.text()).toContain('取消')
    expect(wrapper.text()).toContain('现场接单')
    expect(wrapper.text()).toContain('待处理 → 已接单')
    expect(wrapper.findAll('button').some((button) => button.text() === '开工')).toBe(true)
    expect(wrapper.findAll('button').some((button) => button.text() === '取消')).toBe(false)
  })

  it('renders the server causal lifecycle order even when wall-clock time moves backward', async () => {
    state.workOrder = authoritativeWorkOrder({
      version: 2,
      lifecycle: [
        {
          action: 'assign',
          fromStatus: 'open',
          toStatus: 'open',
          actorPrincipalId: 'principal-1',
          technicianUserId: 'principal-1',
          teamId: 'team-a',
          reason: '版本一：较晚时钟',
          resultingVersion: 1,
          occurredAtUtc: '2026-08-03T02:00:00.000Z',
        },
        {
          action: 'assign',
          fromStatus: 'open',
          toStatus: 'open',
          actorPrincipalId: 'principal-1',
          technicianUserId: 'principal-1',
          teamId: 'team-a',
          reason: '版本二：时钟回拨',
          resultingVersion: 2,
          occurredAtUtc: '2026-08-03T01:00:00.000Z',
        },
      ],
    })

    const wrapper = await mountPage()
    const detailText = wrapper.get('[data-testid="maintenance-work-order-detail"]').text()

    expect(detailText.indexOf('版本一：较晚时钟')).toBeLessThan(
      detailText.indexOf('版本二：时钟回拨'),
    )
  })

  it.each([
    [{}, '维修人员 张维修 · 未指派班组'],
    [{ assignedTeamId: 'team-a' }, '维修人员 张维修 · 班组 甲班'],
  ])('renders readable self assignment identities', async (assignment, label) => {
    state.workOrder = authoritativeWorkOrder({
      ...assignment,
    })

    const wrapper = await mountPage()

    expect(wrapper.text()).toContain(label)
  })

  it('renders a plan-generated work order with a stable Chinese priority', async () => {
    state.workOrder = authoritativeWorkOrder({ priority: 'planned' })

    const wrapper = await mountPage()

    expect(wrapper.get('[data-testid="maintenance-work-order-detail"]').text()).toContain(
      '计划保养',
    )
  })

  it('does not trust an editable source alarm query that differs from the work order', async () => {
    state.workOrder = authoritativeWorkOrder({ sourceAlarmId: 'ALM-AUTHORITATIVE' })

    const wrapper = await mountPage(
      '/equipment/work-orders/019f0000-0000-7000-8000-000000000101?source=repair&sourceAlarmId=ALM-EDITED',
    )

    expect(wrapper.find('[data-testid="maintenance-source-context"]').exists()).toBe(false)
  })

  it('matches source alarm GUIDs case-insensitively while keeping opaque IDs ordinal', async () => {
    const sourceAlarmId = 'aaaaaaaa-aaaa-4aaa-8aaa-aaaaaaaaaaaa'
    state.workOrder = authoritativeWorkOrder({ sourceAlarmId })

    const wrapper = await mountPage(
      `/equipment/work-orders/019f0000-0000-7000-8000-000000000101?sourceAlarmId=${sourceAlarmId.toUpperCase()}`,
    )

    expect(wrapper.get('[data-testid="maintenance-source-context"]').text()).toBe('由报警报修创建')
  })

  it('does not claim ordinary repair source context without an authoritative alarm link', async () => {
    state.workOrder = authoritativeWorkOrder()

    const wrapper = await mountPage(
      '/equipment/work-orders/019f0000-0000-7000-8000-000000000101?source=repair',
    )

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

    expect(wrapper.findAll('button').some((button) => button.text() === '开工')).toBe(false)
  })

  it('reads before accepting and keeps the same key for an unconfirmed result', async () => {
    state.workOrder = authoritativeWorkOrder({ allowedActions: ['accept'] })
    state.transition.mockResolvedValue({ response: { status: 200 }, data: { success: true } })
    state.confirm.mockRejectedValueOnce(new Error('回执未确认'))
    state.confirm.mockImplementationOnce(() => {
      state.workOrder = authoritativeWorkOrder({
        status: 'accepted',
        version: 2,
        allowedActions: ['start'],
        lifecycle: [
          {
            action: 'accept',
            fromStatus: 'open',
            toStatus: 'accepted',
            actorPrincipalId: 'principal-1',
            resultingVersion: 2,
            occurredAtUtc: '2026-08-02T01:02:03.000Z',
          },
        ],
      })
    })
    const wrapper = await mountPage()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '接单')!
      .trigger('click')
    await wrapper.get('textarea').setValue('接手维修')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    const first = state.transition.mock.calls[0][0]
    expect(first.path.workOrderId).toBe(state.workOrder.workOrderId)
    expect(first.body).toMatchObject({ action: 'accept', expectedVersion: 1, reason: '接手维修' })
    expect(first.body.idempotencyKey).toBeTruthy()
    expect(state.refresh).toHaveBeenCalledTimes(1)
    expect(wrapper.text()).not.toContain('操作已确认。')

    await wrapper.get('form').trigger('submit')
    await flushPromises()
    expect(state.transition.mock.calls[1][0].body).toEqual(first.body)
    expect(wrapper.text()).toContain('操作已确认。')
    expect(wrapper.findAll('button').some((button) => button.text() === '开工')).toBe(true)
  })

  it('refreshes a stale start action after a conflict', async () => {
    state.workOrder = authoritativeWorkOrder({ status: 'accepted', allowedActions: ['start'] })
    state.transition.mockImplementationOnce(() => {
      state.workOrder = authoritativeWorkOrder({ status: 'cancelled', version: 2 })
      return { response: { status: 409 }, error: {} }
    })
    const wrapper = await mountPage()
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '开工')!
      .trigger('click')
    await wrapper.get('textarea').setValue('现场开工')
    await wrapper.get('form').trigger('submit')
    await flushPromises()

    expect(state.refresh).toHaveBeenCalledTimes(2)
    expect(wrapper.find('form').exists()).toBe(false)
    expect(wrapper.findAll('button').some((button) => button.text() === '开工')).toBe(false)
    expect(wrapper.text()).toContain('工单状态已更新')
  })

  it('offers an actionable identity refresh when enrichment returns 502', async () => {
    state.workOrder = authoritativeWorkOrder()
    state.identitiesUnavailable = true
    const wrapper = await mountPage()

    const button = wrapper.get('[data-testid="refresh-maintenance-identities"]')
    expect(button.text()).toContain('刷新身份资料')
    await button.trigger('click')

    expect(state.refresh).toHaveBeenCalledTimes(1)
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

  it('shows an invalid route ID as locally unavailable without retry controls', async () => {
    state.enabled = false

    const wrapper = await mountPage('/equipment/work-orders/WO-INVALID')

    expect(wrapper.text()).toContain('工单标识无效，请返回列表重新选择')
    expect(wrapper.find('[data-testid="maintenance-work-order-detail-error"]').exists()).toBe(false)
    expect(state.refresh).not.toHaveBeenCalled()
  })

  it('fails closed for forbidden or unmatched strong IDs without rendering old detail', async () => {
    state.failed = true
    state.error = { status: 403 }

    const wrapper = await mountPage('/equipment/work-orders/019f0000-0000-7000-8000-000000000109')

    expect(state.requestedId?.value).toBe('019f0000-0000-7000-8000-000000000109')
    expect(wrapper.text()).toContain('工单不可查看')
    expect(wrapper.text()).toContain('当前账号不可查看')
    expect(wrapper.text()).not.toContain('Self')
    expect(wrapper.text()).not.toContain('服务端')
    expect(wrapper.text()).not.toContain('MWO-2026-0042')
  })
})
