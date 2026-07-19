import { mount } from '@vue/test-utils'
import { afterAll, beforeAll, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'
import NvDataTable from './NvDataTable.vue'
import type { NvDataTableColumn } from './types'

// 这两条锁住「服务端分页 / 受控排序」的公共契约。审核(#516)指出过两类回归：
// 未知 prop 会作为 DOM 属性透传，vue-tsc / 打桩单测都抓不到，只有挂真实组件才暴露。

interface Row {
  id: string
  name: string
}
// 列按 `object` 行类型标注：mount() 对泛型 SFC 默认 T=object，标 <Row> 会因 accessor 逆变而 TS2322。
const columns: NvDataTableColumn<object>[] = [
  { key: 'id', header: 'ID', sortable: true },
  { key: 'name', header: '名称' },
]
const base = { columns, rowKey: 'id', searchable: false, columnSettings: false }

beforeAll(() => {
  vi.stubGlobal(
    'ResizeObserver',
    class {
      observe() {}
      unobserve() {}
      disconnect() {}
    },
  )
})

afterAll(() => {
  vi.unstubAllGlobals()
})

describe('NvDataTable 服务端分页 + 受控排序（公共契约回归）', () => {
  it('headerTitle 通过可聚焦帮助按钮向键盘和触屏用户提供列头提示', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        columns: [{ key: 'name', header: '效期', headerTitle: 'FEFO 说明' }],
        rows: [],
        pagination: false,
      },
    })

    const trigger = wrapper.get('thead button[aria-label="效期：FEFO 说明"]')
    expect(trigger.attributes('type')).toBe('button')

    await trigger.trigger('pointerdown', { pointerType: 'touch' })
    await trigger.trigger('pointerup', { pointerType: 'touch' })
    await trigger.trigger('click')
    expect(document.body.textContent).toContain('FEFO 说明')
    wrapper.unmount()
  })

  // P1#1：调用点统一传 `:total-items`，故公共 prop 必须叫 `totalItems`。manual 下页脚用外部总数，
  // 不得回退到当前页行数——否则服务端多页时总数/页数全错、第 2 页会被夹回第 1 页。
  it('manual：页脚总数取外部 `totalItems`，而非当前页行数', async () => {
    const rows: Row[] = Array.from({ length: 10 }, (_, i) => ({
      id: `R${i + 1}`,
      name: `行 ${i + 1}`,
    }))
    const wrapper = mount(NvDataTable, {
      props: { ...base, rows, manual: true, totalItems: 95, pageSize: 10 },
    })
    await nextTick()
    expect(wrapper.text()).toContain('95') // “显示 1–10 / 95 条”
    expect(wrapper.text()).not.toContain('/ 10 条') // 若 totalItems 未绑定 → 回退 rows.length=10
  })

  // P1#2：页面用 `:client-sort="false"` + `v-model:sort`。关掉客户端排序时，点表头只发 update:sort
  // 交给父级受控，NvDataTable 不得擅自重排父级已分页好的整页。
  it('clientSort=false：点表头发 update:sort 且不内部重排', async () => {
    const rows: Row[] = [
      { id: 'B', name: 'b' },
      { id: 'A', name: 'a' },
      { id: 'C', name: 'c' },
    ]
    const wrapper = mount(NvDataTable, {
      props: { ...base, rows, pagination: false, clientSort: false, sort: null },
    })
    await nextTick()
    const firstCell = () => wrapper.findAll('tbody td')[0]?.text()
    const before = firstCell() // 原始顺序首行 = 'B'

    await wrapper.find('button.nv-dt-sort').trigger('click')
    await nextTick()

    expect(wrapper.emitted('update:sort')?.[0]?.[0]).toMatchObject({ key: 'id', direction: 'asc' })
    expect(firstCell()).toBe(before) // 受控：未内部按 id 升序重排（否则首行会变 'A'）
  })
})
