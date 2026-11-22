import { mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { nextTick } from 'vue'

import { planningSpies, resetPlanningWorkbench } from './planningWorkbenchTestFixture'
import PlanningWorkbench from './PlanningWorkbench.vue'

describe('PlanningWorkbench', () => {
  beforeEach(resetPlanningWorkbench)

  it('只在最近完成的 MRP 有后续需求变更时显示过期横幅', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.mrpRunsRef!.value = [
      { runId: 'retrying', status: 'Running', demandChangeCount: 9 },
      { runId: 'completed', status: 'Completed', demandChangeCount: 3 },
      { runId: 'older', status: 'Completed', demandChangeCount: 7 },
    ]
    await nextTick()
    expect(wrapper.get('[role="alert"]').text()).toContain('MRP 结果已过期（3 条需求变更）')

    planningSpies.mrpRunsRef!.value = [
      { runId: 'completed', status: 'Completed', demandChangeCount: 0 },
    ]
    await nextTick()
    expect(wrapper.text()).not.toContain('MRP 结果已过期')

    planningSpies.mrpRunsRef!.value = [{ runId: 'running', status: 'Running' }]
    await nextTick()
    expect(wrapper.text()).not.toContain('MRP 结果已过期')
  })

  // MAN-700 / #1289：RunMrp 500 曾把英文 `Internal Server Error` 常驻在页面上，
  // 且 submitMrpRun 没有 try/catch，异常逃逸后弹框永远关不掉。
  it('RunMrp 500 只走 toast 人话，英文原文既不上屏也不留常驻错误条', async () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    planningSpies.runMrp = vi.fn(async () => {
      throw { title: 'Internal Server Error', status: 500 }
    })
    const wrapper = mount(PlanningWorkbench)

    // 未捕获就会变成 unhandled rejection（vitest 直接判错），本用例同时守住「弹框不卡死」。
    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.toastError).toHaveBeenCalledWith('运行 MRP 失败，请稍后重试。')
    expect(planningSpies.toastError).not.toHaveBeenCalledWith(
      expect.stringContaining('Internal Server'),
    )
    expect(wrapper.text()).not.toContain('Internal Server Error')
    consoleError.mockRestore()
  })

  it('RunMrp 被后端按领域理由拒绝时原样透传那句中文', async () => {
    planningSpies.runMrp = vi.fn(async () => {
      throw { status: 400, detail: '计划期内没有已发布的主计划行' }
    })
    const wrapper = mount(PlanningWorkbench)

    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.toastError).toHaveBeenCalledWith(
      '运行 MRP 失败：计划期内没有已发布的主计划行',
    )
  })

  // #1306 异步任务模式：提交=受理，弹框全程可关闭，后台跑完由 watch 统一 toast。
  it('RunMrp 提交即受理并提示后台计算中', async () => {
    const wrapper = mount(PlanningWorkbench)

    await wrapper.findAll('form')[0]!.trigger('submit')
    await Promise.resolve()

    expect(planningSpies.runMrp).toHaveBeenCalled()
    expect(planningSpies.toastSuccess).toHaveBeenCalledWith(
      'MRP 已受理，正在后台计算，完成后自动刷新。',
    )
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })

  it('轮询到完成态时 toast 建议数并收起弹框', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.status = 'completed'
    planningSpies.activeMrpRun.suggestionCount = 5
    await wrapper.vm.$nextTick()

    expect(planningSpies.toastSuccess).toHaveBeenCalledWith('MRP 计算完成，共生成 5 条计划建议。')
  })

  it('MRP 自动重试期间不报最终失败，重试成功只通知完成', async () => {
    const wrapper = mount(PlanningWorkbench)
    planningSpies.activeMrpRun.runId = 'run-retrying'
    planningSpies.activeMrpRun.status = 'running'
    await wrapper.vm.$nextTick()
    expect(planningSpies.toastError).not.toHaveBeenCalled()

    planningSpies.activeMrpRun.status = 'completed'
    planningSpies.activeMrpRun.suggestionCount = 2
    await wrapper.vm.$nextTick()
    expect(planningSpies.toastSuccess).toHaveBeenCalledWith('MRP 计算完成，共生成 2 条计划建议。')
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })

  it('轮询到失败态时把 failureReason 走分层透传上屏', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.failureReason = 'MRP 计算失败：上游库存快照不可用。'
    planningSpies.mrpRunsRef!.value = [
      {
        runId: 'run-async-1',
        status: 'Failed',
        failureReason: planningSpies.activeMrpRun.failureReason,
      },
    ]
    planningSpies.activeMrpRun.status = 'failed'
    await wrapper.vm.$nextTick()

    // 后端前缀被去重：不出现「MRP 计算失败：MRP 计算失败：…」的叠层。
    expect(planningSpies.toastError).toHaveBeenCalledWith('MRP 计算失败：上游库存快照不可用。')
    expect(wrapper.find('.cell-status').text()).toContain('MRP 计算失败：上游库存快照不可用。')
  })

  it('轮询超时只提醒去运行列表回看，不按失败处理', async () => {
    const wrapper = mount(PlanningWorkbench)

    planningSpies.activeMrpRun.runId = 'run-async-1'
    planningSpies.activeMrpRun.status = 'polling-timeout'
    await wrapper.vm.$nextTick()

    expect(planningSpies.toastWarning).toHaveBeenCalledWith(
      'MRP 仍在后台计算，可稍后在「MRP 运行」列表查看结果。',
    )
    expect(planningSpies.toastError).not.toHaveBeenCalled()
  })
})
