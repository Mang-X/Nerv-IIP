import type { VueWrapper } from '@vue/test-utils'
import { computed, nextTick, shallowRef, watch } from 'vue'
import { expect, vi } from 'vitest'

const unreadyMessage = shallowRef('')
let boundFilters: { scopeKind?: string; scopeId?: string; skip: number }

export function bindWmsWorkScopeFilters(
  filters: typeof boundFilters,
  catalog: 'receipts' | 'shipments' | 'counts',
) {
  boundFilters = filters
  const scopeKey = shallowRef('self:emp049')
  const scopeOptions = computed(() => [
    { label: '我的任务', value: 'self:emp049' },
    { label: '当前作业池', value: `work-pool:${catalog}` },
    { label: '备用作业池', value: `work-pool:${catalog}-alternate` },
  ])
  watch(
    scopeKey,
    (key) => {
      const [kind, id] = key.split(':')
      filters.scopeKind = kind
      filters.scopeId = id
    },
    { immediate: true },
  )
  return {
    scopeKey,
    scopeOptions,
    siteCode: computed(() => 'SITE-001'),
    selectedScopeLabel: computed(
      () => scopeOptions.value.find((option) => option.value === scopeKey.value)?.label ?? '',
    ),
    hasSelection: computed(() => !unreadyMessage.value),
    unreadyMessage,
    pending: shallowRef(false),
    error: shallowRef(undefined),
    refresh: vi.fn(async () => undefined),
  }
}

export const scopePageStubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  NvToolbar: { template: '<div><slot name="filters" /></div>' },
  NvSearchSelect: {
    props: ['modelValue', 'options'],
    emits: ['update:modelValue'],
    template: `<select :value="modelValue" @change="$emit('update:modelValue', $event.target.value)">
      <option v-for="option in options" :key="option.value" :value="option.value">{{ option.label }}</option>
    </select>`,
  },
  NvDataTable: {
    props: ['page', 'awaitingScope', 'awaitingScopeMessage'],
    emits: ['update:page'],
    template: `<div data-test="scope-table" :data-page="page">
      <button data-test="scope-page-2" @click="$emit('update:page', 2)">第 2 页</button>
      <p v-if="awaitingScope">{{ awaitingScopeMessage }}</p>
    </div>`,
  },
}

export async function assertScopePageBehavior(wrapper: VueWrapper, catalog: string) {
  try {
    const selector = wrapper.get('select[aria-label="作业范围"]')
    expect(selector.findAll('option').map((option) => option.attributes('value'))).toEqual([
      'self:emp049',
      `work-pool:${catalog}`,
      `work-pool:${catalog}-alternate`,
    ])
    const table = wrapper.get('[data-test="scope-table"]')

    for (const id of [catalog, `${catalog}-alternate`]) {
      await table.get('[data-test="scope-page-2"]').trigger('click')
      expect(table.attributes('data-page')).toBe('2')
      expect(boundFilters.skip).toBeGreaterThan(0)
      await selector.setValue(`work-pool:${id}`)
      expect(table.attributes('data-page')).toBe('1')
      expect(boundFilters).toMatchObject({ scopeKind: 'work-pool', scopeId: id, skip: 0 })
    }

    unreadyMessage.value = '没有已授权的作业范围，请联系仓库管理员。'
    await nextTick()
    expect(table.text()).toContain(unreadyMessage.value)
  } finally {
    wrapper.unmount()
    unreadyMessage.value = ''
  }
}
