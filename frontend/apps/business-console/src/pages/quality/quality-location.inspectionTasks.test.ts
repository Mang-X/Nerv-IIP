import {
  resetQualityLocationFixture,
  routeState,
  notifySpies,
  taskActionSpies,
  createRecordSpy,
  qualityTaskRead,
  routerSpies,
  qualityState,
  mountQualityPage,
  nextRenderTick,
} from './quality-location.fixture'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import InspectionsPage from './inspections.vue'

describe('quality route location behavior', () => {
  beforeEach(resetQualityLocationFixture)

  it('keeps the user-selected inspection status filter when inspectionPlanId is removed from the route', async () => {
    routeState.route!.query = { inspectionPlanId: 'PLAN-001' }
    mountQualityPage(InspectionsPage)

    qualityState.inspectionFilters!.status = 'active'
    routeState.route!.query = {}
    await nextRenderTick()

    expect(qualityState.inspectionFilters!.keyword).toBeUndefined()
    expect(qualityState.inspectionFilters!.status).toBe('active')
  })

  it('lets the user independently filter first-article inspection plans', async () => {
    const wrapper = mountQualityPage(InspectionsPage)

    await wrapper.get('select[aria-label="检验类别"]').setValue('first-article')

    expect(qualityState.inspectionFilters!.category).toBe('first-article')
  })

  it('offers only inspection-plan categories accepted by the Quality domain', () => {
    const wrapper = mountQualityPage(InspectionsPage)
    const categoryOptions = wrapper
      .get('select[aria-label="检验类别"] + [data-select-options]')
      .findAll('[data-select-item]')
      .map((option) => ({ value: option.attributes('data-value'), label: option.text() }))

    expect(categoryOptions).toEqual([
      { value: 'all', label: '全部类别' },
      { value: 'receiving', label: '来料检' },
      { value: 'operation', label: '工序检' },
      { value: 'final', label: '终检' },
      { value: 'first-article', label: '首件检验' },
      { value: 'maintenance', label: '维修检' },
      { value: 'customer-return', label: '客户退货检' },
    ])
  })

  it('renders every accepted inspection-plan category as a business label in table summaries', () => {
    qualityState.inspectionPlans = [
      {
        id: 'PLAN-MAINTENANCE',
        code: 'IQP-MAINTENANCE',
        category: 'maintenance',
        skuCode: 'SKU-001',
        status: 'active',
      },
      {
        id: 'PLAN-RETURN',
        code: 'IQP-RETURN',
        category: 'customer-return',
        skuCode: 'SKU-002',
        status: 'active',
      },
    ]

    const wrapper = mountQualityPage(InspectionsPage)
    const summaries = wrapper.findAll('[data-plan-summary]').map((cell) => cell.text())

    expect(summaries).toEqual(['维修检 / SKU-001', '客户退货检 / SKU-002'])
    expect(wrapper.text()).not.toContain('maintenance')
    expect(wrapper.text()).not.toContain('customer-return')
  })

  it('does not open the inspection record dialog for a plain inspectionPlanId location route', async () => {
    routeState.route!.query = { inspectionPlanId: 'PLAN-001' }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    expect(wrapper.find('[data-dialog]').exists()).toBe(false)
    expect(qualityState.inspectionFilters!.keyword).toBe('PLAN-001')
  })

  it('prefills the existing record flow from the stable inspection task query contract', async () => {
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

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    const form = (
      wrapper.vm as unknown as {
        recordForm: {
          sourceDocumentId: string
          skuCode: string
          inspectedQuantity: string
          resultLines: Array<{
            characteristicCode: string
            specification: string
            unitCode: string
          }>
        }
      }
    ).recordForm
    expect(form.sourceDocumentId).toBe('GR-001')
    expect(form.skuCode).toBe('SKU-RM-001')
    expect(form.inspectedQuantity).toBe('12')
    expect(form.resultLines).toEqual([
      expect.objectContaining({
        characteristicCode: 'DIM-01',
        specification: '9.8–10.2 mm',
        unitCode: 'mm',
      }),
    ])
  })

  it('uses the first-article source contract when creating a record from a first-article plan', async () => {
    const firstArticlePlan = {
      id: 'PLAN-FA-001',
      code: 'FA-PLAN-001',
      category: 'first-article',
      skuCode: 'SKU-FA-001',
      status: 'active',
    }
    qualityState.inspectionPlans = [firstArticlePlan]
    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    const vm = wrapper.vm as unknown as {
      recordForm: {
        inspectionPlanId: string
        sourceType: string
        sourceService: string
        skuCode: string
      }
      useInspectionPlan: (plan: typeof firstArticlePlan) => void
    }
    vm.useInspectionPlan(firstArticlePlan)

    expect(vm.recordForm).toEqual(
      expect.objectContaining({
        inspectionPlanId: 'PLAN-FA-001',
        sourceType: 'first-article',
        sourceService: 'mes-operation',
        skuCode: 'SKU-FA-001',
      }),
    )
  })

  it('accepts whole-number quantities prefilled by an inspection task', async () => {
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '1200',
      action: 'create',
    }

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()

    // 待检任务流：来源单据 / 物料 / 检验数量全部由任务带出，只读呈现而非输入框。
    const carried = wrapper.get('[data-slot="carried-context"]')
    expect(carried.text()).toContain('1200')
    expect(carried.text()).toContain('SKU-RM-001')
    expect(carried.text()).toContain('GR-001')
    expect(wrapper.find('#record-quantity').exists()).toBe(false)
  })

  it('enables task submission after business context arrives asynchronously', async () => {
    qualityState.inspectionContextInitiallyEmpty = true
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

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ observedValue: string }> }
      canCreateRecord: boolean
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await nextRenderTick()

    expect(vm.canCreateRecord).toBe(false)
    await vm.submitInspectionRecord()
    expect(notifySpies.error).toHaveBeenCalledWith('业务范围尚未就绪，请稍后重试。')
    expect(taskActionSpies.startInspection).not.toHaveBeenCalled()

    qualityState.inspectionFilters!.organizationId = 'org-001'
    qualityState.inspectionFilters!.environmentId = 'env-dev'
    await nextRenderTick()

    expect(vm.canCreateRecord).toBe(true)
    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).toHaveBeenCalledOnce()
    expect(taskActionSpies.startInspection.mock.calls[0]?.[1]).not.toHaveProperty('inspectorUserId')
  })

  it('keeps the existing task submit action disabled when backend allowedActions only permits claim', async () => {
    qualityTaskRead.mockResolvedValueOnce({
      data: {
        success: true,
        data: {
          items: [
            {
              inspectionTaskId: 'TASK-001',
              status: 'pending',
              allowedActions: ['claim'],
            },
          ],
          total: 1,
        },
      },
    })
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

    const wrapper = mountQualityPage(InspectionsPage)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ observedValue: string }> }
      canCreateRecord: boolean
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await nextRenderTick()

    expect(vm.canCreateRecord).toBe(false)
    await vm.submitInspectionRecord()
    expect(taskActionSpies.startInspection).not.toHaveBeenCalled()
  })

  it('preserves inspector input when plan characteristics arrive asynchronously', async () => {
    qualityState.planCharacteristics = []
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

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const form = (
      wrapper.vm as unknown as {
        recordForm: {
          resultLines: Array<{ characteristicCode: string; observedValue: string }>
        }
      }
    ).recordForm
    form.resultLines[0]!.characteristicCode = 'MANUAL-01'
    form.resultLines[0]!.observedValue = '10.1'
    qualityState.planCharacteristicsRef!.value = [
      { characteristicCode: 'DIM-01', lowerSpecLimit: 9.8, upperSpecLimit: 10.2 },
    ]
    await nextRenderTick()

    expect(form.resultLines).toEqual([
      expect.objectContaining({ characteristicCode: 'MANUAL-01', observedValue: '10.1' }),
    ])
  })

  it('clears the routed inspection task and stale form context after a lifecycle conflict', async () => {
    const { LifecycleStateChangedError } = await import('@/composables/shared/lifecycleAction')
    routeState.route!.query = {
      inspectionTaskId: 'TASK-001',
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
      sourceType: 'receiving',
      sourceService: 'wms',
      skuCode: 'SKU-RM-001',
      quantity: '12',
      batchNo: 'LOT-7',
      action: 'create',
    }
    taskActionSpies.startInspection.mockRejectedValueOnce(
      new LifecycleStateChangedError('conflict'),
    )

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordSheetOpen: boolean
      recordForm: {
        inspectionPlanId: string
        sourceDocumentId: string
        skuCode: string
        batchNo: string
        resultLines: Array<{ observedValue: string }>
      }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await vm.submitInspectionRecord()
    await nextRenderTick()

    expect(routerSpies.replace).toHaveBeenCalledWith({ query: {} })
    expect(routeState.route!.query.inspectionTaskId).toBeUndefined()
    expect(vm.recordSheetOpen).toBe(false)
    expect(vm.recordForm).toMatchObject({
      inspectionPlanId: '',
      sourceDocumentId: '',
      skuCode: '',
      batchNo: '',
    })
    expect(vm.recordForm.resultLines).toEqual([
      expect.objectContaining({ characteristicCode: '', observedValue: '' }),
    ])
    expect(taskActionSpies.refreshInspectionTasks).toHaveBeenCalledOnce()
    expect(notifySpies.error).toHaveBeenCalledWith('状态已被其他操作更新')
  })

  it('preserves the routed inspection task and form input after an ordinary validation error', async () => {
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
    taskActionSpies.startInspection.mockRejectedValueOnce({
      success: false,
      statusCode: 422,
      message: '实测值不符合格式',
    })

    const wrapper = mountQualityPage(InspectionsPage)
    await nextRenderTick()
    const vm = wrapper.vm as unknown as {
      recordSheetOpen: boolean
      recordForm: {
        inspectionPlanId: string
        sourceDocumentId: string
        resultLines: Array<{ observedValue: string }>
      }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await vm.submitInspectionRecord()
    await nextRenderTick()

    expect(routerSpies.replace).not.toHaveBeenCalled()
    expect(routeState.route!.query.inspectionTaskId).toBe('TASK-001')
    expect(vm.recordSheetOpen).toBe(true)
    expect(vm.recordForm).toMatchObject({
      inspectionPlanId: 'PLAN-001',
      sourceDocumentId: 'GR-001',
    })
    expect(vm.recordForm.resultLines[0]!.observedValue).toBe('10.1')
    expect(taskActionSpies.refreshInspectionTasks).not.toHaveBeenCalled()
  })

  it.each([true, false])(
    'uses explicit source document numbers in the %s task submission flow',
    async (taskFlow) => {
      routeState.route!.query = {
        ...(taskFlow ? { inspectionTaskId: 'TASK-001' } : {}),
        inspectionPlanId: 'PLAN-001',
        sourceDocumentNo: 'GR-20261011-000008',
        sourceType: 'receiving',
        sourceService: 'wms',
        skuCode: 'SKU-RM-001',
        quantity: '12',
        action: 'create',
      }
      const submit = taskFlow ? taskActionSpies.startInspection : createRecordSpy
      submit.mockResolvedValueOnce({
        data: { inspectionRecordId: '01a0f250-1234-7234-8234-123456789abc' },
      })
      const wrapper = mountQualityPage(InspectionsPage)
      await nextRenderTick()
      const vm = wrapper.vm as unknown as {
        recordForm: { resultLines: Array<{ observedValue: string }> }
        recordSheetOpen: boolean
        submitInspectionRecord: () => Promise<void>
      }
      vm.recordForm.resultLines[0]!.observedValue = '10.1'
      await nextRenderTick()
      await vm.submitInspectionRecord()
      expect(notifySpies.success).toHaveBeenCalledWith(
        `来源单据 GR-20261011-000008 的检验记录已提交${taskFlow ? '，待检任务已闭合。' : '。'}`,
      )
      expect(vm.recordSheetOpen).toBe(false)
      if (taskFlow)
        expect(routerSpies.push).toHaveBeenCalledWith({ path: '/quality/inspection-tasks' })
      else
        expect(createRecordSpy).toHaveBeenCalledWith(
          expect.objectContaining({
            sourceDocumentId: 'GR-20261011-000008',
            inspectedQuantity: 12,
          }),
        )
    },
  )

  it.each([
    [true, '01a0f250-1234-7234-8234-123456789abc'],
    [false, '01a0f250-1234-7234-8234-123456789abc'],
    [true, undefined],
    [false, undefined],
  ])(
    'does not use internal identifiers when the %s task flow has no business number (%s)',
    async (taskFlow, recordId) => {
      routeState.route!.query = {
        ...(taskFlow ? { inspectionTaskId: 'TASK-001' } : {}),
        inspectionPlanId: 'PLAN-001',
        sourceDocumentId: 'source-internal-001',
        sourceType: 'receiving',
        sourceService: 'wms',
        skuCode: 'SKU-RM-001',
        quantity: '12',
        action: 'create',
      }
      const submit = taskFlow ? taskActionSpies.startInspection : createRecordSpy
      submit.mockResolvedValueOnce({ data: { inspectionRecordId: recordId } })
      const wrapper = mountQualityPage(InspectionsPage)
      await nextRenderTick()
      const vm = wrapper.vm as unknown as {
        recordForm: { resultLines: Array<{ observedValue: string }> }
        submitInspectionRecord: () => Promise<void>
      }
      vm.recordForm.resultLines[0]!.observedValue = '10.1'
      await nextRenderTick()
      await vm.submitInspectionRecord()
      expect(notifySpies.success).toHaveBeenCalledWith(
        `检验记录已提交${taskFlow ? '，待检任务已闭合。' : '。'}`,
      )
    },
  )
})
