import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, reactive } from 'vue'

import BomAnalysisPage from './bom-analysis.vue'

const routeState = reactive<{ query: Record<string, unknown> }>({ query: {} })
const routerReplace = vi.fn(async (location: { query?: Record<string, unknown> }) => {
  routeState.query = location.query ?? {}
})

const api = vi.hoisted(() => ({
  getBusinessConsoleEngineeringBomExplosion: vi.fn(),
  getBusinessConsoleEngineeringManufacturingBomExplosion: vi.fn(),
  getBusinessConsoleEngineeringBomWhereUsed: vi.fn(),
  getBusinessConsoleEngineeringManufacturingBomWhereUsed: vi.fn(),
}))

vi.mock('vue-router', () => ({
  useRoute: () => routeState,
  useRouter: () => ({ replace: routerReplace }),
}))

vi.mock('@/stores/businessContext', () => ({
  useBusinessContextStore: () => ({ organizationId: 'org-001', environmentId: 'env-dev' }),
}))

vi.mock('@nerv-iip/api-client', async (importOriginal) => ({
  ...(await importOriginal<typeof import('@nerv-iip/api-client')>()),
  getBusinessConsoleEngineeringBomExplosion: api.getBusinessConsoleEngineeringBomExplosion,
  getBusinessConsoleEngineeringManufacturingBomExplosion: api.getBusinessConsoleEngineeringManufacturingBomExplosion,
  getBusinessConsoleEngineeringBomWhereUsed: api.getBusinessConsoleEngineeringBomWhereUsed,
  getBusinessConsoleEngineeringManufacturingBomWhereUsed: api.getBusinessConsoleEngineeringManufacturingBomWhereUsed,
}))

vi.mock('@nerv-iip/ui', () => {
  const passthrough = (tag = 'div') => ({ template: `<${tag}><slot /></${tag}>` })
  const modelInput = {
    props: ['modelValue'],
    emits: ['update:modelValue'],
    template: '<input :value="modelValue" @input="$emit(\'update:modelValue\', $event.target.value)" />',
  }
  return {
    ButtonPro: { template: '<button><slot /></button>' },
    DataTablePro: defineComponent({
      props: ['columns', 'rows', 'emptyMessage'],
      setup(props, { slots }) {
        return () => h('div', { 'data-testid': 'table' }, [
          ...(props.rows?.length
            ? props.rows.flatMap((row: Record<string, unknown>) =>
                props.columns.map((column: { key: string }) =>
                  h('div', { class: `cell-${column.key}` }, slots[`cell-${column.key}`]?.({ row }) ?? String(row[column.key] ?? '')),
                ),
              )
            : [h('p', props.emptyMessage)]),
        ])
      },
    }),
    DatePickerPro: modelInput,
    FieldPro: passthrough(),
    FieldProLabel: passthrough('label'),
    InputPro: modelInput,
    PageHeader: { props: ['title', 'description'], template: '<header><h1>{{ title }}</h1><p>{{ description }}</p></header>' },
    SectionCard: { props: ['description', 'value', 'hint'], template: '<section>{{ description }} {{ value }} {{ hint }}</section>' },
    SectionCards: passthrough('section'),
    SelectPro: {
      props: ['modelValue'],
      emits: ['update:modelValue'],
      template: '<select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
    },
    SelectProContent: { template: '<slot />' },
    SelectProItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
    SelectProTrigger: { template: '<slot />' },
    SelectProValue: { template: '<span />' },
    Spinner: passthrough('span'),
    StatusBadgePro: { props: ['label'], template: '<span>{{ label }}</span>' },
    Toolbar: passthrough('div'),
  }
})

const layoutStub = { BusinessLayout: { template: '<main><slot /></main>' } }

beforeEach(() => {
  routeState.query = {}
  routerReplace.mockClear()
  api.getBusinessConsoleEngineeringBomExplosion.mockReset()
  api.getBusinessConsoleEngineeringManufacturingBomExplosion.mockReset()
  api.getBusinessConsoleEngineeringBomWhereUsed.mockReset()
  api.getBusinessConsoleEngineeringManufacturingBomWhereUsed.mockReset()
})

describe('engineering bom analysis page', () => {
  it('用后端 explosion 响应渲染多级树和诊断', async () => {
    api.getBusinessConsoleEngineeringBomExplosion.mockResolvedValue({
      data: {
        success: true,
        data: {
          bomKind: 'engineering',
          selectionMode: 'effective',
          root: {
            itemCode: 'FG-100',
            requiredQuantity: 1,
            unitOfMeasureCode: 'PCS',
            children: [
              {
                itemCode: 'PCB-200',
                parentItemCode: 'FG-100',
                bomCode: 'EBOM-PCB',
                revision: 'B',
                requiredQuantity: 2.2,
                unitOfMeasureCode: 'PCS',
                isPhantom: true,
                children: [],
              },
            ],
          },
          diagnostics: [{ severity: 'warning', itemCode: 'PCB-200', message: '缺少下级有效版本', path: 'FG-100/PCB-200' }],
        },
      },
    })

    const wrapper = mount(BomAnalysisPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await wrapper.find('#bom-root').setValue('FG-100')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.getBusinessConsoleEngineeringBomExplosion).toHaveBeenCalledWith(expect.objectContaining({
      query: expect.objectContaining({ itemCode: 'FG-100', organizationId: 'org-001', environmentId: 'env-dev' }),
    }))
    expect(wrapper.text()).toContain('FG-100')
    expect(wrapper.text()).toContain('PCB-200')
    expect(wrapper.text()).toContain('虚拟件')
    expect(wrapper.text()).toContain('缺少下级有效版本')
  })

  it('MBOM 爆炸视图转调制造 BOM facade 并展示替代与位号', async () => {
    api.getBusinessConsoleEngineeringManufacturingBomExplosion.mockResolvedValue({
      data: {
        success: true,
        data: {
          bomKind: 'manufacturing',
          selectionMode: 'production-version',
          root: {
            itemCode: 'SKU-FG',
            requiredQuantity: 5,
            unitOfMeasureCode: 'PCS',
            children: [
              {
                itemCode: 'RM-1',
                level: 1,
                lineQuantity: 2,
                requiredQuantity: 10.5,
                unitOfMeasureCode: 'KG',
                scrapRate: 0.05,
                yieldRate: 1,
                substituteSkuCodes: 'RM-ALT',
                referenceDesignators: 'R1,R2',
                children: [],
              },
            ],
          },
          diagnostics: [],
        },
      },
    })

    const wrapper = mount(BomAnalysisPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().trim() === '爆炸')!.trigger('click')
    await wrapper.find('select').setValue('manufacturing')
    await wrapper.find('#bom-root').setValue('SKU-FG')
    await wrapper.find('#bom-lot').setValue('5')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.getBusinessConsoleEngineeringManufacturingBomExplosion).toHaveBeenCalledWith(expect.objectContaining({
      query: expect.objectContaining({ skuCode: 'SKU-FG', lotSize: 5 }),
    }))
    expect(wrapper.text()).toContain('RM-ALT')
    expect(wrapper.text()).toContain('R1,R2')
  })

  it('反查视图只调用 where-used facade 并显示父项上下文', async () => {
    api.getBusinessConsoleEngineeringBomWhereUsed.mockResolvedValue({
      data: {
        success: true,
        data: {
          componentCode: 'RM-1',
          items: [
            {
              bomKind: 'engineering',
              bomCode: 'EBOM-FG',
              revision: 'A',
              parentItemCode: 'FG-100',
              lineQuantity: 3,
              unitOfMeasureCode: 'PCS',
              effectiveDate: '2026-03-01',
              backflush: true,
            },
          ],
        },
      },
    })

    const wrapper = mount(BomAnalysisPage, { global: { stubs: layoutStub } })
    await flushPromises()
    await wrapper.findAll('button').find((b) => b.text().trim() === '反查')!.trigger('click')
    await wrapper.find('#bom-component').setValue('RM-1')
    await wrapper.find('form').trigger('submit')
    await flushPromises()

    expect(api.getBusinessConsoleEngineeringBomWhereUsed).toHaveBeenCalledWith(expect.objectContaining({
      query: expect.objectContaining({ componentCode: 'RM-1' }),
    }))
    expect(api.getBusinessConsoleEngineeringBomExplosion).not.toHaveBeenCalled()
    expect(wrapper.text()).toContain('FG-100')
    expect(wrapper.text()).toContain('EBOM-FG / A')
    expect(wrapper.text()).toContain('倒冲')
  })
})
