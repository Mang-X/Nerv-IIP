import { mount } from '@vue/test-utils'
import type { BusinessConsoleEquipmentHealthResponse } from '@nerv-iip/api-client'
import { describe, expect, it } from 'vitest'

import EquipmentHealthCard from './EquipmentHealthCard.vue'

const sourceGuid = '2d74d8d0-a0f8-4dd7-90ef-6397df66f8f7'

function healthFixture(
  overrides: Partial<BusinessConsoleEquipmentHealthResponse> = {},
): BusinessConsoleEquipmentHealthResponse {
  return {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    deviceAssetId: sourceGuid,
    healthScore: 68,
    level: 'warning',
    calculatedAtUtc: '2026-07-24T01:02:03Z',
    dataFreshness: {
      status: 'delayed',
      ageSeconds: 121,
      latestFactAtUtc: '2026-07-24T01:00:02Z',
      sourceFactType: sourceGuid,
      sourceFactLabel: '主轴振动最新采样',
    },
    riskFactors: [
      {
        ruleCode: 'threshold-proximity',
        ruleName: '阈值接近度',
        status: 'risk',
        penalty: 15,
        currentValue: '8.6',
        threshold: '8',
        unit: 'mm/s',
        evidence: '主轴振动已进入阈值风险区间。',
        sourceFactType: sourceGuid,
        sourceFactLabel: '主轴振动最新采样',
        sourceFactOccurredAtUtc: '2026-07-24T01:00:02Z',
      },
    ],
    ruleEvaluations: [
      {
        ruleCode: 'threshold-proximity',
        ruleName: '阈值接近度',
        status: 'risk',
        penalty: 15,
        currentValue: '8.6',
        threshold: '8',
        unit: 'mm/s',
        evidence: '主轴振动已进入阈值风险区间。',
        sourceFactType: sourceGuid,
        sourceFactLabel: '主轴振动最新采样',
        sourceFactOccurredAtUtc: '2026-07-24T01:00:02Z',
      },
      {
        ruleCode: 'runtime-hours-24h',
        ruleName: '近24小时生产运行时长',
        status: 'normal',
        penalty: 0,
        currentValue: '12',
        threshold: '20',
        unit: '小时',
        evidence: '近24小时生产运行低于门槛。',
        sourceFactLabel: '设备运行汇总',
        sourceFactOccurredAtUtc: '2026-07-24T00:58:00Z',
      },
      {
        ruleCode: 'alarm-frequency-24h',
        ruleName: '近24小时报警频次',
        status: 'normal',
        penalty: 0,
        currentValue: '0个活动，24小时1次',
        threshold: '任一活动或24小时≥3次',
        unit: '次',
        evidence: '当前报警频次未达到风险门槛。',
        sourceFactLabel: '报警生命周期',
        sourceFactOccurredAtUtc: '2026-07-23T23:00:00Z',
      },
      {
        ruleCode: 'sustained-exceedance',
        ruleName: '持续超限',
        status: 'accumulating',
        penalty: 0,
        currentValue: '3',
        threshold: '≥6个/≥30分钟/超限≥80%',
        unit: '样本',
        evidence: '当前只有3个样本，仍保留原始证据。',
        sourceFactLabel: '振动历史采样',
        sourceFactOccurredAtUtc: '2026-07-24T00:55:00Z',
      },
      {
        ruleCode: 'trend-growth',
        ruleName: '趋势恶化',
        status: 'normal',
        penalty: 0,
        currentValue: '8%',
        threshold: '20%',
        unit: '%',
        evidence: '趋势变化尚未达到风险门槛。',
        sourceFactLabel: '振动趋势窗口',
        sourceFactOccurredAtUtc: '2026-07-24T00:50:00Z',
      },
    ],
    ...overrides,
  }
}

describe('EquipmentHealthCard', () => {
  it('presents the score, freshness, triggered count, and all five evidence rows in business language', () => {
    const health = healthFixture()
    const wrapper = mount(EquipmentHealthCard, {
      props: { health, pending: false, error: undefined },
    })
    const text = wrapper.text()

    expect(text).toContain('设备健康')
    expect(text).toContain('68')
    expect(text).toContain('预警')
    expect(text).toContain('延迟')
    expect(text).toContain('命中 1 项风险')
    expect(text).toContain(
      new Date(health.calculatedAtUtc).toLocaleString('zh-CN', { hour12: false }),
    )

    for (const evaluation of health.ruleEvaluations) {
      expect(text).toContain(evaluation.ruleName)
      expect(text).toContain(evaluation.currentValue)
      expect(text).toContain(evaluation.threshold)
      expect(text).toContain(evaluation.unit)
      expect(text).toContain(evaluation.evidence)
      expect(text).toContain(evaluation.sourceFactLabel)
    }
    expect(text).toContain('风险')
    expect(text).toContain('正常')
    expect(text).toContain('历史数据积累中')
    expect(text).not.toContain(sourceGuid)
    expect(text).not.toContain('warning')
    expect(text).not.toContain('delayed')
    expect(text).not.toContain('accumulating')
  })

  it('keeps server-formatted values intact and presents units separately without duplication', () => {
    const wrapper = mount(EquipmentHealthCard, {
      props: { health: healthFixture(), pending: false },
    })

    const row = (name: string) => {
      const article = wrapper
        .findAll('article')
        .find((candidate) => candidate.get('h3').text() === name)
      expect(article).toBeDefined()
      return article!
    }

    const alarm = row('近24小时报警频次')
    expect(alarm.findAll('dd').map((cell) => cell.text())).toEqual([
      '0个活动，24小时1次',
      '任一活动或24小时≥3次',
      '次',
    ])

    const trend = row('趋势恶化')
    expect(trend.findAll('dd').map((cell) => cell.text())).toEqual(['8%', '20%', '%'])

    const accumulating = row('持续超限')
    expect(accumulating.findAll('dd').map((cell) => cell.text())).toEqual([
      '3',
      '≥6个/≥30分钟/超限≥80%',
      '样本',
    ])
    expect(accumulating.text()).toContain('历史数据积累中')
  })

  it.each([
    ['healthy', '健康'],
    ['watch', '关注'],
    ['warning', '预警'],
    ['critical', '严重'],
  ] as const)('maps health level %s to %s', (level, label) => {
    const wrapper = mount(EquipmentHealthCard, {
      props: { health: healthFixture({ level }), pending: false },
    })
    expect(wrapper.text()).toContain(label)
    expect(wrapper.text()).not.toContain(level)
  })

  it.each([
    ['fresh', '实时'],
    ['delayed', '延迟'],
    ['stale', '陈旧'],
    ['unavailable', '暂无数据'],
  ] as const)('maps freshness %s to %s', (status, label) => {
    const wrapper = mount(EquipmentHealthCard, {
      props: {
        health: healthFixture({
          dataFreshness: { status },
        }),
        pending: false,
      },
    })
    expect(wrapper.text()).toContain(label)
    expect(wrapper.text()).not.toContain(status)
  })

  it('keeps previous health visible during refresh and reports failures without discarding it', async () => {
    const wrapper = mount(EquipmentHealthCard, {
      props: { health: healthFixture(), pending: true },
    })
    expect(wrapper.text()).toContain('68')
    expect(wrapper.text()).toContain('正在刷新')

    await wrapper.setProps({ pending: false, error: '设备健康读取失败，请稍后重试' })
    expect(wrapper.text()).toContain('68')
    expect(wrapper.text()).toContain('设备健康读取失败，请稍后重试')
  })

  it('shows a dedicated initial loading state and a failure state when no previous data exists', async () => {
    const wrapper = mount(EquipmentHealthCard, {
      props: { health: undefined, pending: true },
    })
    expect(wrapper.text()).toContain('正在读取设备健康')

    await wrapper.setProps({
      pending: false,
      error: '设备健康读取失败，请稍后重试',
    })
    expect(wrapper.text()).toContain('设备健康读取失败，请稍后重试')
    expect(wrapper.text()).not.toContain('healthScore')
  })
})
