import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'
import SchedulingPlanRisks from './SchedulingPlanRisks.vue'
import SchedulingMaterialShortageSummary from './SchedulingMaterialShortageSummary.vue'

// Regression / #4463：八工序 × 十一物料保留全部事实，默认只展示分组摘要。
describe('方案冻结风险的信息层级', () => {
  it('groups eight operations by order while preserving every shortage and equipment fact', async () => {
    const materialRisks = Array.from({ length: 8 }, (_, i) => ({
      orderId: 'WO-MAIN',
      operationId: `OP-${i + 1}`,
      message: `开工前备料 ${i + 1}`,
      shortages: Array.from({ length: 11 }, (_, j) => ({
        materialId: `MAT-${j + 1}`,
        requiredQuantity: 5,
        availableQuantity: 2,
        shortageQuantity: 3,
      })),
    }))
    const wrapper = mount(SchedulingPlanRisks, {
      props: {
        materialRisks,
        equipmentRisks: [
          { orderId: 'WO-MAIN', operationId: 'OP-1', resourceId: 'DEV-01', message: '采集已过期' },
          { orderId: 'WO-MAIN', operationId: 'OP-2', resourceId: 'DEV-01', message: '尚无快照' },
        ],
      },
    })
    expect(wrapper.text()).toContain('方案生成时的快照，不随 MES 备料更新')
    expect(wrapper.text()).toContain('当前齐套状态请到 MES')
    const material = wrapper.get('[data-testid="scheduling-material-risks"]')
    expect(material.get('summary').text()).toContain('8 道工序 · 1 个工单')
    expect(material.attributes('open')).toBeUndefined()
    await material.get('summary').trigger('click')
    const group = material.get('details')
    expect(group.get('summary').text()).toContain('WO-MAIN · 8 道工序')
    await group.get('summary').trigger('click')
    expect(material.findAll('article')).toHaveLength(8)
    for (const article of material.findAll('article')) {
      expect(article.findAll('li')).toHaveLength(11)
      expect(article.text()).toContain('MAT-11')
      expect(article.text()).toMatch(/缺 3（需 5，可用\s*2）/)
    }
    const equipment = wrapper.get('[data-testid="scheduling-equipment-risks"]')
    expect(equipment.get('summary').text()).toContain('2 道工序 · 1 个资源')
    expect(equipment.get('details').get('summary').text()).toContain('DEV-01 · 2 道工序')
    expect(equipment.text()).toContain('采集已过期')
    expect(equipment.text()).toContain('尚无快照')
  })

  it('does not claim current MES readiness when the frozen snapshot has no shortage', () => {
    const wrapper = mount(SchedulingMaterialShortageSummary, { props: { shortages: [] } })
    expect(wrapper.text()).toContain('方案快照未记录缺料')
    expect(wrapper.text()).not.toContain('方案物料齐套')
  })

  it('keeps the batch shortage quantity and all affected operations without repeating the list upfront', () => {
    const wrapper = mount(SchedulingMaterialShortageSummary, {
      props: {
        shortages: [
          {
            materialId: 'MAT-SHARED',
            materialLotId: 'LOT-1',
            shortageQuantity: 3,
            uomCode: 'kg',
            affectedOperations: Array.from({ length: 8 }, (_, i) => ({
              orderId: 'WO-MAIN',
              operationId: `OP-${i + 1}`,
            })),
          },
        ],
      },
    })
    expect(wrapper.get('tbody tr').text()).toContain('3 kg')
    expect(wrapper.get('summary').text()).toContain('1 项物料 / 批次缺口')
    expect(wrapper.get('tbody summary').text()).toContain('8 道工序')
    expect(wrapper.get('details').attributes('open')).toBeUndefined()
    expect(wrapper.findAll('li')).toHaveLength(8)
    expect(wrapper.text()).toContain('LOT-1')
  })
})
