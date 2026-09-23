import { mount } from '@vue/test-utils'
import { describe, expect, it, vi } from 'vitest'
import { computed, reactive, ref } from 'vue'

import FoundationPage from './foundation.vue'

// 生产准备检查只要能说清「缺什么、去哪补」（#3771）：后端给出的 fixHint 必须出现在页面上，
// 否则用户只看到「缺费率」却不知道去哪个菜单维护。
const readiness = {
  status: 'Blocked',
  areas: [
    {
      areaCode: 'erp',
      status: 'Blocked',
      issues: [
        {
          code: 'WORK_CENTER_COST_RATE_MISSING',
          severity: 'Blocked',
          message: '工作中心 WC-TUB-01（制管一线）当前没有生效的成本费率',
          fixHint: '在「经营管理 ▸ 财务 ▸ 工作中心费率」为该工作中心新增费率修订',
        },
      ],
    },
    {
      areaCode: 'equipment',
      status: 'Warning',
      issues: [
        {
          code: 'OPERATION_TASK_DEVICE_UNASSIGNED',
          severity: 'Warning',
          message: '工单 WO-001 有 2 道待开工工序未绑定设备',
          fixHint: '在「制造执行 ▸ 计划与工单 ▸ 派工看板」给这些工序派工时选择设备',
        },
      ],
    },
  ],
  blockingIssues: [] as unknown[],
  warningIssues: [] as unknown[],
}
readiness.blockingIssues = readiness.areas[0]!.issues
readiness.warningIssues = readiness.areas[1]!.issues

vi.mock('@/composables/useBusinessMes', () => ({
  useMesFoundationReadiness: () => ({
    filters: reactive({}),
    readiness: computed(() => readiness),
    readinessError: ref(null),
    readinessPending: ref(false),
    refreshReadiness: vi.fn(),
  }),
}))

vi.mock('@/composables/useMesPickerCatalog', () => ({
  useProductionScopeCatalog: () => ({
    siteOptions: computed(() => []),
    sitesPending: ref(false),
    lineOptions: () => [],
    linesPending: ref(false),
    workCenterOptions: () => [],
    workCentersPending: ref(false),
  }),
  useMesMaterialVersionCatalog: () => ({
    skuOptions: computed(() => []),
    skusPending: ref(false),
    productionVersionOptions: () => [],
    productionVersionsPending: ref(false),
  }),
}))

describe('生产准备检查页', () => {
  it('每条问题都带出去哪里补的提示', () => {
    const wrapper = mount(FoundationPage, {
      global: { stubs: { BusinessLayout: { template: '<div><slot /></div>' } } },
    })

    const alert = wrapper.get('[role="alert"]').text()
    expect(alert).toContain('当前没有生效的成本费率')
    expect(alert).toContain('去处理：在「经营管理 ▸ 财务 ▸ 工作中心费率」为该工作中心新增费率修订')

    // 区域列要显示中文区域名，而不是裸区域码 erp。
    const areaCells = wrapper.findAll('tbody tr').map((row) => row.find('td').text())
    expect(areaCells).toEqual(['成本费率', '设备'])

    const text = wrapper.text()
    expect(text).toContain('去处理：在「制造执行 ▸ 计划与工单 ▸ 派工看板」给这些工序派工时选择设备')
  })
})
