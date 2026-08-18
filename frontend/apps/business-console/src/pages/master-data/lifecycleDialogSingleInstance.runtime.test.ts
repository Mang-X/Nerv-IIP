import { flushPromises, mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, shallowRef } from 'vue'

import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import UnitsPage from './units.vue'

/**
 * 运行时实例计数（#1591 验收项）。
 *
 * 源码扫描能挡住「页面忘了渲染确认框」，但挡不住「确认框其实被渲染了 N 次」——那要真挂一页、
 * 数组件实例才看得见。计量单位页有两张表（单位 + 换算），是这条断言最合适的样本：
 * **行操作 = 每行一个，确认框 = 整页一个。**
 */
const rows = [
  { resourceType: 'unit-of-measure', code: 'EA', displayName: '个', active: true },
  { resourceType: 'unit-of-measure', code: 'BOX', displayName: '箱', active: true },
  { resourceType: 'unit-of-measure', code: 'MPa', displayName: '兆帕', active: false },
]
const conversions = [
  { resourceType: 'uom-conversion', code: 'BOX→EA', displayName: 'BOX→EA', active: true },
]

const actionsStub = () => ({
  update: vi.fn(),
  disable: vi.fn().mockResolvedValue({}),
  enable: vi.fn().mockResolvedValue({}),
  fetchDetail: vi.fn().mockResolvedValue(undefined),
  updatePending: shallowRef(false),
  disablePending: shallowRef(false),
  enablePending: shallowRef(false),
  actionError: computed(() => undefined),
})

vi.mock('@/composables/useBusinessMasterData', () => ({
  useBusinessUoms: () => ({
    createUom: vi.fn(),
    createUomError: shallowRef(undefined),
    createUomPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    refreshUoms: vi.fn(),
    uoms: computed(() => rows),
    uomsError: shallowRef(undefined),
    uomsPending: shallowRef(false),
    uomsTotal: computed(() => rows.length),
  }),
  useUomConversions: () => ({
    createUomConversion: vi.fn(),
    createUomConversionError: shallowRef(undefined),
    createUomConversionPending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev', skip: 0, take: 10 }),
    refreshUomConversions: vi.fn(),
    uomConversions: computed(() => conversions),
    uomConversionsError: shallowRef(undefined),
    uomConversionsPending: shallowRef(false),
    uomConversionsTotal: computed(() => conversions.length),
  }),
  useBusinessMasterDataResources: () => ({
    resources: computed(() => []),
    total: computed(() => 0),
    error: shallowRef(undefined),
    pending: shallowRef(false),
    filters: reactive({ organizationId: 'org-001', environmentId: 'env-dev' }),
    refresh: vi.fn(),
  }),
  useMasterDataResourceActions: () => actionsStub(),
}))

// 弹层与下拉含 reka portal/Teleport，jsdom 卸载会崩——就地渲染；
// 注意**不 stub** MasterDataLifecycleDialog / MasterDataRowActions 本身，否则就数不到真实例了。
const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvDropdownMenuContent: { template: '<div><slot /></div>' },
  // NvDropdownMenuItem 是 reka 菜单项，会 inject MenuContent（已被上面的 stub 抹平）；
  // 本用例只数实例，不需要真菜单。
  NvDropdownMenuItem: { template: '<button type="button"><slot /></button>' },
  NvAlertDialog: { template: '<div><slot /></div>' },
  NvAlertDialogContent: { template: '<div><slot /></div>' },
  NvAlertDialogHeader: { template: '<div><slot /></div>' },
  NvAlertDialogFooter: { template: '<div><slot /></div>' },
  NvAlertDialogTitle: { template: '<h2><slot /></h2>' },
  NvAlertDialogDescription: { template: '<p><slot /></p>' },
  NvAlertDialogCancel: { template: '<button type="button"><slot /></button>' },
  NvDialog: { template: '<div><slot /></div>' },
  NvDialogTrigger: { template: '<div><slot /></div>' },
  NvDialogContent: { template: '<div><slot /></div>' },
  NvDialogHeader: { template: '<div><slot /></div>' },
  NvDialogFooter: { template: '<div><slot /></div>' },
  NvDialogTitle: { template: '<h2><slot /></h2>' },
  NvDialogDescription: { template: '<p><slot /></p>' },
}

describe('计量单位页的确认框实例数（运行时）', () => {
  it('行操作按行渲染，确认框整页只有一个', async () => {
    const wrapper = mount(UnitsPage, { global: { stubs } })
    await flushPromises()

    const triggers = wrapper.findAllComponents(MasterDataRowActions)
    const dialogs = wrapper.findAllComponents(MasterDataLifecycleDialog)

    // 触发器随行增长——这正是此前确认框也跟着增长的原因。
    expect(triggers.length).toBeGreaterThan(1)
    // 确认框不随行增长：整页一个实例。
    expect(dialogs).toHaveLength(1)
  })
})
