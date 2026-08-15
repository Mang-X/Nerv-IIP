import { mount } from '@vue/test-utils'
import { NvMobileDropdownMenuItem } from '@nerv-iip/ui-mobile'
import { describe, expect, it } from 'vitest'

import WmsScopeStatusFilter from './WmsScopeStatusFilter.vue'

describe('WmsScopeStatusFilter', () => {
  it('将授权范围与业务状态选择回传给页面', async () => {
    const wrapper = mount(WmsScopeStatusFilter, {
      props: {
        scopeKey: 'self:emp049',
        status: 'Open',
        scopeOptions: [
          { label: '我的任务', value: 'self:emp049' },
          { label: '一号仓作业池', value: 'work-pool:WMS-SITE-001' },
        ],
        statusOptions: [
          { label: '全部状态', value: '' },
          { label: '待执行', value: 'Open' },
        ],
      },
    })
    const fields = wrapper.findAllComponents(NvMobileDropdownMenuItem)

    fields[0]!.vm.$emit('update:modelValue', 'work-pool:WMS-SITE-001')
    fields[1]!.vm.$emit('update:modelValue', '')

    expect(wrapper.emitted('update:scopeKey')?.at(-1)).toEqual(['work-pool:WMS-SITE-001'])
    expect(wrapper.emitted('update:status')?.at(-1)).toEqual([undefined])
  })
})
