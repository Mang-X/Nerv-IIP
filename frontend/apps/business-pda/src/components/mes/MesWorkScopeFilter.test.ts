import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { computed, ref } from 'vue'

const scopeOptionsRef = ref<Array<{ label: string; value: string }>>([])
const selectionRef = ref<string | undefined>(undefined)

vi.mock('@/composables/useBusinessMes', () => ({
  useMesWorkScopeSelection: () => ({
    scopeOptions: computed(() => scopeOptionsRef.value),
    scopeSelectionValue: selectionRef,
  }),
}))

import MesWorkScopeFilter from './MesWorkScopeFilter.vue'

describe('PDA MES 作业范围选择器', () => {
  beforeEach(() => {
    scopeOptionsRef.value = [
      { label: '精加工一线（工作中心）', value: 'work-center:WC-A' },
      { label: '精加工二线（工作中心）', value: 'work-center:WC-B' },
    ]
    selectionRef.value = 'work-center:WC-A'
  })

  it('展示当前范围，并把授权清单渲染成可切换的选项', async () => {
    const wrapper = mount(MesWorkScopeFilter, {
      props: { permissionCode: 'business.mes.operations.read' },
    })

    const trigger = wrapper.get('[data-slot="dropdown-menu-item"] button')
    expect(trigger.text()).toContain('精加工一线（工作中心）')

    await trigger.trigger('click')
    const options = wrapper.findAll('[data-slot="dropdown-menu-item"] button')
    expect(options.map((option) => option.text())).toEqual([
      '精加工一线（工作中心）',
      '精加工一线（工作中心）',
      '精加工二线（工作中心）',
    ])
  })

  it('选中另一个范围时把选择写回共享选择', async () => {
    const wrapper = mount(MesWorkScopeFilter, {
      props: { permissionCode: 'business.mes.operations.read' },
    })

    await wrapper.get('[data-slot="dropdown-menu-item"] button').trigger('click')
    const options = wrapper.findAll('[data-slot="dropdown-menu-item"] button')
    await options[2].trigger('click')

    expect(selectionRef.value).toBe('work-center:WC-B')
  })

  it('没有已授权范围时不渲染选择器（由页面统一给出「去哪配」的提示）', () => {
    scopeOptionsRef.value = []
    const wrapper = mount(MesWorkScopeFilter, {
      props: { permissionCode: 'business.mes.operations.read' },
    })

    expect(wrapper.find('[data-testid="mes-work-scope-select"]').exists()).toBe(false)
  })
})
