import {
  state,
  mountOptions,
  openCompleteSheet,
  hasActionsColumn,
  signInAsMaintenanceReader,
} from './maintenance-pages.fixture'
import { DOMWrapper, flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import WorkOrdersPage from './work-orders.vue'

describe('maintenance work orders page', () => {
  it('开单列与完工上下文固定本地日期时间，不随英文默认语言变化', async () => {
    state.query = {}
    // 使用本地时刻构造 UTC 输入，不改变测试进程时区。
    state.workOrders[0]!.openedAtUtc = new Date(2026, 6, 6, 8, 15).toISOString()
    const wrapper = mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    const headers = [...document.body.querySelectorAll('th')]
    const openedIndex = headers.findIndex((header) => header.textContent?.includes('开单时间'))
    expect(openedIndex).toBeGreaterThanOrEqual(0)
    const row = document.body.querySelector('tbody tr')!
    expect(row.querySelectorAll('td')[openedIndex]?.textContent?.trim()).toBe('2026-07-06 08:15')
    await openCompleteSheet()
    expect(document.body.querySelector('[data-slot="carried-context"]')?.textContent).toContain(
      '2026-07-06 08:15',
    )
    wrapper.unmount()
  })

  it('renders plan-generated work orders with a stable Chinese priority', async () => {
    state.query = {}
    state.workOrders = [
      {
        workOrderId: 'wo-planned',
        deviceAssetId: 'DEV-PRESS-01',
        priority: 'planned',
        status: 'open',
        openedAtUtc: '2026-07-06T00:00:00Z',
      },
    ]

    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    expect(document.body.textContent).toContain('计划保养')
  })

  it('只读角色看不到新建和行内菜单，从设备/报警带参进来也不自动弹出建单', async () => {
    signInAsMaintenanceReader()

    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    expect(document.body.textContent).not.toContain('新建维护工单')
    expect(
      document.body.querySelector('[aria-label="维护工单操作 MWO-20260706-000001"]'),
    ).toBeNull()
    expect(document.body.querySelector('[data-slot="carried-context"]')).toBeNull()
  })

  it('只读角色的维护工单列表不渲染「操作」列', async () => {
    signInAsMaintenanceReader()
    state.query = {}

    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    // 哨兵：表头确实渲染出来了。
    expect(
      [...document.body.querySelectorAll('th')].some((th) => th.textContent?.includes('开单时间')),
    ).toBe(true)
    expect(hasActionsColumn()).toBe(false)
  })

  it('有维护工单管理权限时新建按钮与行内菜单都在', async () => {
    state.query = {}

    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    expect(
      [...document.body.querySelectorAll('button')].some((b) =>
        b.textContent?.includes('新建维护工单'),
      ),
    ).toBe(true)
    expect(
      document.body.querySelector('[aria-label="维护工单操作 MWO-20260706-000001"]'),
    ).not.toBeNull()
  })

  it('prefills maintenance work order creation from equipment alarm context', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    expect(document.body.textContent).toContain('新建维护工单')
    expect(document.body.textContent).toContain('在保')
    expect(document.body.textContent).toContain('SUP-ACME')
    // 带出式录入：设备与来源报警由报警行带出，只读呈现，不再是输入框。
    const carried = document.body.querySelector('[data-slot="carried-context"]')
    expect(carried?.textContent).toContain('DEV-PRESS-01')
    expect(carried?.textContent).toContain('ALARM-9001')
    expect(document.body.querySelector('#mwo-device')).toBeNull()
    expect(document.body.querySelector('#mwo-alarm')).toBeNull()
  })

  it('从报警带入建单时，提交的来源报警就是带入的那一条', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    const submit = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button[type="submit"]'),
    ].find((b) => b.textContent?.includes('创建维护工单'))!
    submit.click()
    await flushPromises()

    expect(state.createWorkOrder.mock.calls[0][0]).toMatchObject({
      deviceAssetId: 'DEV-PRESS-01',
      sourceAlarmId: 'ALARM-9001',
    })
  })

  it('手工建单：先选设备才能挑报警，选中的报警随提交带出，换设备清掉原先挑的报警', async () => {
    state.query = {}
    const wrapper = mount(WorkOrdersPage, mountOptions())
    ;(wrapper.vm as unknown as { openCreate: () => void }).openCreate()
    await flushPromises()
    function input(selector: string) {
      return document.body.querySelector<HTMLInputElement>(selector)!
    }
    async function type(selector: string, value: string) {
      input(selector).value = value
      input(selector).dispatchEvent(new Event('input', { bubbles: true }))
      await flushPromises()
    }
    // 选择器在本文件被桩成输入位，读占位文案判断「先选设备」。
    expect(input('#mwo-alarm').placeholder).toBe('先选设备')

    await type('#mwo-device', 'DEV-PRESS-01')
    expect(input('#mwo-alarm').placeholder).toBe('可选')
    await type('#mwo-alarm', 'ALARM-1')
    await type('#mwo-device', 'DEV-SMT-01')
    expect(input('#mwo-alarm').value).toBe('')

    await type('#mwo-alarm', 'ALARM-1')
    const submit = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button[type="submit"]'),
    ].find((b) => b.textContent?.includes('创建维护工单'))!
    submit.click()
    await flushPromises()
    expect(state.createWorkOrder.mock.calls[0][0]).toMatchObject({
      deviceAssetId: 'DEV-SMT-01',
      sourceAlarmId: 'ALARM-1',
    })
  })

  it('offers a technician selector and estimated labor on the create sheet', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    // 指派技师 + 预估工时进入建单表单（技师是可靠性按技师聚合的前提）。
    expect(document.body.textContent).toContain('指派技师')
    expect(document.body.querySelector('#mwo-est-labor')).not.toBeNull()
  })

  it('submits the selected authority code unchanged without the v1 field', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await new DOMWrapper(document.body.querySelector('#mwo-unavailability-mode')!).trigger('click')
    const register = [...document.body.querySelectorAll('[role="option"]')].find(
      (element) =>
        element.textContent?.includes('登记设备不可用') && !element.textContent?.includes('不登记'),
    )!
    await new DOMWrapper(register).trigger('click')
    const reasonInput = document.body.querySelector<HTMLInputElement>(
      '#mwo-asset-unavailable-reason',
    )
    expect(reasonInput).not.toBeNull()
    reasonInput!.value = 'Line-A.Spindle'
    reasonInput!.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const predictionInput = document.body.querySelector<HTMLInputElement>(
      '#mwo-create-expected-restore',
    )!
    predictionInput.value = '2026-10-10T13:30'
    predictionInput.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()

    const form = reasonInput!.closest('form')!
    form.dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()

    expect(state.createWorkOrder).toHaveBeenCalledTimes(1)
    expect(state.createWorkOrder.mock.calls[0][0]).toMatchObject({
      deviceAssetId: 'DEV-PRESS-01',
      assetUnavailableReasonCode: 'Line-A.Spindle',
      expectedRestoreAtUtc: new Date('2026-10-10T13:30').toISOString(),
    })
    expect(state.createWorkOrder.mock.calls[0][0]).not.toHaveProperty('assetUnavailableReason')
  })

  it.each(['scope', 'failed'])(
    'does not silently submit null after a %s change',
    async (change) => {
      mount(WorkOrdersPage, mountOptions())
      await flushPromises()
      await new DOMWrapper(document.body.querySelector('#mwo-unavailability-mode')!).trigger(
        'click',
      )
      await new DOMWrapper(
        [...document.body.querySelectorAll('[role="option"]')].find(
          (element) => element.textContent?.trim() === '登记设备不可用',
        )!,
      ).trigger('click')
      const input = new DOMWrapper(
        document.body.querySelector<HTMLInputElement>('#mwo-asset-unavailable-reason')!,
      )
      await input.setValue('Line-A.Spindle')
      if (change === 'scope') state.maintenanceFilters.environmentId = 'env-other'
      else Object.assign(state.directory, { state: 'failed', message: '停机原因读取失败，请重试' })
      await flushPromises()
      if (change === 'scope') expect(input.element.value).toBe('')
      else expect(input.element.value).toBe('Line-A.Spindle')
      const form = new DOMWrapper(input.element.closest('form')!)
      await form.trigger('submit')
      await flushPromises()
      expect(state.createWorkOrder).not.toHaveBeenCalled()
      await new DOMWrapper(document.body.querySelector('#mwo-unavailability-mode')!).trigger(
        'click',
      )
      await new DOMWrapper(
        [...document.body.querySelectorAll('[role="option"]')].find(
          (element) => element.textContent?.trim() === '不登记设备不可用',
        )!,
      ).trigger('click')
      await form.trigger('submit')
      await flushPromises()
      expect(state.createWorkOrder).toHaveBeenCalledWith(
        expect.objectContaining({ assetUnavailableReasonCode: null }),
      )
    },
  )

  // 回归：number 输入框经 v-model 可能回传 number；预估工时校验若对 number 调用
  // .trim() 会抛异常，令 submitCreate 静默失败、不发请求（真机走查发现）。
  it('submits create with a numeric estimated-labor value (no silent .trim() crash)', async () => {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()

    function setInput(selector: string, value: number | string) {
      const el = document.body.querySelector<HTMLInputElement>(selector)!
      el.value = String(value)
      el.dispatchEvent(new Event('input', { bubbles: true }))
    }
    // 设备由报警上下文带出（DEV-PRESS-01），开单人默认当前登录用户（admin），二者都无需填写。
    // 直接以 number 赋值，复现 number 型 v-model 回传。
    setInput('#mwo-est-labor', 45)
    await flushPromises()

    const submit = [
      ...document.body.querySelectorAll<HTMLButtonElement>('[role="dialog"] button[type="submit"]'),
    ].find((b) => b.textContent?.includes('创建维护工单'))!
    submit.click()
    await flushPromises()

    expect(state.createWorkOrder).toHaveBeenCalledTimes(1)
    const body = state.createWorkOrder.mock.calls[0][0]
    expect(body.deviceAssetId).toBe('DEV-PRESS-01')
    expect(body.estimatedLaborMinutes).toBe(45)
    // 开单人默认解析为当前用户显示名（验证「默认当前用户」）。
    expect(body.openedBy).toBe('admin')
    expect(body.assetUnavailableReasonCode).toBeNull()
  })
})

describe('maintenance work orders inline downtime reason and issue location', () => {
  async function openCreateWithReasonMode() {
    mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    await new DOMWrapper(document.body.querySelector('#mwo-unavailability-mode')!).trigger('click')
    await new DOMWrapper(
      [...document.body.querySelectorAll('[role="option"]')].find(
        (element) => element.textContent?.trim() === '登记设备不可用',
      )!,
    ).trigger('click')
    await flushPromises()
  }

  async function chooseOption(triggerSelector: string, label: string) {
    await new DOMWrapper(document.body.querySelector(triggerSelector)!).trigger('click')
    await flushPromises()
    await new DOMWrapper(
      [...document.body.querySelectorAll('[role="option"]')].find(
        (element) => element.textContent?.trim() === label,
      )!,
    ).trigger('click')
    await flushPromises()
  }

  async function fillInlineReason(code: string) {
    ;[...document.body.querySelectorAll<HTMLButtonElement>('button')]
      .find((button) => button.textContent?.includes('新增停机原因'))!
      .click()
    await flushPromises()
    const codeInput = document.body.querySelector<HTMLInputElement>('#dtr-code')!
    codeInput.value = code
    codeInput.dispatchEvent(new Event('input', { bubbles: true }))
    const description = document.body.querySelector<HTMLInputElement>('#dtr-description')!
    description.value = '液压系统故障'
    description.dispatchEvent(new Event('input', { bubbles: true }))
    await flushPromises()
    await chooseOption('#dtr-category', '设备故障')
    await chooseOption('#dtr-loss', '可用率损失')
    codeInput
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
  }

  it('就地新增停机原因后自动选中新建的原因，并随建单提交（#3855）', async () => {
    await openCreateWithReasonMode()
    await fillInlineReason('DT-HYD')

    expect(state.createReason).toHaveBeenCalledWith(
      expect.objectContaining({
        reasonCode: 'DT-HYD',
        reasonCategory: 'breakdown',
        lossCategory: 'availability',
      }),
    )
    const reasonInput = document.body.querySelector<HTMLInputElement>(
      '#mwo-asset-unavailable-reason',
    )!
    expect(reasonInput.value).toBe('DT-HYD')

    reasonInput
      .closest('form')!
      .dispatchEvent(new Event('submit', { bubbles: true, cancelable: true }))
    await flushPromises()
    expect(state.createWorkOrder.mock.calls.at(-1)![0]).toMatchObject({
      assetUnavailableReasonCode: 'DT-HYD',
    })
  })

  it('就地新增时编码已存在：提示换编码，不报新建成功，也不选中任何原因（#3855）', async () => {
    state.createReason.mockRejectedValue({
      success: false,
      message: 'downtime-reason-code-already-exists',
      code: 409,
    })
    await openCreateWithReasonMode()
    await fillInlineReason('DT-MECH')

    expect(state.createReason).toHaveBeenCalledTimes(1)
    expect(document.body.textContent).toContain('该原因编码已存在')
    expect(state.toastSuccess).not.toHaveBeenCalled()
    expect(
      document.body.querySelector<HTMLInputElement>('#mwo-asset-unavailable-reason')!.value,
    ).toBe('')
  })

  it('完工换件行的领出库位只在设备所在工厂里选（#3902）', async () => {
    state.query = {}
    state.workOrders = [
      {
        workOrderId: 'wo-site',
        deviceAssetId: 'DEV-1',
        priority: 'high',
        status: 'open',
        openedAtUtc: '2026-06-10T08:00:00Z',
      },
    ]
    const wrapper = mount(WorkOrdersPage, mountOptions())
    await flushPromises()
    document.body.querySelector<HTMLButtonElement>('[aria-label^="维护工单操作"]')!.click()
    await flushPromises()
    ;[...document.body.querySelectorAll<HTMLElement>('[role="menuitem"]')]
      .find((el) => el.textContent?.includes('完成工单'))!
      .click()
    await flushPromises()

    const locationPicker = wrapper
      .findAllComponents({ name: 'DirectoryPicker' })
      .find((picker) =>
        String(picker.attributes('id') ?? picker.vm.$attrs.id).startsWith('spare-location-'),
      )
    expect(locationPicker).toBeDefined()
    expect(locationPicker!.props('directoryType')).toBe('location')
    expect(locationPicker!.props('formSiteCode')).toBe('SITE-001')
  })
})
