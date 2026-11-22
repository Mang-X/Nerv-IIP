import {
  resetQualityLocationFixture,
  routeState,
  notifySpies,
  taskActionSpies,
  qualityState,
  mountQualityPage,
  nextRenderTick,
} from './quality-location.fixture'
import { beforeEach, describe, expect, it } from 'vitest'
import InspectionsPage from './inspections.vue'

describe('quality route location behavior', () => {
  beforeEach(resetQualityLocationFixture)

  // #1326：计量型（variable）特性必须提交数值 measuredValue，缺失时后端会以领域 400 拒绝。
  it('submits measuredValue for variable characteristics and gates submit on a numeric value', async () => {
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'DIM-01',
        name: '长度',
        characteristicType: 'variable',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    taskActionSpies.startInspection.mockResolvedValueOnce({
      data: { inspectionRecordId: 'REC-001' },
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: {
        resultLines: Array<{
          characteristicCode: string
          characteristicType: string
          measuredValue: string
          observedValue: string
        }>
      }
      canCreateRecord: boolean
      submitInspectionRecord: () => Promise<void>
    }

    // 自动带出的计量特性行：未录测量值前禁止提交（不再靠文本 observedValue 假放行）。
    expect(vm.recordForm.resultLines[0]).toMatchObject({
      characteristicCode: 'DIM-01',
      characteristicType: 'variable',
    })
    expect(vm.canCreateRecord).toBe(false)

    vm.recordForm.resultLines[0]!.measuredValue = '10.1'
    await nextRenderTick()
    expect(vm.canCreateRecord).toBe(true)

    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledWith(
      'TASK-001',
      expect.objectContaining({
        resultLines: [
          expect.objectContaining({
            characteristicCode: 'DIM-01',
            measuredValue: 10.1,
            observedValue: '10.1',
            unitCode: 'mm',
          }),
        ],
      }),
    )
  })

  it('keeps attribute characteristics on text observedValue without measuredValue', async () => {
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'APP-01',
        name: '外观',
        characteristicType: 'attribute',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    taskActionSpies.startInspection.mockResolvedValueOnce({
      data: { inspectionRecordId: 'REC-002' },
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ observedValue: string }> }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '外观无划痕'
    await nextRenderTick()

    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledWith(
      'TASK-001',
      expect.objectContaining({
        resultLines: [
          expect.objectContaining({
            characteristicCode: 'APP-01',
            observedValue: '外观无划痕',
            measuredValue: undefined,
          }),
        ],
      }),
    )
  })

  it('routes the backend measured-value rejection through operation failure passthrough', async () => {
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'DIM-01',
        name: '长度',
        characteristicType: 'variable',
        lowerSpecLimit: 9.8,
        upperSpecLimit: 10.2,
        unitCode: 'mm',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    const rejection = {
      success: false,
      statusCode: 400,
      message: '计量型特性“dim-01”需要填写测量值，请录入数值后重新提交。',
    }
    taskActionSpies.startInspection.mockRejectedValueOnce(rejection)

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ measuredValue: string }> }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.measuredValue = '10.1'
    await nextRenderTick()

    await vm.submitInspectionRecord()
    // 台账 #52：领域拒绝理由必须走分层透传（notifyOperationFailure），不能吞成兜底文案。
    expect(notifySpies.operationFailure).toHaveBeenCalledWith(
      '检验记录提交失败',
      rejection,
      '检验记录提交失败，请稍后重试。',
    )
  })

  it('locates a source inspection record: opens read-only record detail from inspectionRecordId', async () => {
    routeState.route!.query = { inspectionRecordId: 'INSP-REC-9' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    // 记录详情真实消费 inspectionRecordId → 展示该记录的判定结论与特性实测值（定位到具体记录，非仅方案）。
    const text = wrapper.text()
    expect(text).toContain('INSP-REC-9')
    expect(text).toContain('rejected')
    expect(text).toContain('DIM-01')
  })

  it('toasts + offers retry (not “未找到”) when the record detail request fails', async () => {
    qualityState.recordError = new Error('403 forbidden')
    routeState.route!.query = { inspectionRecordId: 'INSP-REC-X' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    // 请求失败不再误报为空：走 toast + 可重试，不显示“未找到”。
    expect(notifySpies.error).toHaveBeenCalled()
    expect(wrapper.text()).toContain('检验记录加载失败')
    expect(wrapper.text()).not.toContain('未找到该检验记录')
  })

  // #1396 / 走查 #79：三项实测值填满、无任何红字，「提交检验记录」按钮仍恒为禁用。
  // 两个真因都在这里上锁，并锁住「禁用必有可见理由」这条不变式本身。
  it('accepts the number that a numeric input writes back into measuredValue', async () => {
    // `<input type="number">` 的 v-model 回写的是 number 而不是 string。
    // 旧断言一律喂 '10.1' 这样的字符串，绕开了真实回写类型，于是 `toMeasuredNumber`
    // 里的 `.trim()` 在门禁里永远碰不到 number——测试全绿、真机一录入就哑火。
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'damping-force',
        name: '阻尼力',
        characteristicType: 'variable',
        lowerSpecLimit: 1080,
        upperSpecLimit: 1320,
        unitCode: 'N',
      },
    ]
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'WO-1-OP-60',
      sourceType: 'operation',
      sourceService: 'mes-operation',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      action: 'create',
    }
    taskActionSpies.startInspection.mockResolvedValueOnce({
      data: { inspectionRecordId: 'REC-N-001' },
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ measuredValue: string | number }> }
      canCreateRecord: boolean
      submitBlockers: string[]
      submitInspectionRecord: () => Promise<void>
    }

    vm.recordForm.resultLines[0]!.measuredValue = 1210
    await nextRenderTick()

    expect(vm.submitBlockers).toEqual([])
    expect(vm.canCreateRecord).toBe(true)

    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledWith(
      'TASK-001',
      expect.objectContaining({
        resultLines: [
          expect.objectContaining({
            characteristicCode: 'damping-force',
            measuredValue: 1210,
            observedValue: '1210',
            unitCode: 'N',
          }),
        ],
      }),
    )
  })

  it('never leaves the submit button disabled without a visible reason', async () => {
    // 「无红字」与「可提交」必须同一个真相：禁用⟺清单非空。这条不变式一破，
    // 检验员就只能对着灰按钮干瞪眼——#79 的用户可见形态。
    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: {
        sourceDocumentId: string
        skuCode: string
        resultLines: Array<{ characteristicCode: string; observedValue: string }>
      }
      canCreateRecord: boolean
      submitBlockers: string[]
    }

    expect(vm.canCreateRecord).toBe(false)
    expect(vm.submitBlockers.length).toBeGreaterThan(0)
    // 缺什么就点名什么，而不是一句笼统的「请检查填写项」。
    expect(vm.submitBlockers.join('\n')).toContain('来源单据')

    vm.recordForm.sourceDocumentId = 'WO-1-OP-60'
    vm.recordForm.skuCode = 'SKU-RM-001'
    vm.recordForm.resultLines[0]!.characteristicCode = 'APP-01'
    vm.recordForm.resultLines[0]!.observedValue = '无渗漏'
    await nextRenderTick()

    // 手动流程里弹框根本没有组织/环境输入位，所以业务范围只能取自实时 filters；
    // 旧代码在 setup 时快照了一次空串，这里会永远是 false 且没有任何红字可看。
    expect(vm.submitBlockers).toEqual([])
    expect(vm.canCreateRecord).toBe(true)
  })

  it('derives the characteristic unit from the plan instead of the global UOM catalog', async () => {
    // 走查 #80：单位候选只有 分钟/件/克/千克/升，而特性规格写着「1080–1320 N」。
    // 单位是特性自己的量纲，不是全局计量单位表里随便挑一个。
    qualityState.planCharacteristics = [
      {
        characteristicCode: 'damping-force',
        name: '阻尼力',
        characteristicType: 'variable',
        lowerSpecLimit: 1080,
        upperSpecLimit: 1320,
        unitCode: 'N',
      },
      {
        characteristicCode: 'leakage',
        name: '渗漏检查',
        characteristicType: 'attribute',
        lowerSpecLimit: undefined as unknown as number,
        upperSpecLimit: undefined as unknown as number,
        unitCode: undefined as unknown as string,
      },
    ]
    routeState.route!.query = { inspectionPlanId: 'PLAN-001' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ characteristicCode: string; unitCode: string }> }
      onCharacteristicCodeChange: (line: unknown, value: string) => void
      unitPolicyFor: (line: { characteristicCode: string }) => { mode: string; unitCode?: string }
    }

    const line = vm.recordForm.resultLines[0]!
    vm.onCharacteristicCodeChange(line, 'damping-force')
    await nextRenderTick()
    expect(line.unitCode).toBe('N')
    expect(vm.unitPolicyFor(line)).toEqual({ mode: 'derived', unitCode: 'N' })

    // 换成无量纲的计数型特性时，上一条特性的单位不许留在行上跟着提交。
    vm.onCharacteristicCodeChange(line, 'leakage')
    await nextRenderTick()
    expect(line.unitCode).toBe('')
    expect(vm.unitPolicyFor(line)).toEqual({ mode: 'dimensionless' })
  })
})
