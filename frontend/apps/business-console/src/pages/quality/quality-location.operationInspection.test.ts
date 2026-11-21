import {
  resetQualityLocationFixture,
  routeState,
  authState,
  notifySpies,
  createRecordSpy,
  routerSpies,
  operationContextState,
  qualityState,
  mountQualityPage,
} from './quality-location.fixture'
import { flushPromises } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import InspectionsPage from './inspections.vue'
import NcrsPage from './ncrs.vue'

describe('quality route location behavior', () => {
  beforeEach(resetQualityLocationFixture)

  it('uses the work-order product for operation inspection and submits its real source', async () => {
    routeState.route!.query = { workOrderId: 'WO-ROD-02', operationTaskId: 'OP-ROD-10' }
    const wrapper = mountQualityPage(InspectionsPage)
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      recordForm: {
        skuCode: string
        resultLines: Array<{ characteristicCode: string; observedValue: string }>
      }
      submitInspectionRecord: () => Promise<void>
    }
    expect(wrapper.text()).toContain('正在加载工序检验对象')
    expect(vm.recordForm.skuCode).toBe('')
    vm.recordForm.resultLines[0]!.characteristicCode = 'DIM-01'
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await vm.submitInspectionRecord()
    expect(createRecordSpy).not.toHaveBeenCalled()

    operationContextState.detail!.value = {
      workOrderId: 'WO-ROD-02',
      skuId: 'SF-ROD-01',
      operationTasks: [
        { operationTaskId: 'OP-ROD-10', operationSequence: 10, operationCode: 'OP10' },
      ],
    }
    operationContextState.pending!.value = false
    await flushPromises()
    expect(wrapper.text()).toContain('SF-ROD-01')
    expect(wrapper.text()).toContain('OP10')
    expect(vm.recordForm.skuCode).toBe('SF-ROD-01')
    await vm.submitInspectionRecord()
    expect(createRecordSpy).toHaveBeenCalledWith(
      expect.objectContaining({
        sourceType: 'operation',
        sourceService: 'mes-operation',
        sourceDocumentId: 'WO-ROD-02',
        skuCode: 'SF-ROD-01',
      }),
    )
    wrapper.unmount()
  })

  // #4421：批准的同 WO/OP 接力，不创建领域关联或自动登记不良。
  async function submittedOperationPage(
    permissions = [
      'business.mes.quality.read',
      'business.mes.quality.write',
      'business.mes.operations.read',
    ],
  ) {
    authState.permissionCodes.push(...permissions)
    routeState.route!.query = { workOrderId: 'WO-ROD-02', operationTaskId: 'OP-ROD-10' }
    const wrapper = mountQualityPage(InspectionsPage)
    operationContextState.detail!.value = {
      workOrderId: 'WO-ROD-02',
      skuId: 'SF-ROD-01',
      operationTasks: [{ operationTaskId: 'OP-ROD-10', operationSequence: 10 }],
    }
    operationContextState.pending!.value = false
    await flushPromises()
    const vm = wrapper.vm as unknown as {
      recordForm: { resultLines: Array<{ characteristicCode: string; observedValue: string }> }
      submitInspectionRecord: () => Promise<void>
    }
    vm.recordForm.resultLines[0]!.characteristicCode = 'DIM-01'
    vm.recordForm.resultLines[0]!.observedValue = '10.1'
    await flushPromises()
    return { wrapper, vm }
  }
  function sourceLinks(wrapper: ReturnType<typeof mountQualityPage>) {
    return wrapper.findAllComponents({ name: 'RouterLink' })
  }

  it('offers same-operation defect registration only after successful inspection submission', async () => {
    const { wrapper, vm } = await submittedOperationPage()
    expect(wrapper.text()).not.toContain('登记同工序不良')
    await vm.submitInspectionRecord()
    await flushPromises()
    const link = sourceLinks(wrapper).find((item) => item.text() === '登记同工序不良')!
    expect(link).toBeDefined()
    expect(link.props('to')).toEqual({
      path: '/mes/quality',
      query: { workOrderId: 'WO-ROD-02', operationTaskId: 'OP-ROD-10' },
    })
    expect(createRecordSpy).toHaveBeenCalledOnce()
    expect(routerSpies.push).not.toHaveBeenCalled()
    const back = sourceLinks(wrapper).find((item) => item.text().startsWith('返回工单'))!
    expect(back.props('to')).toEqual({
      path: '/mes/work-orders/WO-ROD-02',
      query: { operationTaskId: 'OP-ROD-10' },
    })
  })

  it.each([
    { permissions: ['business.mes.operations.read'] },
    { permissions: ['business.mes.quality.read', 'business.mes.operations.read'] },
    { permissions: ['business.mes.quality.write', 'business.mes.operations.read'] },
    { permissions: ['business.mes.quality.read', 'business.mes.quality.write'] },
  ])(
    'hides submitted defect continuation without all target permissions: %j',
    async ({ permissions }) => {
      const { wrapper, vm } = await submittedOperationPage(permissions)
      await vm.submitInspectionRecord()
      await flushPromises()
      expect(createRecordSpy).toHaveBeenCalledOnce()
      expect(wrapper.text()).not.toContain('登记同工序不良')
    },
  )

  it('does not offer defect continuation after inspection submission fails', async () => {
    const { wrapper, vm } = await submittedOperationPage()
    createRecordSpy.mockRejectedValueOnce(new Error('检验拒绝'))
    await vm.submitInspectionRecord()
    await flushPromises()
    expect(notifySpies.operationFailure).toHaveBeenCalledOnce()
    expect(wrapper.text()).not.toContain('登记同工序不良')
  })

  it('drops submitted continuation when the source work order changes', async () => {
    const { wrapper, vm } = await submittedOperationPage()
    await vm.submitInspectionRecord()
    await flushPromises()
    expect(wrapper.text()).toContain('登记同工序不良')
    routeState.route!.query = { workOrderId: 'WO-OTHER' }
    await flushPromises()
    expect(wrapper.text()).not.toContain('登记同工序不良')
    const back = sourceLinks(wrapper).find((item) => item.text().startsWith('返回工单'))!
    expect(back.props('to')).toEqual({
      path: '/mes/work-orders/WO-OTHER',
      query: { operationTaskId: undefined },
    })
  })

  it('never returns or continues an operation absent from the source work order', async () => {
    const { wrapper, vm } = await submittedOperationPage()
    operationContextState.detail!.value = {
      workOrderId: 'WO-ROD-02',
      skuId: 'SF-ROD-01',
      operationTasks: [{ operationTaskId: 'OP-OTHER' }],
    }
    await flushPromises()
    await vm.submitInspectionRecord()
    expect(createRecordSpy).not.toHaveBeenCalled()
    expect(wrapper.text()).not.toContain('登记同工序不良')
    const back = sourceLinks(wrapper).find((item) => item.text().startsWith('返回工单'))!
    expect(back.props('to')).toEqual({
      path: '/mes/work-orders/WO-ROD-02',
      query: { operationTaskId: undefined },
    })
  })

  it('does not restore old continuation when the source changes during submission', async () => {
    const { wrapper, vm } = await submittedOperationPage()
    let finish!: () => void
    createRecordSpy.mockImplementationOnce(
      () =>
        new Promise<void>((resolve) => {
          finish = resolve
        }),
    )
    const submission = vm.submitInspectionRecord()
    routeState.route!.query = { workOrderId: 'WO-OTHER' }
    await flushPromises()
    finish()
    await submission
    await flushPromises()
    expect(createRecordSpy).toHaveBeenCalledOnce()
    expect(wrapper.text()).not.toContain('登记同工序不良')
  })

  it.each<Record<string, string>>([{}, { workOrderId: 'WO-ROD-02' }])(
    'keeps normal inspection entry without inventing an operation: %j',
    async (query) => {
      routeState.route!.query = query
      const wrapper = mountQualityPage(InspectionsPage)
      await flushPromises()
      expect(wrapper.text()).toContain('创建检验记录')
      expect(wrapper.text()).not.toContain('登记同工序不良')
      if (query.workOrderId) {
        const back = sourceLinks(wrapper).find((item) => item.text().startsWith('返回工单'))!
        expect(back.props('to')).toEqual({
          path: '/mes/work-orders/WO-ROD-02',
          query: { operationTaskId: undefined },
        })
      }
    },
  )

  it('blocks a failed operation context and offers retry without a default product', async () => {
    routeState.route!.query = { workOrderId: 'WO-ROD-02', operationTaskId: 'OP-ROD-10' }
    const wrapper = mountQualityPage(InspectionsPage)
    await flushPromises()
    operationContextState.pending!.value = false
    operationContextState.error!.value = '工单读取失败'
    await flushPromises()
    expect(wrapper.text()).toContain('工单读取失败')
    const retry = wrapper.findAll('button').find((button) => button.text() === '重试检验对象')!
    expect(retry).toBeDefined()
    await retry.trigger('click')
    expect(operationContextState.refresh).toHaveBeenCalled()
    expect((wrapper.vm as unknown as { recordForm: { skuCode: string } }).recordForm.skuCode).toBe(
      '',
    )
    wrapper.unmount()
  })

  it.each([
    ['SKU-FG-1000', 'SKU-FG-1000'],
    ['MES-SKU-UNRESOLVED', '未解析物料'],
  ])(
    'shows %s consistently in the NCR list and located disposition detail',
    async (skuCode, label) => {
      qualityState.ncrs = [
        {
          id: 'NCR-MES-001',
          code: 'NCR-MES-001',
          sourceType: 'in-process',
          sourceDocumentId: 'DEF-001',
          skuCode,
          defectQuantity: 2,
          defectReason: 'SURFACE',
          status: 'open',
          reworkWorkOrderCreationStatus: 'not-requested',
        },
      ]
      routeState.route!.query = {
        ncrId: 'NCR-MES-001',
        workOrderId: 'WO-001',
        operationTaskId: 'OP-10',
      }
      const wrapper = mountQualityPage(NcrsPage)
      await flushPromises()

      expect(wrapper.get('[data-plan-summary]').text()).toContain(label)
      expect(wrapper.get('[data-plan-summary]').text()).toContain('DEF-001')
      const context = wrapper.get('section[aria-label="不合格品信息"]')
      const material = context.findAll('dl > div').find((item) => item.get('dt').text() === '物料')!
      expect(material.get('dd').text()).toBe(label)
      expect(context.text()).toContain('DEF-001')
      expect(context.text()).not.toContain('WO-001')
      expect(context.text()).not.toContain('OP-10')
      expect(wrapper.text()).not.toContain('MES-SKU-UNRESOLVED')
      wrapper.unmount()
    },
  )
})
