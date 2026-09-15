import { flushPromises, mount } from '@vue/test-utils'
import { NvDataTable } from '@nerv-iip/ui'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import FinanceIndexPage from './finance/index.vue'
import VouchersPage from './finance/vouchers.vue'
import { stableRowKey } from './shared'

/**
 * #3278 S4：凭证表的 `row-key` 必须钉在稳定 id 上，凭证号只负责显示。
 *
 * 这不是换个字段的风格改动。原写法 `(r) => r.voucherNo ?? '凭证'` 一旦同页出现两行 key 相同，
 * Vue 的 keyed diff 离开「顺序未变」的快路径后就会多渲染出并不存在的行。下面第一组用例直接把
 * 这个形态钉死：整批换行后**行数与逐格内容**必须与数据一致，且不得出现
 * `[Vue warn]: Duplicate keys found during update`。
 *
 * ⚠️ 本文件的夹具（`voucherNo` 为空 / 撞号）是**当前后端契约产不出**的输入——域侧
 * `ErpText.Required` 拒空、`voucher_no` 列 NOT NULL、`(org, env, voucher_no)` 上有无 filter 的
 * 唯一索引。这不影响用例有效：它们证的是**渲染层不变式**，不是「后端今天会这么回」。
 * 真实可达路径（网关↔Erp 版本偏斜、S6/S8 改写编号形状）见 `shared.ts` 里 `stableRowKey` 的注释。
 */

const hoisted = vi.hoisted(() => ({ rows: null as { value: Record<string, unknown>[] } | null }))

vi.mock('@/composables/useBusinessErp', async () => {
  const { computed, reactive, shallowRef } = await import('vue')
  const rows = shallowRef<Record<string, unknown>[]>([])
  hoisted.rows = rows
  return {
    useErpFinanceSummary: () => ({
      ready: computed(() => true),
      summary: computed(() => ({
        openReceivableAmount: 14_853_060,
        openPayableAmount: 3_458_646.25,
        costCandidateAmount: 1_387_052.5,
        postedVoucherCount: 5590,
      })),
      summaryError: shallowRef(undefined),
      summaryPending: shallowRef(false),
      refreshSummary: vi.fn(),
    }),
    useErpJournalVouchers: () => ({
      filters: reactive({ status: undefined, keyword: undefined, skip: 0, take: 10 }),
      items: computed(() => rows.value),
      total: computed(() => 60),
      organizationId: computed(() => 'org-001'),
      environmentId: computed(() => 'env-dev'),
      error: shallowRef(undefined),
      pending: shallowRef(false),
      ready: computed(() => true),
      refresh: vi.fn(),
      postVoucher: vi.fn(),
      postVoucherPending: shallowRef(false),
      postVoucherError: shallowRef(undefined),
    }),
  }
})

vi.mock('@/composables/usePagedList', async () => {
  const { shallowRef } = await import('vue')
  return {
    usePagedList: () => ({
      page: shallowRef(1),
      pageSize: shallowRef('10'),
      pageSizeNumber: shallowRef(10),
      resetPage: vi.fn(),
    }),
  }
})

const stubs = {
  BusinessLayout: { template: '<main><slot /></main>' },
  RouterLink: { template: '<a><slot /></a>' },
}

/** 一张凭证；`voucherNo` 传 undefined 表示服务端没给出凭证号（见文件头：当前契约产不出）。 */
function voucher(id: string, voucherNo: string | undefined, amount: number) {
  return {
    id,
    voucherNo,
    postingDate: '2026-03-01T00:00:00Z',
    status: 'POSTED',
    totalDebitAmount: amount,
    totalCreditAmount: amount,
  }
}

/**
 * 真实可达形态：服务端整批换行（翻页 / 关键字过滤 / 刷新），且同一页里**部分凭证缺号**。
 * 两页各自内部有重复 key，且跨页首尾都对不齐——正好落进 keyed diff 的乱序分支。
 */
const PAGE_1 = [
  voucher('v-1', undefined, 111),
  voucher('v-2', undefined, 222),
  voucher('v-3', 'JV-2026-0003', 333),
]
const PAGE_2 = [
  voucher('v-4', 'JV-2026-0004', 444),
  voucher('v-5', undefined, 555),
  voucher('v-6', undefined, 666),
]

function expectedRowTexts(rows: ReturnType<typeof voucher>[]) {
  return rows.map(
    (r) =>
      `${r.voucherNo ?? '-'}|2026/3/1|已过账|¥${r.totalDebitAmount}.00|¥${r.totalCreditAmount}.00`,
  )
}

function renderedRowTexts(wrapper: ReturnType<typeof mount>) {
  return wrapper.findAll('tbody tr').map((tr) =>
    tr
      .findAll('td')
      .map((td) => td.text())
      .join('|'),
  )
}

beforeEach(() => {
  hoisted.rows!.value = []
})

describe.each([
  ['会计凭证页 vouchers.vue', VouchersPage],
  ['财务摘要页 finance/index.vue', FinanceIndexPage],
])('#3278 S4 %s 的凭证表 row-key', (_name, Page) => {
  it('整批换行后行数与逐格内容都与数据一致，且不出现重复 key 告警', async () => {
    const duplicateKeyWarnings: string[] = []
    const warnSpy = vi.spyOn(console, 'warn').mockImplementation((...args: unknown[]) => {
      const text = args.map(String).join(' ')
      if (text.includes('Duplicate keys found during update')) duplicateKeyWarnings.push(text)
    })

    hoisted.rows!.value = PAGE_1
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    expect(renderedRowTexts(wrapper)).toEqual(expectedRowTexts(PAGE_1))

    hoisted.rows!.value = PAGE_2
    await flushPromises()

    // 旧写法（`r.voucherNo ?? '凭证'`）在这一步实测渲染出 **4** 行，
    // 且首行重复显示了 ¥666.00——多出来的是一张并不存在的凭证。
    expect(renderedRowTexts(wrapper)).toHaveLength(PAGE_2.length)
    expect(renderedRowTexts(wrapper)).toEqual(expectedRowTexts(PAGE_2))
    expect(duplicateKeyWarnings).toEqual([])

    warnSpy.mockRestore()
  })

  it('同一批逻辑行以全新对象重新到达时，<tr> DOM 节点必须被复用（证明 key 真的钉在服务端 id 上）', async () => {
    // 这条是**行为层**守着「row-key 钉的是稳定 id」这个票面目标本身。
    // 上面两条只要求 key 两两互异——把 id 那一支整个删掉、一律发代理 key 也满足，
    // 所以它们对「有没有用 id」零鉴别力。
    //
    // refetch 语义：同一批凭证重新到达时是**全新的对象**（JSON 反序列化产物），
    // 只有 key 取自服务端 id 才认得出「还是这几行」，从而原地 patch、复用 DOM 节点；
    // 一旦回落到按对象引用发的代理 key，新对象 = 新 key = 整行卸载重挂。
    hoisted.rows!.value = PAGE_1
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()
    const before = wrapper.findAll('tbody tr').map((row) => row.element)
    expect(before).toHaveLength(PAGE_1.length)

    // 同样的三张凭证、同样的 id，但每个都是新对象（深拷贝，行内 lines 也重建）。
    hoisted.rows!.value = PAGE_1.map((row) => ({ ...row }))
    await flushPromises()
    const after = wrapper.findAll('tbody tr').map((row) => row.element)

    expect(after).toHaveLength(before.length)
    // ⚠️ 必须逐元素比**身份**（`===`）。对数组整体用 toEqual 是**结构**比较，
    // 整行重建出来的新节点结构相同，会判相等——那样这条断言就是假绿。
    for (const [index, element] of after.entries()) {
      expect(element === before[index]).toBe(true)
    }
  })

  it('页面交给表格的 row-key 对缺号 / 撞号的行仍然两两互异', async () => {
    // 同一页里：两行缺号 + 两行凭证号真的撞在一起 + 一行正常。
    const rows = [
      voucher('v-a', undefined, 1),
      voucher('v-b', undefined, 2),
      voucher('v-c', 'JV-DUP', 3),
      voucher('v-d', 'JV-DUP', 4),
      voucher('v-e', 'JV-2026-0009', 5),
    ]
    hoisted.rows!.value = rows
    const wrapper = mount(Page, { global: { stubs } })
    await flushPromises()

    // 取页面**实际传给** NvDataTable 的那个函数，而不是在用例里重写一份。
    // NvDataTable 是泛型组件，VTU 的 findComponent 重载会退化到 DOMWrapper，这里显式收窄。
    const table = wrapper.findComponent(NvDataTable) as unknown as {
      props: (name: string) => unknown
    }
    const rowKey = table.props('rowKey') as (row: unknown) => unknown
    expect(typeof rowKey).toBe('function')

    const keys = rows.map((row) => rowKey(row))
    expect(new Set(keys).size).toBe(rows.length)
    // 凭证号降为纯显示：它不得再出现在 key 里，撞号的两行才不会互相吃掉。
    expect(keys).not.toContain('JV-DUP')
  })
})

describe('#3278 S4 stableRowKey 兜底语义', () => {
  it('有 id 时直接用 id', () => {
    expect(stableRowKey({ id: 'v-1' })).toBe('v-1')
  })

  it('缺 id 时同一行对象重复取到同一个 key（重渲染稳定）', () => {
    const row = { id: undefined, voucherNo: undefined }
    expect(stableRowKey(row)).toBe(stableRowKey(row))
  })

  it('缺 id 时不同行对象必然拿到不同 key，且不回落到任何业务字段', () => {
    // 后两行连**凭证号都撞在一起**：旧写法会让它们共用一个 key。
    const a = { id: undefined, voucherNo: undefined }
    const b = { id: undefined, voucherNo: undefined }
    const c = { id: undefined, voucherNo: 'JV-DUP' }
    const d = { id: undefined, voucherNo: 'JV-DUP' }
    const keys = [stableRowKey(a), stableRowKey(b), stableRowKey(c), stableRowKey(d)]
    expect(new Set(keys).size).toBe(4)
    expect(keys).not.toContain('JV-DUP')
    expect(keys).not.toContain('凭证')
  })
})
