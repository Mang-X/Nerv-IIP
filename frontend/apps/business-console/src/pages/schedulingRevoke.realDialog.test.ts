import { flushPromises, mount } from '@vue/test-utils'
import { createPinia } from 'pinia'
import { computed, reactive, shallowRef } from 'vue'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import SchedulingPage from './scheduling.vue'

/**
 * **不 stub `NvAlertDialog*`** 的一组用例（#1613 子项 f · 排产页）。
 *
 * 撤销发布是终态动作（方案进「已撤销」、MES 侧回流撤销工序排程），确认按钮原先是
 * `NvAlertDialogAction`：包的是 reka `AlertDialogAction` → 渲染成 `DialogClose`，`@click` 里
 * `onOpenChange(false)` 无条件执行、不看 `defaultPrevented`。于是撤销失败时框早已消失，
 * `:disabled="revokePlanPending"` 也一瞬都看不到（confirm-destroy 规则 3）。
 *
 * `scheduling.test.ts` 已有两条撤销用例，但它们只断言了 `revokePlan` 调用与 toast——
 * **失败后框还在不在、pending 期间点不点得动，那两条都没问过**，所以把确认按钮改回
 * `NvAlertDialogAction` 也照样全绿。这份用例把关框时机变成不变量。
 *
 * 本文件另起 mock 是必要的：`scheduling.test.ts` 的部分用例桩了 `DialogRoot`
 * （reka `AlertDialogRoot` 内部就是它），桩掉真弹层拿不到上下文。
 */
const stub = vi.hoisted(() => ({
  revokePlan: vi.fn(),
  toastError: vi.fn(),
  toastSuccess: vi.fn(),
}))

vi.mock('@/composables/useSkuNames', async () => {
  const { computed } = await import('vue')
  return {
    useSkuNames: () => ({
      resolveSkuName: () => undefined,
      resolveSkuLabel: (code?: string | null) => code ?? '未指定物料',
      skuByCode: computed(() => new Map<string, string>()),
      skusPending: computed(() => false),
    }),
  }
})
vi.mock('@/composables/useBusinessPartnerNames', async () => {
  const { computed } = await import('vue')
  return {
    useBusinessPartnerNames: () => ({
      resolvePartner: () => undefined,
      resolvePartnerLabel: (code?: string | null, fallback = '未指定') => code ?? fallback,
      partnerByCode: computed(() => new Map<string, string>()),
      partners: computed(() => []),
      partnersPending: computed(() => false),
    }),
  }
})
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
vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRoute: () => ({ query: {} }),
}))
vi.mock('@/composables/useOrderUrgency', () => ({
  useOrderUrgencies: () => ({ byReference: { value: new Map() }, refresh: vi.fn() }),
}))
vi.mock('@/components/urgency/OrderUrgencyBadge.vue', () => ({
  default: { props: ['orderReference', 'mode', 'urgency'], template: '<span>未计算</span>' },
}))
vi.mock('@/components/mes/MesWorkScopeSelect.vue', () => ({
  default: { name: 'MesWorkScopeSelect', template: '<div />' },
}))
vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({
    principal: {
      permissionCodes: [
        'business.scheduling.plans.read',
        'business.scheduling.plans.manage',
        'business.scheduling.plans.release',
      ],
    },
  }),
}))
vi.mock('@/composables/useSchedulingWorkbench', () => ({
  useSchedulingWorkbench: () => ({
    candidates: computed(() => []),
    candidatesError: shallowRef(undefined),
    candidatesPending: shallowRef(false),
    candidatesScopeMessage: computed(() => ''),
    candidatesScopeReady: computed(() => true),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    generatePending: shallowRef(false),
    generatePlan: vi.fn(),
    refreshCandidates: vi.fn(),
    revisionPending: shallowRef(false),
    revisePlan: vi.fn(),
    schedulableCandidates: computed(() => []),
  }),
}))

/** `revokePlanPending` 必须是真 ref，否则「pending 期间禁点」那条会假绿。 */
const revokePlanPending = shallowRef(false)

vi.mock('@/composables/useBusinessScheduling', () => ({
  useBusinessScheduling: () => ({
    detailSelection: reactive({ planId: '' }),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    page: shallowRef(1),
    pageSize: shallowRef('100'),
    planDetail: computed(() => undefined),
    planDetailError: shallowRef(undefined),
    planDetailPending: shallowRef(false),
    plans: computed(() => [
      {
        planId: 'plan-released',
        status: 'released',
        generatedAtUtc: '2026-07-01T12:00:00Z',
        releasedAtUtc: '2026-07-01T12:30:00Z',
        assignmentCount: 4,
        conflictCount: 0,
        unscheduledOperationCount: 0,
      },
    ]),
    plansError: shallowRef(undefined),
    plansPending: shallowRef(false),
    releasePlan: vi.fn(),
    releasePlanPending: shallowRef(false),
    revokePlan: stub.revokePlan,
    revokePlanPending,
    upsertOperationOverride: vi.fn(),
    upsertOperationOverridePending: shallowRef(false),
    refreshPlans: vi.fn(),
  }),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

/** 只桩布局；**AlertDialog 与 `DialogRoot` 一律保留真件**。 */
const stubs = { BusinessLayout: { template: '<main><slot /></main>' } }

/** 真弹层挂 body，用例之间必须卸载。 */
let mounted: ReturnType<typeof mount> | null = null

function alertDialog() {
  return document.querySelector('[role="alertdialog"]')
}

/** 弹层内容经 Teleport 挂到 body，断言要在 document 上找。 */
function confirmButton() {
  return [...document.querySelectorAll('[role="alertdialog"] button')].find((b) =>
    b.textContent?.includes('确认撤销'),
  ) as HTMLButtonElement | undefined
}

async function openRevokeConfirm() {
  const wrapper = mount(SchedulingPage, {
    global: { plugins: [createPinia()], stubs },
    attachTo: document.body,
  })
  mounted = wrapper
  await flushPromises()

  const tab = wrapper.findAll('[role="tab"]').find((t) => t.text().includes('表格'))!
  await tab.trigger('focus')
  await tab.trigger('mousedown')
  await flushPromises()

  await wrapper
    .findAll('tbody tr')
    .find((row) => row.text().includes('plan-released'))!
    .findAll('button')
    .find((b) => b.text().includes('撤销发布'))!
    .trigger('click')
  await flushPromises()

  // 触发只开框，不发请求（confirm-destroy 规则 2）。
  expect(alertDialog()).not.toBeNull()
  expect(stub.revokePlan).not.toHaveBeenCalled()
  return wrapper
}

beforeEach(() => {
  revokePlanPending.value = false
  stub.revokePlan.mockReset()
  stub.revokePlan.mockResolvedValue({
    success: true,
    data: { planId: 'plan-released', status: 'revoked' },
  })
  stub.toastError.mockClear()
  stub.toastSuccess.mockClear()
})

afterEach(() => {
  mounted?.unmount()
  mounted = null
  document.body.innerHTML = ''
})

describe('排程方案撤销发布确认框在真弹层下的关闭时机', () => {
  it('撤销失败时框保持打开，用户可原地重试', async () => {
    stub.revokePlan.mockRejectedValueOnce(new Error('方案不处于已发布状态'))
    await openRevokeConfirm()

    const confirm = confirmButton()
    expect(confirm).toBeTruthy()
    confirm!.click()
    await flushPromises()

    expect(stub.revokePlan).toHaveBeenCalledWith('plan-released')
    expect(stub.toastError).toHaveBeenCalledWith('撤销失败：方案不处于已发布状态')
    expect(stub.toastSuccess).not.toHaveBeenCalled()
    // 这一条正是 NvAlertDialogAction 会打破的：点击即无条件关框。
    expect(alertDialog()).not.toBeNull()
    expect(confirmButton()).toBeTruthy()
  })

  it('撤销成功才关框', async () => {
    await openRevokeConfirm()

    confirmButton()!.click()
    await flushPromises()

    expect(stub.toastSuccess).toHaveBeenCalled()
    expect(alertDialog()).toBeNull()
  })

  it('pending 期间确认按钮禁用——这一瞬只有普通 NvButton 才留得住', async () => {
    await openRevokeConfirm()
    expect(confirmButton()!.disabled).toBe(false)

    revokePlanPending.value = true
    await flushPromises()

    expect(confirmButton()!.disabled).toBe(true)
    expect(alertDialog()).not.toBeNull()
  })
})
