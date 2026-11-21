import {
  route,
  createWorkOrder,
  DIRECTORY_OPTIONS,
  reasonOptions,
  reasonDirectoryState,
  reasonDirectoryMessage,
  reasonsTotal,
  searchReasons,
  refreshReasons,
  selectPriority,
  lastCreateBody,
  selectReason,
  resetFixture,
} from './repair.fixture'
import { RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import { NvSearchBar } from '@nerv-iip/ui-mobile'
import DowntimeReasonPicker from '@/components/equipment/DowntimeReasonPicker.vue'
import RepairPage from './repair.vue'

describe('PDA equipment repair page', () => {
  beforeEach(resetFixture)

  // ---- #2970 v2 动态停机原因码 ------------------------------------------------------
  it('提交所选目录码的**原值**——不 trim、不改大小写、不送展示文案', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const body = lastCreateBody()
    // toBe 而不是 toMatchObject 的字符串包含：大小写与前后空白任一被改写都必须红。
    expect(body.assetUnavailableReasonCode).toBe('Hyd-Leak_01')
    expect(body.assetUnavailableReasonCode).not.toBe('hyd-leak_01')
    expect(body.assetUnavailableReasonCode).not.toBe('液压泄漏（Hyd-Leak_01）')
    expect(body).not.toHaveProperty('assetUnavailableReason')
  })

  it('明确选择「不登记设备不可用」时提交 null——不是空串、不是伪默认码', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await selectReason(wrapper, '不登记设备不可用')
    expect(wrapper.get('[data-testid="reason-trigger"]').text()).toContain('不登记设备不可用')

    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const body = lastCreateBody()
    expect(body.assetUnavailableReasonCode).toBeNull()
    expect(body.assetUnavailableReasonCode).not.toBe('')
    expect(body.assetUnavailableReasonCode).not.toBe('Hyd-Leak_01')
    expect(Object.keys(body)).toContain('assetUnavailableReasonCode')
  })

  it('从未打开原因抽屉时同样提交 null（默认不登记设备不可用）', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const body = lastCreateBody()
    expect(body.assetUnavailableReasonCode).toBeNull()
    expect(body.assetUnavailableReasonCode).not.toBe('')
  })

  it.each([
    ['failed', '停机原因读取失败，请重试', true],
    ['unavailable', '停机原因词表暂不可用，请稍后重试', true],
    ['forbidden', '当前账号没有停机原因词表的读取权限，请联系管理员开通', false],
  ])(
    '目录 %s 时只给明确错误态：没有自由文本入口、没有可选码，仍只能提交 null',
    async (state, message, retryable) => {
      reasonDirectoryState.value = state as string
      reasonDirectoryMessage.value = message as string
      reasonOptions.value = []
      route.query = { deviceAssetId: 'DEV-9' }
      const wrapper = mount(RepairPage, { attachTo: document.body })

      // 页面上没有任何可输入原因的控件——回退自由文本正是本票点名的禁区。
      expect(wrapper.find('textarea').exists()).toBe(false)
      expect(wrapper.get('[data-testid="reason-directory-blocked"]').text()).toContain(
        message as string,
      )
      expect(wrapper.get('[data-testid="reason-trigger"]').text()).toContain(message as string)

      await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
      await flushPromises()
      const sheet = document.body.querySelector('[data-slot="mobile-sheet-content"]')!
      expect(sheet.querySelector('[data-testid="reason-directory-error"]')!.textContent).toContain(
        message as string,
      )
      // 排除搜索框本身（`input[type="search"]`）后，抽屉里不许再有任何可输入控件。
      expect(
        sheet.querySelectorAll('textarea, input:not([type="search"]), [contenteditable="true"]'),
      ).toHaveLength(0)
      expect(sheet.querySelector('[data-testid="reason-option-Hyd-Leak_01"]')).toBeNull()
      expect(sheet.querySelector('[data-testid="reason-retry"]') !== null).toBe(retryable)

      await selectReason(wrapper, '不登记设备不可用')
      await selectPriority(wrapper, '高')
      await wrapper.get('[data-testid="submit"]').trigger('click')
      await flushPromises()

      const body = lastCreateBody()
      expect(body.assetUnavailableReasonCode).toBeNull()
      expect(body.assetUnavailableReasonCode).not.toBe('')
    },
  )

  it('目录读失败后重试成功即恢复可选，不需要重进页面', async () => {
    reasonDirectoryState.value = 'failed'
    reasonDirectoryMessage.value = '停机原因读取失败，请重试'
    reasonOptions.value = []
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })

    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()
    ;(document.body.querySelector('[data-testid="reason-retry"]') as HTMLButtonElement).click()
    expect(refreshReasons).toHaveBeenCalledTimes(1)

    reasonDirectoryState.value = 'ok'
    reasonDirectoryMessage.value = ''
    reasonOptions.value = [...DIRECTORY_OPTIONS]
    await flushPromises()
    expect(wrapper.find('[data-testid="reason-directory-blocked"]').exists()).toBe(false)
    expect(document.body.querySelector('[data-testid="reason-option-Hyd-Leak_01"]')).not.toBeNull()
  })

  it('原因抽屉的关键字搜索走服务端目录查询，不在前端筛租户', async () => {
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()

    const reasonSearch = wrapper
      .findAllComponents(NvSearchBar)
      .find((component) => component.props('ariaLabel') === '停机原因关键字')
    expect(reasonSearch).toBeTruthy()
    await reasonSearch!.find('input').setValue('液压')
    await reasonSearch!.find('input').trigger('keydown.enter')
    await flushPromises()

    expect(searchReasons).toHaveBeenLastCalledWith('液压')
  })

  it('目录只呈现响应里的条目——前端不合成任何固定原因码', async () => {
    reasonOptions.value = [{ code: 'Only-One', name: '仅此一条', label: '仅此一条（Only-One）' }]
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()

    const sheet = document.body.querySelector('[data-slot="mobile-sheet-content"]')!
    const codes = [...sheet.querySelectorAll<HTMLElement>('[data-row]')]
      .map((row) => row.getAttribute('data-testid'))
      .filter((id): id is string => Boolean(id))
    expect(codes).toEqual(['reason-option-none', 'reason-option-Only-One'])
  })

  it('清空搜索关键字后重新查全量——不留"输入框空了、列表还在过滤"的错位', async () => {
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()

    const reasonSearch = wrapper
      .findAllComponents(NvSearchBar)
      .find((component) => component.props('ariaLabel') === '停机原因关键字')!
    await reasonSearch.find('input').setValue('主轴')
    await reasonSearch.find('input').trigger('keydown.enter')
    await flushPromises()
    expect(searchReasons).toHaveBeenLastCalledWith('主轴')

    // NvSearchBar 的清除按钮只置空 v-model、不 emit search；页面必须自己把关键字清回去，
    // 否则空结果会显示"没有匹配的停机原因"，把"翻不到"误导成"本组织没配"。
    await reasonSearch.get('button[aria-label="清除"]').trigger('click')
    await flushPromises()
    expect(reasonSearch.find('input').element.value).toBe('')
    expect(searchReasons).toHaveBeenLastCalledWith('')
  })

  it('目录被截断时引导去搜索、但不报条数，不让"翻不到"看起来像"没配"', async () => {
    reasonsTotal.value = 137
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()

    const hint = document.body.querySelector('[data-testid="reason-directory-truncated"]')!
    expect(hint).not.toBeNull()
    expect(hint.textContent).toContain('搜索')
    expect(hint.textContent).not.toContain('137')

    reasonsTotal.value = DIRECTORY_OPTIONS.length
    await flushPromises()
    expect(document.body.querySelector('[data-testid="reason-directory-truncated"]')).toBeNull()
  })

  it.each([
    ['empty', '当前组织尚未配置可用停机原因'],
    ['loading', '正在读取停机原因…'],
    ['scope-pending', '登录范围尚未就绪，暂不能读取停机原因'],
  ])('抽屉在 %s 态渲染出可归因文案而不是空白列表', async (state, message) => {
    reasonDirectoryState.value = state as string
    reasonDirectoryMessage.value = message as string
    reasonOptions.value = []
    reasonsTotal.value = 0
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()

    const sheet = document.body.querySelector('[data-slot="mobile-sheet-content"]')!
    expect(sheet.querySelector('[data-testid="reason-directory-state"]')!.textContent).toContain(
      message as string,
    )
    expect(sheet.querySelector('[data-testid="reason-directory-error"]')).toBeNull()
    // 「不登记设备不可用」在任何一态都必须还在，否则 null 路径不可达。
    expect(sheet.querySelector('[data-testid="reason-option-none"]')).not.toBeNull()
  })

  it('意图锁定期即使抽屉直接 emit select 也不改原因码（防呆层的独立契约）', async () => {
    createWorkOrder.mockRejectedValueOnce(new RequestTimeoutError())
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()
    const firstPayload = lastCreateBody()

    await wrapper.get('[data-testid="retry"]').trigger('click')
    await flushPromises()
    // UI 上抽屉根本打不开（前两层守卫）；这条只钉最内层 `onReasonSelected` 的防呆：
    // 即便有人绕过入口直接触发组件事件，已锁定的意图也不许被改。
    wrapper.findComponent(DowntimeReasonPicker).vm.$emit('select', DIRECTORY_OPTIONS[1])
    await flushPromises()

    // 界面也不许说谎：显示成"主轴异响"而实际重发"液压泄漏"，等于让工人以为自己改成了
    // 另一条停机原因。`submit()` 冻结意图只保证发出去的对，保证不了屏幕上写的对。
    expect(wrapper.get('[data-testid="reason-trigger"]').text()).toContain('液压泄漏')
    expect(wrapper.get('[data-testid="reason-trigger"]').text()).not.toContain('主轴异响')

    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(2)
    expect(lastCreateBody()).toEqual(firstPayload)
    expect(lastCreateBody().assetUnavailableReasonCode).toBe('Hyd-Leak_01')
  })

  it('「继续报修」清空已选停机原因——下一单不会带着上一单的目录码', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()
    expect(lastCreateBody().assetUnavailableReasonCode).toBe('Hyd-Leak_01')

    const again = [...wrapper.findAll('button')].find(
      (button) => button.text().trim() === '继续报修',
    )!
    await again.trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="reason-trigger"]').text()).toContain('不登记设备不可用')
    expect(wrapper.get('[data-testid="reason-trigger"]').text()).not.toContain('液压泄漏')
    await selectPriority(wrapper, '中')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(lastCreateBody().assetUnavailableReasonCode).toBeNull()
  })

  it.each([
    ['选了原因', true, '已登记设备占用原因'],
    ['没选原因', false, '本次未登记设备占用原因'],
  ])('成功页复述本次是否登记了设备占用（%s）', async (_name, pickReason, expected) => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage, { attachTo: document.body })
    await selectPriority(wrapper, '高')
    if (pickReason) await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const state = wrapper.get('[data-testid="created-work-order-asset-unavailable-state"]')
    expect(state.text()).toContain(expected as string)
    if (pickReason) expect(state.text()).toContain('Hyd-Leak_01')
  })
})
