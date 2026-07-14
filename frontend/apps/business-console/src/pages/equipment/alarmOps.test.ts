import { mount } from '@vue/test-utils'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, nextTick, shallowRef } from 'vue'

import AlarmsPage from './alarms.vue'

const alarmState = vi.hoisted(() => ({
  alarms: [] as Array<Record<string, unknown>>,
  acknowledgeAlarm: vi.fn((..._args: unknown[]) => Promise.resolve()),
  shelveAlarm: vi.fn((..._args: unknown[]) => Promise.resolve()),
  unshelveAlarm: vi.fn((..._args: unknown[]) => Promise.resolve()),
  refreshAlarms: vi.fn((..._args: unknown[]) => Promise.resolve()),
}))

const routerState = vi.hoisted(() => ({
  replace: vi.fn((..._args: unknown[]) => Promise.resolve()),
  query: {} as Record<string, unknown>,
}))

vi.mock('@/composables/useBusinessEquipment', () => ({
  useBusinessEquipmentAlarms: () => ({
    acknowledgeAlarm: alarmState.acknowledgeAlarm,
    alarms: computed(() => alarmState.alarms),
    alarmsError: shallowRef(),
    alarmsPending: shallowRef(false),
    refreshAlarms: alarmState.refreshAlarms,
    shelveAlarm: alarmState.shelveAlarm,
    unshelveAlarm: alarmState.unshelveAlarm,
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
  // Reactive query so the page's URL→state watcher fires on external navigation / back-forward.
  routerState.query = reactive(routerState.query)
  return {
    ...actual,
    useRouter: () => ({ push: vi.fn(), replace: routerState.replace }),
    useRoute: () => ({ query: routerState.query }),
  }
})

function resetQuery(next: Record<string, unknown> = {}) {
  for (const k of Object.keys(routerState.query)) delete routerState.query[k]
  Object.assign(routerState.query, next)
}
// router.replace writes the query back into the reactive route so the full
// state→router→route round-trip (and the URL→state watcher) is exercised for real.
function resetRouter() {
  routerState.replace.mockReset().mockImplementation((...args: unknown[]) => {
    const loc = args[0] as { query?: Record<string, unknown> } | undefined
    resetQuery(loc?.query ?? {})
    return Promise.resolve()
  })
  resetQuery()
}

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
}

function seedAlarms() {
  alarmState.alarms = [
    {
      alarmEventId: 'ALM-1',
      deviceAssetId: 'DEV-OIL-01',
      alarmCode: 'TEMP-HIGH',
      severity: 'critical',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:00:00Z',
      escalatedAtUtc: '2026-07-12T02:00:00Z',
      escalationReason: '15 分钟未确认',
      escalationRecipientRefs: ['班组长', '设备主管'],
    },
    {
      alarmEventId: 'ALM-2',
      deviceAssetId: 'DEV-OIL-02',
      alarmCode: 'VIB-HIGH',
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:10:00Z',
    },
    {
      alarmEventId: 'ALM-3',
      deviceAssetId: 'DEV-PACK-01',
      alarmCode: 'PRESSURE-LOW',
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:20:00Z',
      acknowledgedAtUtc: '2026-07-12T01:30:00Z',
      acknowledgedBy: 'operator-b',
    },
    {
      alarmEventId: 'ALM-4',
      deviceAssetId: 'DEV-PACK-02',
      alarmCode: 'DOOR-OPEN',
      severity: 'info',
      status: 'shelved',
      raisedAtUtc: '2026-07-12T01:40:00Z',
      shelvedUntilUtc: '2026-07-12T03:40:00Z',
      shelvedBy: 'operator-c',
    },
    {
      // Escalated AND acknowledged — 升级与处置正交:必须仍归入「已确认」且显示确认人。
      alarmEventId: 'ALM-5',
      deviceAssetId: 'DEV-CNC-09',
      alarmCode: 'SPINDLE-OVERTEMP',
      severity: 'critical',
      status: 'raised',
      raisedAtUtc: '2026-07-12T01:50:00Z',
      acknowledgedAtUtc: '2026-07-12T02:05:00Z',
      acknowledgedBy: 'operator-d',
      escalatedAtUtc: '2026-07-12T02:10:00Z',
      escalationReason: '严重报警自动升级',
      escalationRecipientRefs: ['设备主管'],
    },
    {
      // Shelved AND acknowledged — 处置列必须同时显示搁置与确认两个事实。
      alarmEventId: 'ALM-6',
      deviceAssetId: 'DEV-BOIL-03',
      alarmCode: 'LEVEL-LOW',
      severity: 'warning',
      status: 'shelved',
      raisedAtUtc: '2026-07-12T02:20:00Z',
      shelvedUntilUtc: '2026-07-12T04:20:00Z',
      shelvedBy: 'operator-e',
      acknowledgedAtUtc: '2026-07-12T02:25:00Z',
      acknowledgedBy: 'operator-f',
    },
  ]
}

function rowByText(wrapper: ReturnType<typeof mount>, text: string) {
  return wrapper.findAll('tbody tr').find((r) => r.text().includes(text))
}
async function clickViewTab(wrapper: ReturnType<typeof mount>, label: string) {
  const tab = wrapper.findAll('[role="tab"]').find((b) => b.text().startsWith(label))
  await tab!.trigger('click')
  await nextTick()
}

describe('alarm ops depth (MAN-441 #795)', () => {
  beforeEach(() => {
    alarmState.acknowledgeAlarm.mockReset().mockResolvedValue(undefined)
    alarmState.shelveAlarm.mockReset().mockResolvedValue(undefined)
    alarmState.refreshAlarms.mockReset().mockResolvedValue(undefined)
    resetRouter()
    seedAlarms()
  })

  it('marks every escalated row with a red icon and never dims escalated rows', () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    const icons = wrapper.findAll('[aria-label="报警已升级"]')
    // ALM-1 (escalated) + ALM-5 (escalated & acknowledged) both carry the marker.
    expect(icons).toHaveLength(2)
    expect(icons[0].classes()).toContain('text-destructive')
    // ALM-5 is acknowledged but escalated → stays solid (not dimmed).
    expect(rowByText(wrapper, 'ALM-5')?.classes()).not.toContain('opacity-55')
  })

  it('de-emphasizes acknowledged / shelved rows but keeps active + escalated rows solid', () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    expect(rowByText(wrapper, 'ALM-1')?.classes()).not.toContain('opacity-55') // escalated
    expect(rowByText(wrapper, 'ALM-2')?.classes()).not.toContain('opacity-55') // raised
    expect(rowByText(wrapper, 'ALM-3')?.classes()).toContain('opacity-55') // acknowledged
    expect(rowByText(wrapper, 'ALM-4')?.classes()).toContain('opacity-55') // shelved
  })

  it('treats 升级 as orthogonal: an escalated+acknowledged alarm stays in 已确认 with confirmer visible', async () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })

    // 已确认 view = acknowledgedAtUtc set, regardless of escalation → ALM-3 and ALM-5.
    await clickViewTab(wrapper, '已确认')
    const ackRows = wrapper.findAll('tbody tr')
    const ids = ackRows.map((r) => r.text())
    expect(ids.some((t) => t.includes('ALM-5'))).toBe(true)
    expect(ids.some((t) => t.includes('ALM-3'))).toBe(true)
    expect(ids.some((t) => t.includes('ALM-2'))).toBe(false) // 待确认, excluded

    // Disposition (confirmer + time) is not masked by escalation.
    expect(rowByText(wrapper, 'ALM-5')?.text()).toContain('确认于')
    expect(rowByText(wrapper, 'ALM-5')?.text()).toContain('operator-d')

    // 已升级 view is independent → ALM-5 also appears there (matches two views).
    await clickViewTab(wrapper, '已升级')
    expect(rowByText(wrapper, 'ALM-5')).toBeTruthy()
    expect(rowByText(wrapper, 'ALM-1')).toBeTruthy()
  })
})

describe('alarm ops — shelve validation + batch retry (attaches to body for teleported dialog)', () => {
  let wrapper: ReturnType<typeof mount>

  beforeEach(() => {
    alarmState.acknowledgeAlarm.mockReset().mockResolvedValue(undefined)
    alarmState.shelveAlarm.mockReset().mockResolvedValue(undefined)
    alarmState.refreshAlarms.mockReset().mockResolvedValue(undefined)
    resetRouter()
    seedAlarms()
    wrapper = mount(AlarmsPage, { global: { stubs }, attachTo: document.body })
  })
  afterEach(() => {
    wrapper.unmount()
    document.body.innerHTML = ''
  })

  function q<T extends Element = HTMLElement>(sel: string): T | null {
    return document.body.querySelector<T>(sel)
  }
  function nativeClick(el: Element | null) {
    el?.dispatchEvent(new MouseEvent('click', { bubbles: true, cancelable: true }))
  }
  async function setInput(sel: string, value: string) {
    const el = q<HTMLInputElement>(sel)!
    el.value = value
    el.dispatchEvent(new Event('input', { bubbles: true }))
    await nextTick()
  }
  function dialogConfirmBtn() {
    // The shelve dialog footer confirm button (excludes 取消 / 放弃重试).
    const btns = [...document.body.querySelectorAll('[data-slot=nv-dialog-content] button')]
    return btns.find(
      (b) =>
        /搁置|确认搁置|重试/.test(b.textContent ?? '') && !/取消|放弃/.test(b.textContent ?? ''),
    ) as HTMLButtonElement | undefined
  }
  async function selectRows(ids: string[]) {
    for (const id of ids) {
      const cb = rowByText(wrapper, id)?.find('[aria-label="选择行"]')
      await cb!.trigger('click')
    }
    await nextTick()
  }
  async function openBatchShelve(ids: string[]) {
    await selectRows(ids)
    const btn = wrapper.findAll('button').find((b) => b.text().includes('批量搁置'))
    await btn!.trigger('click')
    await nextTick()
    await new Promise((r) => setTimeout(r, 0))
  }

  it('blocks a custom duration outside 1..1440 with an inline field error (no toast, no request)', async () => {
    await openBatchShelve(['ALM-1', 'ALM-2'])
    // choose 自定义
    const customRadio = [...document.body.querySelectorAll('[data-slot=nv-radio-group-item]')].find(
      (el) => (el.closest('div')?.textContent ?? '').includes('自定义'),
    )
    nativeClick(customRadio!)
    await nextTick()
    await setInput('#shelve-custom-minutes', '1441')
    await setInput('#shelve-reason', '等待备件')
    await nextTick()

    expect(document.body.textContent).toContain('请输入 1–1440 之间的整数分钟。')
    const confirm = dialogConfirmBtn()
    expect(confirm?.disabled).toBe(true)
    nativeClick(confirm ?? null)
    await nextTick()
    expect(alarmState.shelveAlarm).not.toHaveBeenCalled()
  })

  it('on partial failure keeps only the failed row selected and retries it with the SAME idempotency key', async () => {
    let failAlm2 = true
    alarmState.shelveAlarm.mockImplementation((...args: unknown[]) =>
      args[0] === 'ALM-2' && failAlm2 ? Promise.reject(new Error('boom')) : Promise.resolve(),
    )

    await openBatchShelve(['ALM-1', 'ALM-2'])
    await setInput('#shelve-reason', '计划内检修')
    await nextTick()

    // Run 1: ALM-1 succeeds, ALM-2 fails.
    nativeClick(dialogConfirmBtn() ?? null)
    await new Promise((r) => setTimeout(r, 0))
    await nextTick()

    const run1 = alarmState.shelveAlarm.mock.calls
    const keyOf = (calls: unknown[][], id: string) =>
      (calls.find((c) => c[0] === id)?.[4] as { idempotencyKey?: string } | undefined)
        ?.idempotencyKey
    const alm2Key1 = keyOf(run1, 'ALM-2')
    expect(run1).toHaveLength(2)
    expect(alm2Key1).toMatch(/^shelve:ALM-2:/)

    // Only the failed row remains selected (locatable) and queued as the retry target.
    const bulkText = wrapper.text()
    expect(bulkText).toContain('已选')
    expect(bulkText).toContain('1')

    // Locked: duration + reason inputs disabled so the frozen key/payload cannot drift.
    expect(q<HTMLInputElement>('#shelve-reason')?.disabled).toBe(true)
    expect(document.body.textContent).toContain('放弃重试')

    // Close is blocked while locked (Esc / 取消 must not drop the frozen intent).
    document
      .querySelector('[data-slot=nv-dialog-content]')
      ?.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
    await nextTick()
    expect(q('[data-slot=nv-dialog-content]')).not.toBeNull() // still open

    // Run 2 (retry): ALM-2 now succeeds; the frozen key must be identical → idempotent.
    failAlm2 = false
    alarmState.shelveAlarm.mockClear()
    nativeClick(dialogConfirmBtn() ?? null)
    await new Promise((r) => setTimeout(r, 0))
    await nextTick()

    const run2 = alarmState.shelveAlarm.mock.calls
    expect(run2).toHaveLength(1)
    expect(run2[0][0]).toBe('ALM-2')
    expect(keyOf(run2, 'ALM-2')).toBe(alm2Key1)
  })

  it('commits the locked retry state even when the post-batch refresh fails', async () => {
    alarmState.shelveAlarm.mockImplementation((...args: unknown[]) =>
      args[0] === 'ALM-2' ? Promise.reject(new Error('boom')) : Promise.resolve(),
    )
    // Refresh rejects — it must not undo the failed-row retention or the locked state.
    alarmState.refreshAlarms.mockRejectedValue(new Error('refresh down'))

    await openBatchShelve(['ALM-1', 'ALM-2'])
    await setInput('#shelve-reason', '计划内检修')
    await nextTick()
    nativeClick(dialogConfirmBtn() ?? null)
    await new Promise((r) => setTimeout(r, 0))
    await nextTick()

    // Dialog still open + locked (fields disabled), failed row retained for retry.
    expect(q('[data-slot=nv-dialog-content]')).not.toBeNull()
    expect(q<HTMLInputElement>('#shelve-reason')?.disabled).toBe(true)
    expect(document.body.textContent).toContain('放弃重试')
  })

  it('exposes aria-invalid + aria-describedby on the native input when the duration is invalid', async () => {
    await openBatchShelve(['ALM-1', 'ALM-2'])
    const customRadio = [...document.body.querySelectorAll('[data-slot=nv-radio-group-item]')].find(
      (el) => (el.closest('div')?.textContent ?? '').includes('自定义'),
    )
    nativeClick(customRadio!)
    await nextTick()
    await setInput('#shelve-custom-minutes', '5000')
    await nextTick()

    const input = q<HTMLInputElement>('#shelve-custom-minutes')!
    expect(input.getAttribute('aria-invalid')).toBe('true')
    const describedBy = input.getAttribute('aria-describedby')
    expect(describedBy).toBe('shelve-custom-minutes-error')
    expect(q(`#${describedBy}`)?.getAttribute('role')).toBe('alert')
  })
})

describe('alarm ops — view filtering (orthogonal, selection prune, URL, page reset)', () => {
  beforeEach(() => {
    alarmState.acknowledgeAlarm.mockReset().mockResolvedValue(undefined)
    alarmState.shelveAlarm.mockReset().mockResolvedValue(undefined)
    alarmState.refreshAlarms.mockReset().mockResolvedValue(undefined)
    resetRouter()
    seedAlarms()
  })

  it('shows both 搁置 and 确认 facts for a shelved+acknowledged alarm in 已确认', async () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    await clickViewTab(wrapper, '已确认')
    const row = rowByText(wrapper, 'ALM-6')
    expect(row).toBeTruthy() // acknowledged → in 已确认 even though also shelved
    expect(row?.text()).toContain('搁置至')
    expect(row?.text()).toContain('确认于')
    expect(row?.text()).toContain('operator-f') // acknowledger visible
  })

  it('prunes hidden selection and writes the view to the URL on switch', async () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    // Select everything in 全部.
    await wrapper.find('[aria-label="全选"]').trigger('click')
    await nextTick()
    expect(wrapper.text()).toContain('已选')

    // Switch to 已升级 (only ALM-1, ALM-5 match) → selection pruned to the visible set,
    // so the bulk bar cannot act on now-hidden rows.
    await clickViewTab(wrapper, '已升级')
    expect(routerState.replace).toHaveBeenCalledWith(
      expect.objectContaining({ query: expect.objectContaining({ view: 'escalated' }) }),
    )
    // Batch-confirm target count is now scoped to the escalated view (ALM-1 unacked only;
    // ALM-5 already acknowledged) → at most the escalated＋unacked rows, never all 6.
    const ackBtn = wrapper.findAll('button').find((b) => b.text().includes('批量确认'))
    const m = ackBtn?.text().match(/\((\d+)\)/)
    expect(Number(m?.[1] ?? 0)).toBeLessThanOrEqual(2)
  })

  it('initializes the active view from the URL query', () => {
    resetQuery({ view: 'shelved' })
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    // 已搁置 view → ALM-4 and ALM-6 (shelved), not the raised ALM-2.
    expect(rowByText(wrapper, 'ALM-4')).toBeTruthy()
    expect(rowByText(wrapper, 'ALM-2')).toBeUndefined()
  })

  it('resets the table to page 1 when switching to a narrower view (no empty page)', async () => {
    // 15 raised alarms + 1 escalated so 全部 paginates; 已升级 has a single row.
    const many = Array.from({ length: 15 }, (_, i) => ({
      alarmEventId: `BULK-${i + 1}`,
      deviceAssetId: 'DEV-X',
      alarmCode: `C-${i + 1}`,
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T00:00:00Z',
    }))
    alarmState.alarms = [
      ...many,
      {
        alarmEventId: 'ESC-1',
        deviceAssetId: 'DEV-Y',
        alarmCode: 'ESC',
        severity: 'critical',
        status: 'raised',
        raisedAtUtc: '2026-07-12T00:10:00Z',
        escalatedAtUtc: '2026-07-12T00:20:00Z',
      },
    ]
    const wrapper = mount(AlarmsPage, { global: { stubs } })

    // Go to page 2 of 全部 (16 rows / 10 per page).
    const page2 = wrapper.findAll('button').find((b) => b.text().trim() === '2')
    await page2!.trigger('click')
    await nextTick()

    // Switch to 已升级 (1 row). Page must reset → the escalated row is visible, not an empty page.
    await clickViewTab(wrapper, '已升级')
    expect(rowByText(wrapper, 'ESC-1')).toBeTruthy()
    expect(wrapper.findAll('tbody tr').length).toBe(1)
  })

  it('reflects external URL changes back into the view (browser back/forward)', async () => {
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    expect(rowByText(wrapper, 'ALM-2')).toBeTruthy() // 全部 initially

    // Simulate navigation to ?view=escalated (external / history) → view updates reactively.
    routerState.query.view = 'escalated'
    await nextTick()
    expect(rowByText(wrapper, 'ALM-1')).toBeTruthy()
    expect(rowByText(wrapper, 'ALM-2')).toBeUndefined() // raised, not in 已升级

    // Back to 全部 (query cleared) → view resets.
    delete routerState.query.view
    await nextTick()
    expect(rowByText(wrapper, 'ALM-2')).toBeTruthy()
  })

  function seedRaised(count: number) {
    alarmState.alarms = Array.from({ length: count }, (_, i) => ({
      alarmEventId: `P-${i + 1}`,
      deviceAssetId: 'DEV-Z',
      alarmCode: `C-${i + 1}`,
      severity: 'warning',
      status: 'raised',
      raisedAtUtc: '2026-07-12T00:00:00Z',
    }))
  }

  it('round-trips page through the URL (state → router.replace → reactive route)', async () => {
    seedRaised(25)
    const wrapper = mount(AlarmsPage, { global: { stubs } })

    const page2 = wrapper.findAll('button').find((b) => b.text().trim() === '2')
    await page2!.trigger('click')
    await nextTick()
    // router.replace writes back into the reactive route → the query now carries page=2.
    expect(routerState.query.page).toBe('2')
  })

  it('round-trips pageSize through the URL and resets page to 1 (default page omitted)', async () => {
    seedRaised(25)
    resetQuery({ page: '2' }) // start on page 2 so we can prove the reset
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    // The NvDataTable footer emits update:page-size when the operator changes the size.
    wrapper.findComponent({ name: 'NvDataTable' }).vm.$emit('update:page-size', 20)
    await nextTick()
    expect(routerState.query.pageSize).toBe('20')
    expect(routerState.query.page).toBeUndefined() // reset to 1 → default omitted
  })

  it('honors pageSize from the URL query', () => {
    seedRaised(25)
    resetQuery({ pageSize: '20' })
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    expect(wrapper.findAll('tbody tr').length).toBe(20)
  })

  it('clamps an out-of-range page from the URL and normalizes the query (no empty table)', async () => {
    seedRaised(25) // 3 pages at 10 per page
    resetQuery({ page: '99' })
    const wrapper = mount(AlarmsPage, { global: { stubs } })
    await nextTick()
    // Clamped to the last page → rows are visible, not an empty table…
    expect(wrapper.findAll('tbody tr').length).toBeGreaterThan(0)
    // …and the query is normalized to the last valid page.
    expect(routerState.query.page).toBe('3')
  })
})
