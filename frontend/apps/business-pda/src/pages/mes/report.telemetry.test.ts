import {
  telemetryCandidatesRef,
  telemetryTargetTasksRef,
  telemetryPromote,
  telemetryDismiss,
  openTelemetryCandidate,
  deviceTasks,
  resetFixture,
} from './report.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import ReportPage from './report.vue'

describe('PDA MES production reporting page', () => {
  beforeEach(resetFixture)

  it('遥测待确认候选显示设备名称（编码）与中文状态，不露设备标识和原始状态码', async () => {
    telemetryCandidatesRef.value = [
      {
        candidateId: 'cand-1',
        deviceAssetId: 'DEV-CNC-01',
        goodQuantity: 12,
        status: 'draft',
        suspensionReason: 'active-alarm',
      },
      {
        candidateId: 'cand-2',
        deviceAssetId: 'DEV-CNC-02',
        goodQuantity: 8,
        status: 'pending-confirmation',
        suspensionReason: null,
      },
    ]
    const wrapper = mount(ReportPage)
    await flushPromises()
    const text = wrapper.text()
    expect(text).toContain('设备 数控车床一号（EQ00001） · 12 件')
    expect(text).toContain('设备存在未处理报警')
    // 主数据查不到名称时用占位，不把设备标识露给用户。
    expect(text).toMatch(/设备 — · 8 件[^设]*待确认/)
    expect(text).not.toContain('DEV-CNC-01')
    expect(text).not.toContain('DEV-CNC-02')
    expect(text).not.toContain('active-alarm')
    expect(text).not.toContain('pending-confirmation')
  })

  it('遥测候选区的输入框获焦时扫码框让出焦点，失焦后恢复扫码', async () => {
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-focus',
      deviceAssetId: 'DEV-CNC-01',
    })
    const listScanner = () => wrapper.getComponent({ name: 'MesScanPrevalidation' })
    expect(listScanner().props('active')).toBe(true)

    const search = wrapper.get('input[placeholder="按工单号搜索执行中的工序"]')
    await search.trigger('focusin')
    expect(listScanner().props('active')).toBe(false)

    await search.trigger('focusout')
    expect(listScanner().props('active')).toBe(true)
  })

  it('遥测候选区的按钮获焦时扫码框不让出焦点，点过按钮仍能直接扫码', async () => {
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-button-focus',
      deviceAssetId: 'DEV-CNC-01',
    })
    const listScanner = () => wrapper.getComponent({ name: 'MesScanPrevalidation' })

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('12 件'))!
      .trigger('focusin')
    expect(listScanner().props('active')).toBe(true)
  })

  it('遥测候选已关联工单工序时直接带出，确认转正不需要输入任何编号', async () => {
    telemetryTargetTasksRef.value = deviceTasks
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-1',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'active-alarm',
      workOrderId: 'WO-2026-0002',
      operationTaskId: 'OP-3',
    })

    expect(wrapper.get('[data-testid="telemetry-target"]').text()).toBe('WO-2026-0002 · 工序 10')
    expect(wrapper.find('input[placeholder*="工单号"]').exists()).toBe(false)
    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()
    expect(telemetryPromote).toHaveBeenCalledWith('cand-1', 'WO-2026-0002', 'OP-3')
  })

  it('转正成功后提示报工单号并收起候选', async () => {
    telemetryTargetTasksRef.value = [deviceTasks[0]]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-ok',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('已转为报工 PRPT-20260927-000001')
    expect(wrapper.find('[data-testid="telemetry-target"]').exists()).toBe(false)
  })

  it('转正被拒时上屏业务原因且保留当前选择', async () => {
    telemetryPromote.mockRejectedValueOnce({
      success: false,
      code: 400,
      message: '工序已完工，不能再报工。',
      errorData: [],
    })
    telemetryTargetTasksRef.value = [deviceTasks[0]]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-reject',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('工序已完工，不能再报工。')
    expect(document.body.textContent).not.toContain('已转为报工')
    expect(wrapper.get('[data-testid="telemetry-target"]').text()).toBe('WO-2026-0001 · 工序 10')
  })

  it('忽略成功后给出提示', async () => {
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-dismiss',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    await wrapper.get('input[placeholder="忽略原因（忽略时必填）"]').setValue('重复采集')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '忽略')!
      .trigger('click')
    await flushPromises()

    expect(telemetryDismiss).toHaveBeenCalledWith('cand-dismiss', '重复采集')
    expect(document.body.textContent).toContain('已忽略这条遥测记录')
  })

  it('候选自带的工序不在设备列表里时仍按候选自带的转正', async () => {
    telemetryTargetTasksRef.value = []
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-linked-only',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'active-alarm',
      workOrderId: 'WO-2026-0009',
      operationTaskId: 'OP-9',
    })

    expect(wrapper.get('[data-testid="telemetry-target"]').text()).toBe('WO-2026-0009')
    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()
    expect(telemetryPromote).toHaveBeenCalledWith('cand-linked-only', 'WO-2026-0009', 'OP-9')
  })

  it('改选后可以取消，回到原来的目标', async () => {
    telemetryTargetTasksRef.value = [deviceTasks[0]]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-cancel',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    await wrapper.get('[data-testid="telemetry-change-target"]').trigger('click')
    expect(wrapper.find('[data-testid="telemetry-target"]').exists()).toBe(false)
    await wrapper.get('input[placeholder="按工单号搜索执行中的工序"]').setValue('WO-2026')
    await wrapper.get('[data-testid="telemetry-cancel-change"]').trigger('click')
    // 取消改选要连同关键词一起清掉，否则「设备上唯一一道」的自动目标回不来（下一行断言）。
    expect(wrapper.get('[data-testid="telemetry-target"]').text()).toBe('WO-2026-0001 · 工序 10')
  })

  it('其它设备上的工序标出「非本设备」，选中后目标上也保留标记', async () => {
    telemetryTargetTasksRef.value = [
      { ...deviceTasks[0], deviceAssetId: 'DEV-CNC-01' },
      { ...deviceTasks[1], deviceAssetId: 'DEV-CNC-99' },
    ]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-other-device',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    const own = wrapper.get('[data-testid="telemetry-target-option-OP-1"]')
    const other = wrapper.get('[data-testid="telemetry-target-option-OP-3"]')
    expect(own.text()).not.toContain('非本设备')
    expect(own.text()).toContain('工序 10 · 数控车床一号（EQ00001）')
    expect(other.text()).toContain('非本设备')
    expect(other.text()).toContain('工序 10 · 数控车床九号（EQ00099）')
    await other.trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="telemetry-target-other-device"]').text()).toBe('非本设备')
    expect(wrapper.text()).toContain('数控车床九号（EQ00099）')
  })

  it('改选期间禁用确认转正，选定新工序后恢复', async () => {
    telemetryTargetTasksRef.value = deviceTasks
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-choosing',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'active-alarm',
      workOrderId: 'WO-2026-0001',
      operationTaskId: 'OP-1',
    })

    expect(wrapper.get('[data-testid="telemetry-promote"]').attributes('disabled')).toBeUndefined()
    await wrapper.get('[data-testid="telemetry-change-target"]').trigger('click')
    expect(wrapper.get('[data-testid="telemetry-promote"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-testid="telemetry-target-option-OP-3"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="telemetry-promote"]').attributes('disabled')).toBeUndefined()
    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()
    expect(telemetryPromote).toHaveBeenCalledWith('cand-choosing', 'WO-2026-0002', 'OP-3')
  })

  it('返工工序在列表行标「返工」', async () => {
    telemetryTargetTasksRef.value = [
      {
        ...deviceTasks[0],
        deviceAssetId: 'DEV-CNC-01',
        workOrderType: 'rework',
        sourceWorkOrderId: 'WO-2026-0000',
        sourceNcrId: 'ncr-1',
        sourceNcrCode: 'NCR-0001',
      },
      { ...deviceTasks[1], deviceAssetId: 'DEV-CNC-01' },
    ]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-rework',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    expect(wrapper.get('[data-testid="telemetry-target-option-OP-1"]').text()).toContain('返工')
    expect(wrapper.get('[data-testid="telemetry-target-option-OP-3"]').text()).not.toContain('返工')
  })

  it('设备名查不到时列表行说「设备信息未提供」，不露设备标识', async () => {
    telemetryTargetTasksRef.value = [
      { ...deviceTasks[0], deviceAssetId: 'DEV-CNC-01' },
      { ...deviceTasks[1], deviceAssetId: 'DEV-UNKNOWN-7' },
    ]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-unknown-device',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    const other = wrapper.get('[data-testid="telemetry-target-option-OP-3"]')
    expect(other.text()).toContain('工序 10 · 设备信息未提供')
    expect(other.text()).toContain('非本设备')
    expect(other.text()).not.toContain('DEV-UNKNOWN-7')
  })

  it('忽略被拒时上屏业务原因且候选保留', async () => {
    telemetryDismiss.mockRejectedValueOnce({
      success: false,
      code: 400,
      message: '该遥测记录已被处理。',
      errorData: [],
    })
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-dismiss-reject',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    await wrapper.get('input[placeholder="忽略原因（忽略时必填）"]').setValue('重复采集')
    await wrapper
      .findAll('button')
      .find((button) => button.text() === '忽略')!
      .trigger('click')
    await flushPromises()

    expect(document.body.textContent).toContain('该遥测记录已被处理。')
    expect(document.body.textContent).not.toContain('已忽略这条遥测记录')
    expect(wrapper.find('input[placeholder="忽略原因（忽略时必填）"]').exists()).toBe(true)
  })

  it('设备上只有一道执行中的工序时直接带出', async () => {
    telemetryTargetTasksRef.value = [deviceTasks[0]]
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-2',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    expect(wrapper.get('[data-testid="telemetry-target"]').text()).toBe('WO-2026-0001 · 工序 10')
    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()
    expect(telemetryPromote).toHaveBeenCalledWith('cand-2', 'WO-2026-0001', 'OP-1')
  })

  it('设备上有多道执行中的工序时点选后才能转正', async () => {
    telemetryTargetTasksRef.value = deviceTasks
    const wrapper = await openTelemetryCandidate({
      candidateId: 'cand-3',
      deviceAssetId: 'DEV-CNC-01',
      suspensionReason: 'no-current-work-order',
    })

    expect(wrapper.find('[data-testid="telemetry-target"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="telemetry-promote"]').attributes('disabled')).toBeDefined()
    await wrapper.get('[data-testid="telemetry-target-option-OP-3"]').trigger('click')
    await flushPromises()
    expect(wrapper.get('[data-testid="telemetry-target"]').text()).toBe('WO-2026-0002 · 工序 10')
    await wrapper.get('[data-testid="telemetry-promote"]').trigger('click')
    await flushPromises()
    expect(telemetryPromote).toHaveBeenCalledWith('cand-3', 'WO-2026-0002', 'OP-3')
  })
})
