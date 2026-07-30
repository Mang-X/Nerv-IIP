import { existsSync, readFileSync } from 'node:fs'
import { resolve } from 'node:path'
import { mount } from '@vue/test-utils'
import { NvPicker, NvScanBar } from '@nerv-iip/ui-mobile'
import { describe, expect, it } from 'vitest'
import WmsOperationalCandidatePicker from './WmsOperationalCandidatePicker.vue'

const locationOptions = [
  { value: 'A-01', label: 'A-01', hint: 'SITE-1 · SKU-1' },
  { value: 'B-02', label: 'B-02', hint: 'SITE-1 · SKU-2' },
]
const lotOptions = [{ value: 'LOT-A', label: 'LOT-A', hint: 'SKU-1 · A-01' }]

function mountPicker(showLot = true) {
  return mount(WmsOperationalCandidatePicker, {
    props: {
      locationOptions,
      lotOptions,
      showLot,
      sourceLabel: '当前范围仓储作业记录候选',
      ready: true,
      searchKeyword: '',
      asOfUtc: '2026-07-30T01:00:00Z',
      freshnessUtc: '2026-07-30T00:59:00Z',
      truncated: true,
    },
  })
}

describe('WMS operational candidate picker', () => {
  it('uses mobile picker and scanner components and exposes source metadata', () => {
    const wrapper = mountPicker()

    expect(wrapper.findAllComponents(NvPicker)).toHaveLength(2)
    expect(wrapper.findAllComponents(NvScanBar)).toHaveLength(1)
    expect(wrapper.text()).toContain('当前范围仓储作业记录候选')
    expect(wrapper.text()).not.toContain('warehouse-operational-records')
    expect(wrapper.text()).not.toContain('2026-07-30T01:00:00Z')
    expect(wrapper.text()).toContain('候选已截断')
    expect(wrapper.findAllComponents(NvPicker)[0]!.props('options')[0]).toEqual({
      label: '全部库位',
      value: '',
    })
  })

  it('uses candidates first and keeps an unmatched scan as an explicit unverified filter', async () => {
    const wrapper = mountPicker()
    const scanner = wrapper.findComponent(NvScanBar)

    scanner.vm.$emit('scan', 'A-01')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:locationCode')?.at(-1)).toEqual(['A-01'])

    scanner.vm.$emit('scan', 'UNKNOWN')
    await wrapper.vm.$nextTick()
    expect(wrapper.emitted('update:locationCode')?.at(-1)).toEqual(['UNKNOWN'])
    expect(wrapper.text()).toContain('已作为扫码筛选值应用')
    expect(wrapper.text()).toContain('候选可能因范围或截断不完整')
    expect(wrapper.text()).toContain('未验证为主数据')

    await wrapper
      .findAll('button')
      .find((button) => button.text() === '批次候选')!
      .trigger('click')
    expect(wrapper.text()).toContain('UNKNOWN')
    expect(wrapper.text()).toContain('清除扫码筛选')

    const clearButton = wrapper.findAll('button').find((button) => button.text() === '清除扫码筛选')
    expect(clearButton).toBeDefined()
    await clearButton!.trigger('click')
    expect(wrapper.emitted('update:locationCode')?.at(-1)).toEqual([undefined])
  })

  it('does not render lot controls for count candidates', () => {
    const wrapper = mountPicker(false)

    expect(wrapper.findAllComponents(NvPicker)).toHaveLength(1)
    expect(wrapper.findAllComponents(NvScanBar)).toHaveLength(1)
    expect(wrapper.text()).not.toContain('批次候选')
  })

  it('keeps location and lot inputs behind the mobile component boundary', () => {
    const localPath = resolve('src/components/wms/WmsOperationalCandidatePicker.vue')
    const source = readFileSync(
      existsSync(localPath)
        ? localPath
        : resolve(
            'frontend/apps/business-pda/src/components/wms/WmsOperationalCandidatePicker.vue',
          ),
      'utf8',
    )

    expect(source).toContain("from '@nerv-iip/ui-mobile'")
    expect(source).not.toContain("from '@nerv-iip/ui'")
    expect(source).not.toContain('<select')
    expect(source).not.toContain('NvMobileInput')
  })

  it('fails closed before scope readiness and exposes retryable candidate errors', async () => {
    const unready = mount(WmsOperationalCandidatePicker, {
      props: {
        locationOptions,
        lotOptions,
        sourceLabel: '当前范围仓储作业记录候选',
        ready: false,
      },
    })
    expect(unready.text()).toContain('请先选择可用作业范围')
    expect(unready.findComponent(NvScanBar).exists()).toBe(false)

    const failed = mount(WmsOperationalCandidatePicker, {
      props: {
        locationOptions: [],
        lotOptions: [],
        sourceLabel: '当前范围仓储作业记录候选',
        ready: true,
        error: new Error('network'),
      },
    })
    expect(failed.text()).toContain('候选加载失败')
    expect(failed.text()).not.toContain('暂无库位候选')
    await failed.get('[data-testid="candidate-retry"]').trigger('click')
    expect(failed.emitted('retry')).toHaveLength(1)
  })
})
