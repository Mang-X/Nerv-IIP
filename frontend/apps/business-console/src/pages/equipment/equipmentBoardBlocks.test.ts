import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, ref, shallowRef } from 'vue'

import EquipmentIndexPage from './index.vue'

// 设备看板「当前阻塞」卡片：原因徽标颜色与关联业务走真实的原因字典与关联业务口径，不桩掉。

const state = vi.hoisted(() => ({
  activeBlocks: [] as Array<Record<string, unknown>>,
}))

vi.mock('@/composables/useBusinessEquipment', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@/composables/useBusinessEquipment')>()),
  useBusinessEquipmentOverview: () => ({
    activeBlocks: computed(() => state.activeBlocks),
    deviceRosterError: computed(() => undefined),
    deviceRosterTotal: computed(() => 2),
    devices: computed(() => [
      { deviceAssetId: 'EQ00001', isSourceFresh: false },
      { deviceAssetId: 'EQ00002', currentState: 'running', isSourceFresh: false },
    ]),
    effectiveDeviceAssetIdCount: computed(() => 2),
    filters: { deviceAssetIds: '' },
    overviewError: computed(() => undefined),
    overviewPending: computed(() => false),
    overviewState: computed(() => 'ready'),
    refreshDeviceRoster: vi.fn(),
    refreshOverview: vi.fn(),
  }),
}))

vi.mock('@/composables/useEquipmentScopeSelection', () => ({
  useEquipmentScopeSelection: () => ({
    scope: ref({ workshop: '', line: '', device: '' }),
    levels: computed(() => []),
    devicesInScope: computed(() => []),
    scopePending: shallowRef(false),
  }),
}))

vi.mock('@/composables/useMasterDataDisplayNames', () => {
  const devices = [
    { code: 'EQ00001', name: '五轴加工中心' },
    { code: 'EQ00002', name: '清洗机' },
  ]
  const find = (reference?: string | null) => devices.find((d) => d.code === reference)
  return {
    useMasterDataDisplayNames: () => ({
      resolveDevice: (reference?: string | null) => find(reference)?.name,
      resolveDeviceCode: (reference?: string | null) => find(reference)?.code,
      resolveWorkCenter: () => undefined,
    }),
  }
})

vi.mock('@/stores/auth', () => ({
  useAuthStore: () => ({ principal: { permissionCodes: [] } }),
}))

vi.mock('vue-router', async (importOriginal) => ({
  ...(await importOriginal<typeof import('vue-router')>()),
  useRouter: () => ({ push: vi.fn(), replace: vi.fn() }),
}))

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { props: ['to'], template: '<a><slot /></a>' },
  DeviceQuickViewSheet: { template: '<div />' },
}

// 只在「当前阻塞」面板里找：设备列表「实时数据」一格也可能有同名徽标。
function reasonBadge(wrapper: ReturnType<typeof mount>, label: string) {
  const panel = wrapper
    .findAll('h2')
    .find((h) => h.text() === '当前阻塞')!
    .element.closest('.rounded-lg')!
  const badge = [...panel.querySelectorAll('[data-slot="nv-badge"]')]
    .map((el) => ({ text: () => el.textContent?.trim(), classes: () => [...el.classList] }))
    .find((b) => b.text() === label)
  expect(badge, `当前阻塞里没有「${label}」徽标`).toBeDefined()
  return badge!
}

const BOARD_BLOCKS = [
  {
    deviceAssetId: 'EQ00001',
    availabilityStatus: 'unknown',
    reasonCode: 'equipment.sourceNotConnected',
    sourceType: 'stale-source',
    sourceReferenceId: 'EQ00001',
    sourceReferenceLabel: 'EQ00001',
    startUtc: '2026-09-27T08:00:00Z',
  },
  {
    deviceAssetId: 'EQ00002',
    availabilityStatus: 'unknown',
    reasonCode: 'equipment.sourceStale',
    sourceType: 'stale-source',
    sourceReferenceId: '019fbb41-6666-7666-8666-666666666666',
    // IIoT 回填的是调用方传入的设备引用，这里给公开 ID：透传标签的写法会让 GUID 上屏。
    sourceReferenceLabel: '019fbb41-7777-7777-8777-777777777777',
    startUtc: '2026-09-27T08:00:00Z',
  },
]

describe('设备看板：当前阻塞卡片', () => {
  it('尚未接入采集用中性色，采集中断仍用危险色；关联业务显示设备编码而不是快照主键', () => {
    state.activeBlocks = BOARD_BLOCKS

    const wrapper = mount(EquipmentIndexPage, { global: { stubs } })

    expect(reasonBadge(wrapper, '尚未接入采集').classes()).toContain('bg-muted')
    expect(reasonBadge(wrapper, '尚未接入采集').classes()).not.toContain('bg-destructive/10')
    expect(reasonBadge(wrapper, '采集数据过期').classes()).toContain('bg-destructive/10')
    expect(wrapper.text()).toContain('关联业务 EQ00002')
    expect(wrapper.text()).not.toMatch(/[0-9a-f]{8}-[0-9a-f]{4}-/i)
  })

  it('设备列表「实时数据」：未接入的设备是中性色「尚未接入采集」，采集中断的仍是「暂无实时数据」', () => {
    state.activeBlocks = BOARD_BLOCKS
    const wrapper = mount(EquipmentIndexPage, { global: { stubs } })
    const freshnessCell = (code: string) => {
      const row = wrapper.findAll('tr.nv-dt-row').find((tr) => tr.text().includes(code))
      expect(row, `设备列表里没有 ${code}`).toBeDefined()
      return row!.findAll('[data-slot="nv-badge"]').at(-1)!
    }

    expect(freshnessCell('EQ00001').text()).toBe('尚未接入采集')
    expect(freshnessCell('EQ00001').classes()).toContain('bg-muted')
    expect(freshnessCell('EQ00002').text()).toBe('暂无实时数据')
    expect(freshnessCell('EQ00002').classes()).toContain('bg-warning/10')
  })
})
