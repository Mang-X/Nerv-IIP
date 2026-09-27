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

// 走查发现的架构级缺陷：组件此前只有 loading + emptyMessage，请求失败必然落进空态，
// 一个 500 和「真的 0 条」渲染出同一句安慰话。以下用例锁死三态不可再被合并。
describe('NvDataTable 空 / 失败 / 未查询三态（架构契约）', () => {
  const EMPTY = '当前工厂没有批次记录。'

  it('错误态：不出现 emptyMessage 文案，给出失败提示与重试入口', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        rows: [],
        pagination: false,
        emptyMessage: EMPTY,
        error: new Error('网关 502'),
      },
    })
    await nextTick()

    const text = wrapper.text()
    expect(text).not.toContain(EMPTY)
    // 失败态禁止任何安慰性措辞
    expect(text).not.toContain('暂无')
    expect(text).not.toContain('没有匹配的结果')
    expect(text).toContain('数据加载失败')
    expect(text).toContain('网关 502')

    const retry = wrapper.findAll('button').find((b) => b.text().includes('重新加载'))
    expect(retry).toBeDefined()
    await retry!.trigger('click')
    expect(wrapper.emitted('retry')).toHaveLength(1)
    // retry 与 refresh 语义不重叠，不得互相触发
    expect(wrapper.emitted('refresh')).toBeUndefined()
    wrapper.unmount()
  })

  it('错误态把失败原因放进活动区域，读屏可播报（列表已渲染时分页失败是唯一变化）', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        rows: [{ id: '1', name: '甲' }],
        pagination: false,
        emptyMessage: EMPTY,
        error: new Error('网关 502'),
      },
    })
    await nextTick()

    const alert = wrapper.get('[role="alert"]')
    expect(alert.text()).toContain('数据加载失败')
    expect(alert.text()).toContain('网关 502')
    wrapper.unmount()

    // 只有错误态播报：空态若也落进活动区域，正常查询无结果会被读屏当成告警。
    const empty = mount(NvDataTable, {
      props: { ...base, rows: [], pagination: false, emptyMessage: EMPTY },
    })
    await nextTick()
    expect(empty.find('[role="alert"]').exists()).toBe(false)
    empty.unmount()
  })

  it('错误态优先级高于空态与加载态（rows 为空 + error → 走错误态）', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        rows: [],
        pagination: false,
        emptyMessage: EMPTY,
        loading: true,
        error: '库存批次加载失败',
      },
    })
    await nextTick()

    expect(wrapper.text()).toContain('库存批次加载失败')
    expect(wrapper.text()).not.toContain(EMPTY)
    // 重试中：仍停在错误态，只把按钮置为进行中
    expect(wrapper.text()).toContain('重试中')
    wrapper.unmount()
  })

  it('错误态可被 #error 插槽整体覆盖', async () => {
    const wrapper = mount(NvDataTable, {
      props: { ...base, rows: [], pagination: false, emptyMessage: EMPTY, error: 'boom' },
      slots: { error: '<p>自定义失败呈现</p>' },
    })
    await nextTick()
    // 播报由组件承担，调用点换掉呈现也不必自己加 role="alert"
    expect(wrapper.get('[role="alert"]').text()).toContain('自定义失败呈现')
    expect(wrapper.text()).not.toContain(EMPTY)
    wrapper.unmount()
  })

  it('未查询态与空态渲染不同：说清还要选什么，且不说「暂无数据」', async () => {
    const awaiting = mount(NvDataTable, {
      props: {
        ...base,
        rows: [],
        pagination: false,
        emptyMessage: EMPTY,
        awaitingScope: true,
        awaitingScopeMessage: '请先选择物料后查询批次。',
      },
    })
    await nextTick()
    const empty = mount(NvDataTable, {
      props: { ...base, rows: [], pagination: false, emptyMessage: EMPTY },
    })
    await nextTick()

    expect(awaiting.text()).toContain('尚未发起查询')
    expect(awaiting.text()).toContain('请先选择物料后查询批次。')
    expect(awaiting.text()).not.toContain(EMPTY)
    expect(empty.text()).toContain(EMPTY)
    expect(empty.text()).not.toContain('尚未发起查询')
    expect(awaiting.text()).not.toBe(empty.text())
    awaiting.unmount()
    empty.unmount()
  })

  it('回归：不传新 prop 时行为与改动前一致（空态 / 加载骨架 / 有数据）', async () => {
    const empty = mount(NvDataTable, {
      props: { ...base, rows: [], pagination: false, emptyMessage: EMPTY },
    })
    await nextTick()
    expect(empty.text()).toContain(EMPTY)
    expect(empty.find('.nv-dt-state-icon').exists()).toBe(false)
    empty.unmount()

    const loading = mount(NvDataTable, {
      props: { ...base, rows: [], pagination: false, loading: true, skeletonRows: 3 },
    })
    await nextTick()
    expect(loading.findAll('tbody tr')).toHaveLength(3)
    expect(loading.text()).not.toContain('数据加载失败')
    loading.unmount()

    const rows: Row[] = [{ id: 'A', name: 'a' }]
    const ready = mount(NvDataTable, { props: { ...base, rows, pagination: false } })
    await nextTick()
    expect(ready.findAll('tbody tr')).toHaveLength(1)
    expect(ready.text()).toContain('a')
    ready.unmount()
  })
})

// #3734：列宽声明此前落在 <th> 上，而 <table> 是默认的 `table-layout: auto`，
// 于是 width 只是建议值、内容多的列把它挤掉（完工入库页〈入库状态〉声明 w-48
// 实测 64px，徽标被截成一个字）。修法是组件把表格切成 `table-layout: fixed`。
// jsdom 不实现表格布局、也不加载本仓库的样式表，因此这里断言的是**声明的宽度
// 真的落到了承载该宽度的元素上**（`w-*` 走 class、CSS 尺寸走 inline style），
// 也就是 fixed 布局要按它算列宽的那份输入；「声明宽度 == 渲染宽度」本身由
// `table-layout: fixed` 保证，见 NvDataTable.vue 样式块里的理由。
//
// 注意：本 describe 对样式块**零鉴别力**——把 `:deep(.nv-dt-table)` 回退成裸
// `.nv-dt-table`（即第 1 轮那版死 CSS）这里仍全绿。真正的防线是
// `apps/business-console/e2e/issue3734-column-width.spec.ts`，只有它能在真实浏览器
// 里量到渲染宽度。别把本组测试当成 `:deep` / `fixed` 的覆盖。
describe('NvDataTable 列宽声明落地到表头（#3734 回归）', () => {
  const rows: Row[] = [{ id: 'A', name: 'a' }]

  it('Tailwind 宽度类原样落到表头 class，不被组件改写成别的值', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        columns: [
          { key: 'id', header: '入库单号', width: 'w-48' },
          { key: 'name', header: '名称' },
        ],
        rows,
        pagination: false,
      },
    })
    await nextTick()

    const heads = wrapper.findAll('thead th')
    const declared = heads[0]
    // 均分列不吃调用点声明的类；声明的类原样出现在表头上。
    expect(declared.classes()).toContain('w-48')
    expect(declared.attributes('style')).toBeUndefined()
    // 未声明宽度的列继续走均分，且不得挂上别人的宽度类。
    expect(heads[1].classes()).toContain('nv-dt-fill')
    expect(heads[1].classes()).not.toContain('w-48')
    wrapper.unmount()
  })

  it('CSS 尺寸宽度落到表头 inline style，且不与宽度类混用', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        columns: [{ key: 'id', header: '工单号', width: '160px' }],
        rows,
        pagination: false,
      },
    })
    await nextTick()

    const head = wrapper.get('thead th')
    expect(head.attributes('style')).toContain('width: 160px')
    // 两种写法互斥：CSS 尺寸是 inline style，不再同时挂一个宽度类。
    expect(head.classes().some((c) => c.startsWith('w-'))).toBe(false)
    expect(head.classes()).not.toContain('nv-dt-fill')
    wrapper.unmount()
  })

  // 审核第 1、2 轮：table-layout: fixed 只认 width，min-w-* 挂在 <th> 上不参与列宽
  // 计算，wms/inbound 的〈质检门禁〉因此被算成 0px。这里刻意不解析 min-w-*（等于在
  // 本仓库重写一份 Tailwind 主题表，认不出的写法会静默变成 0px 的列），调用方改用
  // 任意值宽度类 `w-[22rem]`。本格钉的是「任意值宽度类原样作为 class 落到 <th>」。
  it('任意值宽度类 w-[22rem] 原样作为 class 落到表头', async () => {
    const wrapper = mount(NvDataTable, {
      props: {
        ...base,
        columns: [{ key: 'id', header: '质检门禁', width: 'w-[22rem]' }],
        rows,
        pagination: false,
      },
    })
    await nextTick()

    const head = wrapper.get('thead th')
    expect(head.classes()).toContain('w-[22rem]')
    expect(head.attributes('style')).toBeUndefined()
    // 声明了宽度就不该再进均分池（否则 `width: 100%` 会盖掉声明值）。
    expect(head.classes()).not.toContain('nv-dt-fill')
    wrapper.unmount()
  })
})
