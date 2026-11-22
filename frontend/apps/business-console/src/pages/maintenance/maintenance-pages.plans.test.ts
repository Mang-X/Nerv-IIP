import {
  state,
  mountOptions,
  hasActionsColumn,
  signInAsMaintenanceReader,
} from './maintenance-pages.fixture'
import { DOMWrapper, flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import { i18n } from '@/i18n'
import PlansPage from './plans.vue'

describe('maintenance plans page', () => {
  function bodyWrapper<T extends Element>(selector: string) {
    const element = document.body.querySelector<T>(selector)
    expect(element, `Expected ${selector} in document.body`).not.toBeNull()
    return new DOMWrapper(element!)
  }

  function bodyWrapperByText<T extends Element>(selector: string, text: string) {
    const element = [...document.body.querySelectorAll<T>(selector)].find((candidate) =>
      candidate.textContent?.includes(text),
    )
    expect(element, `Expected ${selector} containing ${text} in document.body`).toBeDefined()
    return new DOMWrapper(element!)
  }

  async function openEditDialog(planCode: string) {
    await bodyWrapper<HTMLButtonElement>(`[aria-label="保养计划操作 ${planCode}"]`).trigger('click')
    await flushPromises()
    await bodyWrapperByText<HTMLElement>('[role="menuitem"]', '编辑触发条件').trigger('click')
    await flushPromises()
  }

  it('does not expose any write entry to a principal with plans.read only', async () => {
    useAuthStore().$patch({
      principal: {
        principalType: 'user',
        principalId: 'user-reader',
        loginName: 'reader',
        permissionCodes: ['business.maintenance.plans.read'],
      },
    })
    state.plans = [
      {
        planId: 'p-read-only',
        deviceAssetId: 'DEV-READ-ONLY',
        planCode: 'PM-READ-ONLY',
        interval: 'P30D',
        startsOn: '2026-07-01',
      },
    ]

    mount(PlansPage, mountOptions())
    await flushPromises()

    expect(document.body.textContent).not.toContain('编辑触发条件')
    expect(document.body.querySelector('[aria-label="保养计划操作 PM-READ-ONLY"]')).toBeNull()
    expect(document.body.textContent).not.toContain('新建保养计划')
    expect(document.body.textContent).not.toContain('生成到期工单')
  })

  it('只读角色的保养计划列表不渲染「操作」列', async () => {
    signInAsMaintenanceReader()
    state.plans = [
      {
        planId: 'p-ro',
        deviceAssetId: 'DEV-RO',
        planCode: 'PM-RO',
        interval: 'P30D',
        startsOn: '2026-07-01',
      },
    ]

    mount(PlansPage, mountOptions())
    await flushPromises()

    expect(
      [...document.body.querySelectorAll('th')].some((th) => th.textContent?.includes('计划编号')),
    ).toBe(true)
    expect(hasActionsColumn()).toBe(false)
  })

  it('有计划管理权限时「生成到期工单」「新建保养计划」与「操作」列都在', async () => {
    mount(PlansPage, mountOptions())
    await flushPromises()

    const buttons = [...document.body.querySelectorAll('button')].map((b) => b.textContent ?? '')
    expect(buttons.some((text) => text.includes('生成到期工单'))).toBe(true)
    expect(buttons.some((text) => text.includes('新建保养计划'))).toBe(true)
    expect(hasActionsColumn()).toBe(true)
  })

  it('allows a principal with plans.manage to open the edit dialog', async () => {
    state.plans = [
      {
        planId: 'p-manager',
        deviceAssetId: 'DEV-MANAGER',
        planCode: 'PM-MANAGER',
        interval: 'P30D',
        startsOn: '2026-07-01',
      },
    ]

    mount(PlansPage, mountOptions())
    await flushPromises()
    await openEditDialog('PM-MANAGER')

    expect(document.body.textContent).toContain('编辑保养计划')
    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull()
  })

  it('renders three trigger modes and remaining hours vs next-due date', async () => {
    state.plans = [
      {
        planId: 'p-cal',
        deviceAssetId: 'DEV-CAL',
        planCode: 'PM-CAL',
        interval: 'P30D',
        startsOn: '2026-07-01',
        nextDueOn: '2026-07-31',
        lastGeneratedRuntimeHours: 0,
      },
      {
        planId: 'p-run',
        deviceAssetId: 'DEV-RUN',
        planCode: 'PM-RUN',
        interval: null, // runtime-only: no calendar trigger
        startsOn: '2026-06-01',
        nextDueOn: null,
        runtimeHourInterval: 1000,
        nextDueRuntimeHours: 1000,
        lastGeneratedRuntimeHours: 0,
      },
      {
        planId: 'p-both',
        deviceAssetId: 'DEV-BOTH',
        planCode: 'PM-BOTH',
        interval: 'P90D',
        startsOn: '2026-06-01',
        nextDueOn: '2026-08-30',
        runtimeHourInterval: 2000,
        nextDueRuntimeHours: 2000,
        lastGeneratedRuntimeHours: 0,
      },
    ]
    // Client-derived remaining runtime hours (per plan's own window).
    state.remainingByPlanId = {
      'p-run': { status: 'ok', hours: 300 },
      'p-both': { status: 'ok', hours: 1700 },
    }
    const wrapper = mount(PlansPage, mountOptions())
    await flushPromises()

    expect(wrapper.text()).toContain('触发模式')
    // Three distinguishable modes.
    expect(wrapper.text()).toContain('日历周期')
    expect(wrapper.text()).toContain('运行小时')
    expect(wrapper.text()).toContain('两者组合')
    // Calendar-only shows the next due date; runtime shows remaining hours (not the absolute threshold).
    expect(wrapper.text()).toContain('2026-07-31')
    expect(wrapper.text()).toContain('剩余 300 小时')
    expect(wrapper.text()).toContain('剩余 1700 小时')
  })

  it('distinguishes a telemetry read failure from genuinely having no samples in the plans list', async () => {
    state.plans = [
      {
        planId: 'p-err',
        deviceAssetId: 'DEV-ERR',
        planCode: 'PM-ERR',
        interval: null,
        startsOn: '2026-06-01',
        nextDueOn: null,
        runtimeHourInterval: 1000,
        nextDueRuntimeHours: 1000,
        lastGeneratedRuntimeHours: 0,
      },
      {
        planId: 'p-nos',
        deviceAssetId: 'DEV-NOS',
        planCode: 'PM-NOS',
        interval: null,
        startsOn: '2026-06-01',
        nextDueOn: null,
        runtimeHourInterval: 1000,
        nextDueRuntimeHours: 1000,
        lastGeneratedRuntimeHours: 0,
      },
    ]
    state.remainingByPlanId = { 'p-err': { status: 'error' }, 'p-nos': { status: 'no-samples' } }
    const wrapper = mount(PlansPage, mountOptions())
    await flushPromises()

    // A failed telemetry read is never mislabeled as "暂无运行记录".
    expect(wrapper.text()).toContain('运行小时（读取失败）')
    expect(wrapper.text()).toContain('运行小时（暂无运行记录）')
  })

  it('shows a loading state for a runtime plan whose remaining read is in flight (not a stale value)', async () => {
    state.plans = [
      {
        planId: 'p-load',
        deviceAssetId: 'DEV-LOAD',
        planCode: 'PM-LOAD',
        interval: null,
        startsOn: '2026-06-01',
        nextDueOn: null,
        runtimeHourInterval: 1000,
        nextDueRuntimeHours: 2000,
        lastGeneratedRuntimeHours: 0,
      },
    ]
    // Refresh in flight (e.g. after a threshold advance): the row must show loading, never a stale value.
    state.remainingByPlanId = { 'p-load': { status: 'loading' } }
    state.remainingPending = true
    const wrapper = mount(PlansPage, mountOptions())
    await flushPromises()

    expect(wrapper.text()).toContain('运行小时（读取中…）')
    expect(wrapper.text()).not.toContain('剩余')
  })

  it('re-reads both plans and per-plan remaining hours when the list refresh button is clicked', async () => {
    state.plans = [
      {
        planId: 'p-run',
        deviceAssetId: 'DEV-RUN',
        planCode: 'PM-RUN',
        interval: null,
        startsOn: '2026-06-01',
        nextDueOn: null,
        runtimeHourInterval: 1000,
        nextDueRuntimeHours: 1000,
        lastGeneratedRuntimeHours: 0,
      },
    ]
    state.remainingByPlanId = { 'p-run': { status: 'ok', hours: 500 } }
    mount(PlansPage, mountOptions())
    await flushPromises()

    const refreshBtn = [...document.body.querySelectorAll('button')].find(
      (b) => b.textContent?.trim() === '刷新',
    )!
    refreshBtn.click()
    await flushPromises()

    // Refresh must re-read the plan list AND the client-derived remaining hours — plans alone leaves a
    // still-running device showing a stale "剩余 X 小时".
    expect(state.refreshPlans).toHaveBeenCalledTimes(1)
    expect(state.refreshRemaining).toHaveBeenCalledTimes(1)
  })

  it('shows a settled state (not permanent loading) for a runtime plan with a missing threshold cursor', async () => {
    state.plans = [
      {
        planId: 'p-invalid',
        deviceAssetId: 'DEV-INV',
        planCode: 'PM-INV',
        interval: null,
        startsOn: '2026-06-01',
        nextDueOn: null,
        runtimeHourInterval: 1000,
        nextDueRuntimeHours: null, // inconsistent cursor -> can never compute a remaining
        lastGeneratedRuntimeHours: 0,
      },
    ]
    // The composable settles such a plan as 'invalid'; pending may still be true from other reads.
    state.remainingByPlanId = { 'p-invalid': { status: 'invalid' } }
    state.remainingPending = true
    const wrapper = mount(PlansPage, mountOptions())
    await flushPromises()

    expect(wrapper.text()).toContain('运行小时（阈值缺失）')
    expect(wrapper.text()).not.toContain('运行小时（读取中…）')
  })

  it('keeps the calendar due for a combined plan when the runtime-hours read fails', async () => {
    state.plans = [
      {
        planId: 'p-combined',
        deviceAssetId: 'DEV-COMBINED',
        planCode: 'PM-COMBINED',
        interval: 'P90D',
        startsOn: '2026-06-01',
        nextDueOn: '2026-08-30',
        runtimeHourInterval: 2000,
        nextDueRuntimeHours: 2000,
        lastGeneratedRuntimeHours: 0,
      },
    ]
    state.remainingByPlanId = { 'p-combined': { status: 'error' } }
    const wrapper = mount(PlansPage, mountOptions())
    await flushPromises()

    // Combined plan still has a valid calendar due — it must stay visible even when the runtime read failed.
    expect(wrapper.text()).toContain('2026-08-30')
    expect(wrapper.text()).toContain('运行小时读取失败')
    // Must not hide the calendar date behind a bare runtime-only "读取失败".
    expect(wrapper.text()).not.toContain('运行小时（读取失败）')
  })

  // PublicContract：#3905 r1 / #4387；真实日期交互保持 ISO 请求和既有当天默认值。
  it.each(['plan', 'generate'] as const)(
    '%s 日期选择、响应式 locale 与清空保持请求语义',
    async (kind) => {
      const wrapper = mount(PlansPage, mountOptions())
      await flushPromises()
      const openingText = kind === 'plan' ? '新建保养计划' : '生成到期工单'
      ;[...document.body.querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.includes(openingText))!
        .click()
      await flushPromises()

      const id = kind === 'plan' ? 'plan-starts' : 'gen-date'
      const today = new Date().toISOString().slice(0, 10)
      const trigger = bodyWrapper<HTMLButtonElement>(`#${id}`)
      expect(document.querySelector(`label[for="${id}"]`)).not.toBeNull()
      expect(trigger.text()).toBe(
        new Intl.DateTimeFormat('zh-CN', {
          year: 'numeric',
          month: 'numeric',
          day: 'numeric',
          timeZone: 'UTC',
        }).format(new Date(`${today}T00:00:00Z`)),
      )
      await trigger.trigger('click')
      await flushPromises()
      const selectedDay = [...document.querySelectorAll<HTMLButtonElement>('.nv-dp-cell')].find(
        (button) => button.textContent?.trim() === '19',
      )!
      selectedDay.click()
      await flushPromises()
      const picked = `${today.slice(0, 7)}-19`
      i18n.global.locale.value = 'en-US'
      await flushPromises()
      expect(trigger.text()).toBe(
        new Intl.DateTimeFormat('en-US', {
          year: 'numeric',
          month: 'numeric',
          day: 'numeric',
          timeZone: 'UTC',
        }).format(new Date(`${picked}T00:00:00Z`)),
      )

      if (kind === 'plan') {
        await bodyWrapper<HTMLInputElement>('#plan-device').setValue('DEV-SMT-01')
        await bodyWrapper<HTMLInputElement>('#plan-owner').setValue('设备保全班')
      }
      await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
      await flushPromises()
      const request = kind === 'plan' ? state.createPlan : state.generateDue
      const field = kind === 'plan' ? 'startsOn' : 'businessDate'
      expect(request.mock.calls[0][0][field]).toBe(picked)

      ;[...document.body.querySelectorAll<HTMLButtonElement>('button')]
        .find((button) => button.textContent?.includes(openingText))!
        .click()
      await flushPromises()
      await bodyWrapper<HTMLButtonElement>(`#${id}`).trigger('click')
      await flushPromises()
      ;[...document.querySelectorAll<HTMLButtonElement>('.nv-dp-cell')]
        .find((button) => button.textContent?.trim() === '19')!
        .click()
      await flushPromises()
      const picker = document.querySelector(`#${id}`)!.closest('[data-slot="nv-date-picker"]')!
      ;(picker.querySelector('[aria-label="清除所选日期"]') as HTMLButtonElement).click()
      await flushPromises()
      expect(document.querySelector(`#${id}`)?.textContent).toContain('选择日期')
      if (kind === 'plan') {
        // 清空日期不会绕过原有必填校验。
        await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
        await flushPromises()
        expect(request).toHaveBeenCalledTimes(1)
        expect(document.body.textContent).toContain('请完善标红的必填项。')
        await bodyWrapper<HTMLInputElement>('#plan-device').setValue('DEV-SMT-01')
        await bodyWrapper<HTMLInputElement>('#plan-owner').setValue('设备保全班')
      }
      await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
      await flushPromises()
      expect(request.mock.calls[1][0][field]).toBe(today)
      wrapper.unmount()
    },
  )

  it('creates a genuine runtime-only plan (no calendar interval)', async () => {
    mount(PlansPage, mountOptions())
    await flushPromises()

    const openBtn = [...document.body.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('新建保养计划'),
    )!
    openBtn.click()
    await flushPromises()

    // 切到「运行小时」触发模式。
    document.body
      .querySelector<HTMLButtonElement>('[role="dialog"] button[data-tab="runtime"]')!
      .click()
    await flushPromises()

    function setInput(selector: string, value: string) {
      const el = document.body.querySelector<HTMLInputElement>(selector)!
      el.value = value
      el.dispatchEvent(new Event('input', { bubbles: true }))
    }
    setInput('#plan-device', 'DEV-SMT-01')
    setInput('#plan-owner', '设备保全班')
    // 用 1000 小时快捷按钮填充触发运行小时数。
    const quick = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button'),
    ].find((b) => b.textContent?.trim() === '1000 小时')!
    quick.click()
    await flushPromises()

    const submit = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button[type="submit"]'),
    ].find((b) => b.textContent?.includes('创建保养计划'))!
    submit.click()
    await flushPromises()

    expect(state.createPlan).toHaveBeenCalledTimes(1)
    const body = state.createPlan.mock.calls[0][0]
    expect(body.deviceAssetId).toBe('DEV-SMT-01')
    expect(body.runtimeHourInterval).toBe(1000)
    // Runtime-only mode sends no calendar interval — a genuine usage-triggered plan.
    expect(body.interval).toBeUndefined()
  })

  it.each([
    {
      label: '日历周期',
      plan: {
        planId: 'p-calendar-edit',
        deviceAssetId: 'DEV-CALENDAR',
        planCode: 'PM-CALENDAR',
        interval: 'P90D',
        startsOn: '2026-03-01',
        runtimeHourInterval: null,
      },
      intervalVisible: true,
      runtimeValue: undefined,
    },
    {
      label: '运行小时',
      plan: {
        planId: 'p-runtime-edit',
        deviceAssetId: 'DEV-RUNTIME',
        planCode: 'PM-RUNTIME',
        interval: null,
        startsOn: '2026-04-02',
        runtimeHourInterval: 1000,
      },
      intervalVisible: false,
      runtimeValue: '1000',
    },
    {
      label: '两者组合',
      plan: {
        planId: 'p-both-edit',
        deviceAssetId: 'DEV-BOTH',
        planCode: 'PM-BOTH',
        interval: 'P30D',
        startsOn: '2026-05-03',
        runtimeHourInterval: 2000,
      },
      intervalVisible: true,
      runtimeValue: '2000',
    },
  ])(
    'prefills $label edit mode and keeps plan identity read-only',
    async ({ plan, intervalVisible, runtimeValue }) => {
      state.plans = [plan]
      const wrapper = mount(PlansPage, mountOptions())
      await flushPromises()

      await openEditDialog(plan.planCode)

      expect(document.body.textContent).toContain('编辑保养计划')
      // 计划身份（设备 / 计划编号 / 起始日期）走只读带出区，不再是 readonly 输入框。
      const carried = bodyWrapper<HTMLElement>('[data-slot="carried-context"]')
      expect(carried.text()).toContain(plan.deviceAssetId)
      expect(carried.text()).toContain(plan.planCode)
      expect(carried.text()).toContain(plan.startsOn)
      expect(document.body.querySelector('#plan-device')).toBeNull()
      expect(document.body.querySelector('#plan-code')).toBeNull()
      expect(document.body.querySelector('#plan-starts')).toBeNull()
      expect(
        bodyWrapperByText(
          '[role="dialog"]',
          intervalVisible ? '保养周期' : '按设备累计运行小时',
        ).exists(),
      ).toBe(true)
      expect(document.body.querySelector('#plan-interval') !== null).toBe(intervalVisible)
      expect(document.body.querySelector<HTMLInputElement>('#plan-runtime-hours')?.value).toBe(
        runtimeValue,
      )

      wrapper.unmount()
    },
  )

  it('shows and preserves a non-preset calendar interval when editing', async () => {
    state.plans = [
      {
        planId: 'p-custom-calendar-edit',
        deviceAssetId: 'DEV-CUSTOM-CALENDAR',
        planCode: 'PM-CUSTOM-CALENDAR',
        interval: 'P45D',
        startsOn: '2026-03-15',
        runtimeHourInterval: null,
      },
    ]
    mount(PlansPage, mountOptions())
    await flushPromises()

    await openEditDialog('PM-CUSTOM-CALENDAR')

    expect(bodyWrapper<HTMLButtonElement>('#plan-interval').text()).toContain('45 天（当前）')

    await bodyWrapper<HTMLButtonElement>('#plan-interval').trigger('keydown', { key: 'Enter' })
    await flushPromises()
    await bodyWrapperByText<HTMLElement>('[role="option"]', '每月').trigger('keydown', {
      key: 'Enter',
    })
    await flushPromises()
    expect(bodyWrapper<HTMLButtonElement>('#plan-interval').text()).toContain('每月')

    await bodyWrapper<HTMLButtonElement>('#plan-interval').trigger('keydown', { key: 'Enter' })
    await flushPromises()
    await bodyWrapperByText<HTMLElement>('[role="option"]', '45 天（当前）').trigger('keydown', {
      key: 'Enter',
    })
    await flushPromises()

    await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
    await flushPromises()

    expect(state.updatePlan).toHaveBeenCalledWith('p-custom-calendar-edit', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      interval: 'P45D',
      runtimeHourInterval: null,
    })
  })

  it('clears runtime hours explicitly when changing a runtime plan to calendar-only', async () => {
    state.plans = [
      {
        planId: 'p-runtime-to-calendar',
        deviceAssetId: 'DEV-RUNTIME',
        planCode: 'PM-RUNTIME-CALENDAR',
        interval: null,
        startsOn: '2026-04-02',
        runtimeHourInterval: 1000,
      },
    ]
    mount(PlansPage, mountOptions())
    await flushPromises()
    await openEditDialog('PM-RUNTIME-CALENDAR')

    await bodyWrapper<HTMLButtonElement>('[role="dialog"] button[data-tab="calendar"]').trigger(
      'click',
    )
    await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
    await flushPromises()

    expect(state.updatePlan).toHaveBeenCalledWith('p-runtime-to-calendar', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      interval: 'P30D',
      runtimeHourInterval: null,
    })
    expect(document.body.querySelector('[role="dialog"]')).toBeNull()
    expect(state.toastSuccess).toHaveBeenCalledWith('保养计划触发条件已更新')
  })

  it('clears the calendar trigger for runtime-only and submits both triggers for combined mode', async () => {
    state.plans = [
      {
        planId: 'p-calendar-to-runtime',
        deviceAssetId: 'DEV-CALENDAR',
        planCode: 'PM-CALENDAR-RUNTIME',
        interval: 'P90D',
        startsOn: '2026-03-01',
        runtimeHourInterval: null,
      },
    ]
    mount(PlansPage, mountOptions())
    await flushPromises()
    await openEditDialog('PM-CALENDAR-RUNTIME')

    await bodyWrapper<HTMLButtonElement>('[role="dialog"] button[data-tab="runtime"]').trigger(
      'click',
    )
    await bodyWrapper<HTMLInputElement>('#plan-runtime-hours').setValue('1500')
    await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
    await flushPromises()

    expect(state.updatePlan).toHaveBeenNthCalledWith(1, 'p-calendar-to-runtime', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      interval: null,
      runtimeHourInterval: 1500,
    })

    await openEditDialog('PM-CALENDAR-RUNTIME')
    await bodyWrapper<HTMLButtonElement>('[role="dialog"] button[data-tab="both"]').trigger('click')
    await bodyWrapper<HTMLInputElement>('#plan-runtime-hours').setValue('2000')
    await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
    await flushPromises()

    expect(state.updatePlan).toHaveBeenNthCalledWith(2, 'p-calendar-to-runtime', {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      interval: 'P90D',
      runtimeHourInterval: 2000,
    })
  })

  it('marks runtime hours invalid only after submit and does not call update', async () => {
    state.plans = [
      {
        planId: 'p-calendar-invalid',
        deviceAssetId: 'DEV-CALENDAR',
        planCode: 'PM-CALENDAR-INVALID',
        interval: 'P30D',
        startsOn: '2026-03-01',
        runtimeHourInterval: null,
      },
    ]
    mount(PlansPage, mountOptions())
    await flushPromises()
    await openEditDialog('PM-CALENDAR-INVALID')

    await bodyWrapper<HTMLButtonElement>('[role="dialog"] button[data-tab="runtime"]').trigger(
      'click',
    )
    const runtimeInput = bodyWrapper<HTMLInputElement>('#plan-runtime-hours')
    expect(
      runtimeInput.element.closest('[data-slot="nv-input"]')?.getAttribute('data-invalid'),
    ).not.toBe('true')
    expect(document.body.textContent).not.toContain('请填写正的触发运行小时数。')

    await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
    await flushPromises()

    expect(state.updatePlan).not.toHaveBeenCalled()
    expect(
      runtimeInput.element.closest('[data-slot="nv-input"]')?.getAttribute('data-invalid'),
    ).toBe('true')
    expect(document.body.textContent).toContain('请填写正的触发运行小时数。')
  })

  it('keeps the edit dialog and entered values open and shows a friendly toast when update fails', async () => {
    state.plans = [
      {
        planId: 'p-update-failure',
        deviceAssetId: 'DEV-FAIL',
        planCode: 'PM-UPDATE-FAIL',
        interval: null,
        startsOn: '2026-04-02',
        runtimeHourInterval: 1000,
      },
    ]
    state.updatePlan.mockRejectedValueOnce(new Error('network error'))
    mount(PlansPage, mountOptions())
    await flushPromises()
    await openEditDialog('PM-UPDATE-FAIL')

    await bodyWrapper<HTMLInputElement>('#plan-runtime-hours').setValue('1800')
    await bodyWrapper<HTMLFormElement>('[role="dialog"] form').trigger('submit')
    await flushPromises()

    expect(document.body.querySelector('[role="dialog"]')).not.toBeNull()
    expect(document.body.querySelector<HTMLInputElement>('#plan-runtime-hours')?.value).toBe('1800')
    expect(state.toastError).toHaveBeenCalledWith(
      '网络异常，操作结果可能尚未确认；请刷新列表核实后再重试。',
    )
    expect(state.toastSuccess).not.toHaveBeenCalled()
  })

  it('blocks a runtime-mode submit when the trigger hours are missing', async () => {
    mount(PlansPage, mountOptions())
    await flushPromises()

    const openBtn = [...document.body.querySelectorAll('button')].find((b) =>
      b.textContent?.includes('新建保养计划'),
    )!
    openBtn.click()
    await flushPromises()

    document.body
      .querySelector<HTMLButtonElement>('[role="dialog"] button[data-tab="runtime"]')!
      .click()
    await flushPromises()

    const deviceInput = document.body.querySelector<HTMLInputElement>('#plan-device')!
    deviceInput.value = 'DEV-SMT-02'
    deviceInput.dispatchEvent(new Event('input', { bubbles: true }))

    const ownerInput = document.body.querySelector<HTMLInputElement>('#plan-owner')!
    ownerInput.value = '设备保全班'
    ownerInput.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const submit = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button[type="submit"]'),
    ].find((b) => b.textContent?.includes('创建保养计划'))!
    submit.click()
    await flushPromises()

    expect(state.createPlan).not.toHaveBeenCalled()
    expect(document.body.textContent).toContain('请填写正的触发运行小时数。')
    // Field-level invalid state (red box on the NvInput wrapper), not only the bottom summary.
    const runtimeWrapper = document.body
      .querySelector<HTMLInputElement>('#plan-runtime-hours')!
      .closest('[data-slot="nv-input"]')!
    expect(runtimeWrapper.getAttribute('data-invalid')).toBe('true')
  })
})
