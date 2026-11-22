import { state, mountOptions, signInAsMaintenanceReader } from './maintenance-pages.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import InspectionsPage from './inspections.vue'

describe('maintenance inspections page', () => {
  it('marks out-of-spec measurements on the list and surfaces them in the detail drawer', async () => {
    state.inspections = [
      {
        inspectionId: 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaa0001',
        planId: 'PLAN-1',
        workOrderId: null,
        inspector: '设备保全班',
        result: 'failed',
        inspectedAtUtc: '2026-06-10T08:00:00Z',
        measurements: [
          {
            characteristicCode: '轴承温度',
            measuredValue: 85,
            uomCode: '℃',
            lowerSpecLimit: 0,
            upperSpecLimit: 70,
            isWithinSpec: false,
          },
          {
            characteristicCode: '振动',
            measuredValue: 2,
            uomCode: 'mm/s',
            lowerSpecLimit: 0,
            upperSpecLimit: 5,
            isWithinSpec: true,
          },
        ],
      },
    ]
    const wrapper = mount(InspectionsPage, mountOptions())
    await flushPromises()

    // 列表上可见超差标记（1 项超差）。
    expect(wrapper.text()).toContain('1 项超差')

    // 打开记录详情 → 逐特性 pass/超差标记可见（验收①）。
    const trigger = document.body.querySelector<HTMLButtonElement>('[data-testid^="measurements-"]')
    expect(trigger).not.toBeNull()
    trigger!.click()
    await flushPromises()

    const outOfSpec = document.body.querySelector('[data-testid="detail-measurement-0"]')
    expect(outOfSpec?.getAttribute('data-out-of-spec')).toBe('true')
    expect(document.body.textContent).toContain('轴承温度')
    expect(document.body.textContent).toContain('共 1 项测量值超差')
  })

  it('shows no measurement chip when an inspection carries no measurements', async () => {
    state.inspections = [
      {
        inspectionId: 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbb0002',
        planId: 'PLAN-2',
        workOrderId: null,
        inspector: '巡检员',
        result: 'passed',
        inspectedAtUtc: '2026-06-11T08:00:00Z',
        measurements: [],
      },
    ]
    const wrapper = mount(InspectionsPage, mountOptions())
    await flushPromises()
    expect(wrapper.find('[data-testid^="measurements-"]').exists()).toBe(false)
  })

  it('只读角色看不到「记录点检」', async () => {
    signInAsMaintenanceReader()

    mount(InspectionsPage, mountOptions())
    await flushPromises()

    expect(document.body.textContent).not.toContain('记录点检')
  })

  it('记录点检只看保养计划管理权限：只有计划管理、没有工单管理也能记录', async () => {
    useAuthStore().$patch({
      principal: {
        principalType: 'user',
        principalId: 'user-planner',
        loginName: 'planner',
        permissionCodes: ['business.maintenance.plans.read', 'business.maintenance.plans.manage'],
      },
    })

    mount(InspectionsPage, mountOptions())
    await flushPromises()

    expect(
      [...document.body.querySelectorAll('button')].some((b) =>
        b.textContent?.includes('记录点检'),
      ),
    ).toBe(true)
  })

  it('defaults the inspector to the current user and makes characteristic a select (not free text)', async () => {
    mount(InspectionsPage, mountOptions())
    await flushPromises()

    const openBtn = [...document.body.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('记录点检'),
    )!
    openBtn.click()
    await flushPromises()

    // 点检人默认当前登录用户（admin），且是选择器按钮而非自由输入框。
    const inspector = document.body.querySelector('#insp-inspector')!
    expect(inspector.tagName).toBe('BUTTON')
    expect(inspector.textContent).toContain('admin')

    // 测量特性是下拉选择（button 触发），不再是自由输入。
    const characteristic = document.body.querySelector('[id^="m-char-"]')!
    expect(characteristic.tagName).toBe('BUTTON')
  })
})
