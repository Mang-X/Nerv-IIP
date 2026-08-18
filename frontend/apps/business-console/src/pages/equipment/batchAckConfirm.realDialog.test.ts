import { flushPromises, mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'

import AlarmsPage from './alarms.vue'

/**
 * **不 stub `NvAlertDialog*`** 的一组用例（#1613 子项 e · equipment 域）。
 *
 * 批量确认报警是本票里唯一的**批量**破坏性确认（`batchAck.submitting`）。原确认按钮
 * `NvAlertDialogAction` 渲染成 reka `DialogClose`，`@click` 里 `onOpenChange(false)`
 * 无条件执行、不看 `defaultPrevented`：点下去框立刻关，`:disabled="batchAck.submitting"`
 * 那一瞬**用户根本看不到**——连点两下就是两轮 `Promise.allSettled`（confirm-destroy 规则 3）。
 *
 * 与其它清扫点的差别（票面子项 e「失败后要保留选中集」）：本页确认动作走
 * `Promise.allSettled`、不抛，收尾时**成败都关框**，重试的落点是**保留下来的选中集**
 * 而不是留在原地的确认框。所以这里钉的不变量是两条：
 * 1. 写回**进行中**框还开着、确认按钮真的禁用（这条只有普通 `NvButton` 才留得住）；
 * 2. 部分失败后只有失败行仍被选中，用户可就地重发。
 */
vi.mock('@/composables/useMasterDataDisplayNames', async () => {
  const { computed } = await import('vue')
  const emptyIndex = computed(() => new Map<string, string>())
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: () => undefined,
      resolveLocation: () => undefined,
      resolveWorkCenter: () => undefined,
      resolveTeam: () => undefined,
      resolveUom: () => undefined,
      resolveWorkshop: () => undefined,
      resolveLine: () => undefined,
      formatUom: (code?: string | null, fallback = '') => code ?? fallback,
      deviceByCode: emptyIndex,
      locationByCode: emptyIndex,
      workCenterByCode: emptyIndex,
      teamByCode: emptyIndex,
      uomByCode: emptyIndex,
      workshopByCode: emptyIndex,
      lineByCode: emptyIndex,
    }),
  }
})

const alarmState = vi.hoisted(() => ({
  alarms: [] as Array<Record<string, unknown>>,
  acknowledgeAlarm: vi.fn((..._args: unknown[]) => Promise.resolve()),
  refreshAlarms: vi.fn((..._args: unknown[]) => Promise.resolve()),
}))

vi.mock('@/composables/useBusinessEquipment', () => ({
  useBusinessEquipmentAlarms: () => ({
    acknowledgeAlarm: alarmState.acknowledgeAlarm,
    alarms: computed(() => alarmState.alarms),
    alarmsError: shallowRef(),
    alarmsPending: shallowRef(false),
    refreshAlarms: alarmState.refreshAlarms,
    shelveAlarm: vi.fn(() => Promise.resolve()),
    unshelveAlarm: vi.fn(() => Promise.resolve()),
  }),
}))

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      loginName: 'operator-a',
      permissionCodes: ['business.iiot.alarms.read', 'business.iiot.alarms.write'],
    },
  }),
}))

vi.mock('vue-router', async (importOriginal) => {
  const actual = await importOriginal<typeof import('vue-router')>()
  const { reactive } = await import('vue')
  const query = reactive({})
  return {
    ...actual,
    useRouter: () => ({ push: vi.fn(), replace: vi.fn(() => Promise.resolve()) }),
    useRoute: () => ({ query }),
  }
})

/** 只桩布局；**AlertDialog 一律保留真件**。 */
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

let wrapper: ReturnType<typeof mount>

function rowByText(text: string) {
  return wrapper.findAll('tbody tr').find((r) => r.text().includes(text))
}

/** 弹层内容经 Teleport 挂到 body，断言要在 document 上找。 */
function confirmButton() {
  return [...document.querySelectorAll('[role="alertdialog"] button')].find((b) =>
    /^确认 \d+ 条$/.test(b.textContent?.trim() ?? ''),
  ) as HTMLButtonElement | undefined
}

function alertDialog() {
  return document.querySelector('[role="alertdialog"]')
}

async function openBatchAck(ids: string[]) {
  for (const id of ids) {
    await rowByText(id)!.find('[aria-label="选择行"]').trigger('click')
  }
  await nextTick()
  await wrapper
    .findAll('button')
    .find((b) => b.text().includes('批量确认'))!
    .trigger('click')
  await flushPromises()
  expect(alertDialog()).not.toBeNull()
}

beforeEach(() => {
  alarmState.acknowledgeAlarm.mockReset().mockResolvedValue(undefined)
  alarmState.refreshAlarms.mockReset().mockResolvedValue(undefined)
  alarmState.alarms = [
    {
      alarmEventId: 'ALM-1',
      externalAlarmId: 'ALM-1',
      deviceAssetId: 'DEV-OIL-01',
      alarmCode: 'TEMP-HIGH',
      severity: 'critical',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:00:00Z',
    },
    {
      alarmEventId: 'ALM-2',
      externalAlarmId: 'ALM-2',
      deviceAssetId: 'DEV-OIL-02',
      alarmCode: 'VIB-HIGH',
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:10:00Z',
    },
  ]
  wrapper = mount(AlarmsPage, { global: { stubs }, attachTo: document.body })
})

afterEach(() => {
  wrapper.unmount()
  document.body.innerHTML = ''
})

describe('批量确认报警在真弹层下的关闭时机', () => {
  it('写回进行中框还开着、确认按钮真的禁用——这一瞬只有普通 NvButton 才留得住', async () => {
    // 卡住写回，把「进行中」这一帧留在断言里。**每一行各有一个 promise**，
    // 只留最后一个 resolver 会让 allSettled 永远不结算——那是写这条用例时先踩的坑。
    const resolvers: (() => void)[] = []
    alarmState.acknowledgeAlarm.mockImplementation(
      () => new Promise<void>((resolve) => resolvers.push(() => resolve())),
    )

    await openBatchAck(['ALM-1', 'ALM-2'])
    const confirm = confirmButton()
    expect(confirm).toBeTruthy()
    expect(confirm!.disabled).toBe(false)

    confirm!.click()
    // 必须 flushPromises 而不是单个 nextTick：`nextTick` 之后 reka 的关框还没落到 DOM 上，
    // 于是「框还开着」这条对 NvAlertDialogAction 也成立——变异对照实测过，那样写杀不掉。
    await flushPromises()

    expect(alarmState.acknowledgeAlarm).toHaveBeenCalledTimes(2)
    // NvAlertDialogAction 会打破这两条：点击即无条件关框，disabled 一瞬都看不到。
    expect(alertDialog()).not.toBeNull()
    expect(confirmButton()!.disabled).toBe(true)

    for (const resolve of resolvers) resolve()
    await flushPromises()
    expect(alertDialog()).toBeNull()
  })

  it('禁用期间再点不会发出第二轮写回', async () => {
    const resolvers: (() => void)[] = []
    alarmState.acknowledgeAlarm.mockImplementation(
      () => new Promise<void>((resolve) => resolvers.push(() => resolve())),
    )

    await openBatchAck(['ALM-1', 'ALM-2'])
    confirmButton()!.click()
    await flushPromises()
    expect(alarmState.acknowledgeAlarm).toHaveBeenCalledTimes(2)

    // 框还在、按钮已禁用，所以第二下点不出第二轮写回。用 NvAlertDialogAction 时框已经没了，
    // 这一行会因为找不到确认按钮而直接炸——正是本条想守住的差别。
    confirmButton()!.click()
    await flushPromises()
    expect(alarmState.acknowledgeAlarm).toHaveBeenCalledTimes(2)

    for (const resolve of resolvers) resolve()
    await flushPromises()
  })

  it('全部成功：关框并清空选中集', async () => {
    await openBatchAck(['ALM-1', 'ALM-2'])

    confirmButton()!.click()
    await flushPromises()

    expect(alarmState.acknowledgeAlarm).toHaveBeenCalledTimes(2)
    expect(alertDialog()).toBeNull()
    expect(wrapper.text()).not.toContain('已选')
  })

  it('部分失败：关框但只保留失败行的选中态，用户可就地重发', async () => {
    alarmState.acknowledgeAlarm.mockImplementation((...args: unknown[]) =>
      args[0] === 'ALM-2' ? Promise.reject(new Error('boom')) : Promise.resolve(),
    )

    await openBatchAck(['ALM-1', 'ALM-2'])
    confirmButton()!.click()
    await flushPromises()

    expect(alertDialog()).toBeNull()
    // 重试落点是保留下来的选中集（票面子项 e），不是留在原地的确认框。
    expect(wrapper.text()).toContain('已选')
    expect(rowByText('ALM-2')!.find('[aria-label="选择行"]').attributes('aria-checked')).toBe(
      'true',
    )
    expect(rowByText('ALM-1')!.find('[aria-label="选择行"]').attributes('aria-checked')).toBe(
      'false',
    )
  })
})
