import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import * as barrel from './index'
import { displayValue, EMPTY_TEXT, isEmptyValue } from './lib/empty'
import { NV_SHEET_BLOCK_SIZE, NV_SHEET_INLINE_SIZE } from './components/pc/sheet'
import { resolveStatus } from './components/blocks/status-badge/statusMap'
import NvDescriptions from './components/pc/descriptions/NvDescriptions.vue'
import NvDataTable from './components/pc/data-table/NvDataTable.vue'
import NvRecordCard from './components/pc/record-card/NvRecordCard.vue'

/**
 * 基础件通病的回归闸门。
 *
 * owner 二轮走查暴露的是「一类」问题而不是几个点：字段空了就留白、卡片内容贴边、
 * 抽屉一律太窄、同排控件高矮不一。这些以前没有任何测试拦得住 —— 包里既没有
 * 间距/密度契约，也没有空值占位契约。这个文件把修完的约定固化下来。
 */

describe('空值占位约定', () => {
  it('占位符是 em dash，并从包的公共边界导出', () => {
    expect(EMPTY_TEXT).toBe('—')
    expect(barrel.EMPTY_TEXT).toBe('—')
    expect(typeof barrel.displayValue).toBe('function')
    expect(typeof barrel.isEmptyValue).toBe('function')
  })

  it('判空只认「真的没有值」，不吞 0 / false / NaN', () => {
    expect(isEmptyValue(undefined)).toBe(true)
    expect(isEmptyValue(null)).toBe(true)
    expect(isEmptyValue('')).toBe(true)
    expect(isEmptyValue('   ')).toBe(true)

    // 这三个是真实业务值：0 件库存、否、无效读数。用 `!value` 判空会把它们错误吞掉。
    expect(isEmptyValue(0)).toBe(false)
    expect(isEmptyValue(false)).toBe(false)
    expect(isEmptyValue(Number.NaN)).toBe(false)
  })

  it('displayValue 空值回落占位符，非空原样输出', () => {
    expect(displayValue(null)).toBe('—')
    expect(displayValue('')).toBe('—')
    expect(displayValue(0)).toBe('0')
    expect(displayValue('QC-001')).toBe('QC-001')
    expect(displayValue(null, '暂无')).toBe('暂无')
  })

  // 这是走查里「扫码记录的状态字段部分为空」那一类的根因。
  it('NvDescriptions 对带 key 的空值条目渲染占位符，而不是空白格', () => {
    const wrapper = mount(NvDescriptions, {
      props: {
        items: [
          { key: 'sn', label: '序列号', value: 'SN-001' },
          { key: 'status', label: '状态', value: '' },
          { key: 'operator', label: '操作人', value: null },
        ],
      },
    })

    expect(wrapper.text()).toContain('SN-001')
    // 两个空字段各出一个占位符 —— 旧实现里 `!item.key` 让 emptyText 对带 key 的条目完全失效。
    expect(wrapper.text().match(/—/g)?.length).toBe(2)
  })

  it('NvDataTable 空单元格渲染占位符', () => {
    const wrapper = mount(NvDataTable, {
      props: {
        columns: [
          { key: 'code', header: '编码' },
          { key: 'status', header: '状态' },
        ],
        rows: [{ code: 'SC-001', status: null }],
        rowKey: 'code',
        pagination: false,
        searchable: false,
      },
    })

    expect(wrapper.text()).toContain('SC-001')
    expect(wrapper.text()).toContain('—')
  })

  it('NvRecordCard 的 dt 一定配得上一个可见的 dd', () => {
    const wrapper = mount(NvRecordCard, {
      props: {
        recordNo: 'WO-001',
        meta: [{ label: '计划完工', value: undefined as unknown as string }],
      },
    })

    expect(wrapper.get('dd').text()).toBe('—')
  })
})

describe('抽屉尺寸档位', () => {
  const sizes = ['sm', 'md', 'lg', 'xl', '2xl', 'full'] as const

  it('六档齐全，左右调宽、上下调高', () => {
    for (const size of sizes) {
      expect(NV_SHEET_INLINE_SIZE[size]).toBeTruthy()
      expect(NV_SHEET_BLOCK_SIZE[size]).toBeTruthy()
      // 左右抽屉两个方向都要给到，否则 side="left" 会退回全宽。
      expect(NV_SHEET_INLINE_SIZE[size]).toContain('data-[side=left]')
      expect(NV_SHEET_INLINE_SIZE[size]).toContain('data-[side=right]')
    }
  })

  it('默认档比原来的 sm:max-w-sm 宽 —— 21 个调用点全部手写覆盖过它', () => {
    expect(NV_SHEET_INLINE_SIZE.sm).toContain('max-w-sm')
    expect(NV_SHEET_INLINE_SIZE.md).not.toContain('max-w-sm')
  })
})

describe('状态词表', () => {
  // 兜底会把原始英文码直接印到界面上，所以「后端在用的码」必须在词表里。
  const backendStatuses = [
    'confirmed',
    'partially-shipped',
    'credit-held',
    'pending-confirmation',
    'received',
    'requested',
    'accepted',
    'unrestricted',
    'quarantine',
    'scrapped',
    'hold',
    'quality',
    'disposition-in-progress',
    'effectiveness-verified',
    'superseded',
    'published',
    'degraded',
    'dismissed',
    'PartiallyReceived',
    'ReworkPending',
    'ScrapAccepted',
    'ReturnRequested',
    'InventoryPostingFailed',
    'PartiallyPosted',
  ]

  it.each(backendStatuses)('%s 有中文标签，不会把英文码印到界面上', (status) => {
    const { label } = resolveStatus(status)
    expect(label).not.toBe(status)
    // 中文标签里不应残留 ASCII 字母。
    expect(/[a-z]/i.test(label)).toBe(false)
  })

  it('kebab-case 与 PascalCase 归一到同一条词条', () => {
    expect(resolveStatus('partially-received').label).toBe(resolveStatus('PartiallyReceived').label)
    expect(resolveStatus('in-progress').label).toBe(resolveStatus('InProgress').label)
    expect(resolveStatus('conditional-release').label).toBe(resolveStatus('ConditionalRelease').label)
  })

  it('空值给「未知」，未登记的码原样回吐（便于发现漏登记）', () => {
    expect(resolveStatus(null).label).toBe('未知')
    expect(resolveStatus('').label).toBe('未知')
    expect(resolveStatus('brand-new-code').label).toBe('brand-new-code')
  })

  it('语义色调跟标签一致', () => {
    expect(resolveStatus('ScrapAccepted').tone).toBe('danger')
    expect(resolveStatus('InventoryPostingFailed').tone).toBe('danger')
    expect(resolveStatus('confirmed').tone).toBe('success')
    expect(resolveStatus('pending-confirmation').tone).toBe('warning')
    expect(resolveStatus('superseded').tone).toBe('neutral')
  })
})
