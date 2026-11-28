import {
  inventoryState,
  routeState,
  uiStubs,
  mountInventoryPage,
  openCreateCountTask,
  openCountTaskRow,
  resetFixture,
} from './inventory-workflow.fixture'
import { flushPromises, mount } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import { nextTick } from 'vue'
import { beforeEach, describe, expect, it } from 'vitest'
import CountsPage from './counts.vue'
import MovementsPage from './movements.vue'
import { useBusinessContextStore } from '@/stores/businessContext'

describe('inventory workflow pages', () => {
  beforeEach(resetFixture)

  it('在来源单据下显示真实来源行，缺失时说明未记录', () => {
    inventoryState.movementRows = [
      { movementId: 'mov-1', sourceDocumentId: 'IN-101', sourceDocumentLineId: '20' },
      { movementId: 'mov-2', sourceDocumentId: 'IN-102' },
    ]
    const wrapper = mountInventoryPage(MovementsPage)
    const sourceCells = wrapper.findAll('[data-cell="sourceDocumentId"]').map((cell) => cell.text())
    expect(sourceCells[0]).toContain('IN-101')
    expect(sourceCells[0]).toContain('20')
    expect(sourceCells[1]).toContain('IN-102')
    expect(sourceCells[1]).toContain('未记录来源行')
    wrapper.unmount()
  })

  // #3832 审核 R3-2：库位选择器的工厂只从表单工厂来（候选收窄与就地新增都用它），每页一条轻量断言。
  // 批次 / 序列号不可新增，漏传工厂不会在界面上显形，只能靠这里逐字段钉住（审核 R4-1）。
  it.each([
    ['库存移动 · 库位', MovementsPage, '#movement-site', '#movement-location'],
    ['库存移动 · 批次', MovementsPage, '#movement-site', '#movement-lot'],
    ['库存移动 · 序列号', MovementsPage, '#movement-site', '#movement-serial'],
    ['库存盘点 · 库位', CountsPage, '#count-task-site', '#count-task-location'],
    ['库存盘点 · 批次', CountsPage, '#count-task-site', '#count-task-lot'],
    ['库存盘点 · 序列号', CountsPage, '#count-task-site', '#count-task-serial'],
  ])('%s拿到表单工厂', async (_name, Page, siteSelector, fieldSelector) => {
    const wrapper = mountInventoryPage(Page)

    await wrapper.get(siteSelector).setValue('S1')

    expect(wrapper.get(fieldSelector).attributes('data-form-site')).toBe('S1')
  })

  it('库存移动 · 调拨入库库位拿到表单工厂', async () => {
    const wrapper = mount(MovementsPage, {
      global: {
        plugins: [createPinia()],
        stubs: {
          ...uiStubs,
          // 移动类型要能切到「移库」，入库库位才出现。
          NvSelect: {
            props: ['modelValue'],
            emits: ['update:modelValue'],
            template:
              '<select data-select :value="modelValue" @change="$emit(\'update:modelValue\', $event.target.value)"><slot /></select>',
          },
          NvSelectItem: { props: ['value'], template: '<option :value="value"><slot /></option>' },
          NvSelectTrigger: { template: '<slot />' },
          NvSelectContent: { template: '<slot />' },
          RouterLink: { template: '<a><slot /></a>' },
        },
      },
    })
    const movementType = wrapper
      .findAll('select[data-select]')
      .find((select) => select.find('option[value="transfer"]').exists())!
    await movementType.setValue('transfer')
    await wrapper.get('#movement-site').setValue('S1')

    expect(wrapper.get('#movement-transfer-in-location').attributes('data-form-site')).toBe('S1')
  })

  it('库存移动 · 幂等键只用网关白名单字符，重试沿用、过账成功后换新', async () => {
    // #3922：过去用「|」把表单字段拼成键、中文来源单号原样进键，一提交就被网关 400 拒掉。
    const pinia = createPinia()
    setActivePinia(pinia)
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
    routeState.query = {
      sourceDocumentId: '期初入库-一号仓',
      skuCode: 'SKU-001',
      siteCode: 'S1',
      locationCode: 'A-01',
    }
    inventoryState.postMovement
      .mockRejectedValueOnce(new Error('network'))
      .mockResolvedValueOnce({})
      .mockResolvedValueOnce({})
    const wrapper = mount(MovementsPage, {
      global: {
        plugins: [pinia],
        stubs: { ...uiStubs, RouterLink: { template: '<a><slot /></a>' } },
      },
    })
    await flushPromises()

    for (let attempt = 0; attempt < 3; attempt += 1) {
      await wrapper.get('form').trigger('submit')
      await flushPromises()
    }

    const keys = inventoryState.postMovement.mock.calls.map(([body]) => body.idempotencyKey)
    expect(keys).toHaveLength(3)
    for (const key of keys) expect(key).toMatch(/^[A-Za-z0-9._:/-]+$/)
    expect(keys[1]).toBe(keys[0])
    expect(keys[2]).not.toBe(keys[1])
  })

  it('uses design-system table components for the stock movement read face', () => {
    const wrapper = mountInventoryPage(MovementsPage)

    expect(wrapper.find('[data-ui-table]').exists()).toBe(true)
  })

  /**
   * 盘点与流水表格必须渲染服务端返回的行。
   *
   * 这两页此前只有会话内本地队列，刷新即空；#1194 补了库存域的盘点 / 流水读面之后，
   * 页面必须真的从读面取数——否则补了后端也白补。
   */
  it('renders stock count tasks and stock movements from the server read face', () => {
    inventoryState.countTaskRows = [
      { ...openCountTaskRow('COUNT-TASK-1'), status: 'pending-approval', varianceQuantity: -3 },
    ]
    inventoryState.countAdjustmentRows = [
      {
        adjustmentId: 'ADJ-1',
        countTaskCode: 'CNT-2026-0001',
        status: 'pending-approval',
        approvalChainId: 'APPR-CNT-2026-0001',
      },
    ]
    inventoryState.movementRows = [
      {
        movementId: 'MOVE-1',
        movementType: 'inbound',
        sourceService: 'seed:world-history',
        sourceDocumentId: 'PR-2026-0001',
        skuCode: 'RM-BAR-45-01',
        uomCode: 'kg',
        siteCode: 'S1',
        locationCode: 'WH-WB-RM-01',
        lotNo: 'LOT-PR-2026-0001',
        quantity: 120,
        postedAtUtc: '2026-03-02T09:00:00Z',
      },
    ]

    const counts = mountInventoryPage(CountsPage)
    expect(counts.text()).toContain('CNT-2026-0001')
    expect(counts.text()).toContain('待审批')
    expect(counts.text()).toContain('APPR-CNT-2026-0001')

    const movements = mountInventoryPage(MovementsPage)
    expect(movements.text()).toContain('PR-2026-0001')
    expect(movements.text()).toContain('入库')
    expect(movements.text()).toContain('LOT-PR-2026-0001')
  })

  it('shows maintenance spare-part issues by formal number and hides legacy work-order GUIDs', () => {
    const legacyWorkOrderId = '01a0f250-c298-7901-a442-fd1741691fb5'
    const movementLine = {
      movementType: 'outbound',
      skuCode: 'SPARE-BEARING-01',
      uomCode: 'pcs',
      siteCode: 'SITE-001',
      locationCode: 'loc-spare-01',
      quantity: -2,
      postedAtUtc: '2026-09-30T12:35:00Z',
    }
    inventoryState.movementRows = [
      {
        ...movementLine,
        movementId: 'MOVE-MWO',
        sourceService: 'maintenance',
        sourceDocumentId: 'MWO-20260930-000001',
      },
      {
        ...movementLine,
        movementId: 'MOVE-LEGACY',
        sourceService: 'maintenance',
        sourceDocumentId: legacyWorkOrderId,
      },
    ]

    const text = mountInventoryPage(MovementsPage).text()
    expect(text).toContain('MWO-20260930-000001')
    expect(text).toContain('维修工单')
    expect(text).not.toContain(legacyWorkOrderId)
  })

  it('寄售货主随货主类型联动：客户寄售选客户、供应商寄售选供应商，其余类型不填货主', async () => {
    routeState.query = { skuCode: 'SKU-001', locationCode: 'A-01' }
    inventoryState.createCountTask.mockResolvedValue({ data: { countTaskId: 'COUNT-TASK-NEW' } })
    const wrapper = mountInventoryPage(CountsPage)
    await openCreateCountTask(wrapper)
    const ownerTypeSelect = wrapper
      .findAllComponents(uiStubs.NvSelect)
      .find((select) => select.find('[aria-label="货主类型"]').exists())!
    const chooseOwnerType = async (value: string) => {
      ownerTypeSelect.vm.$emit('update:modelValue', value)
      await nextTick()
    }
    const ownerOptions = () =>
      wrapper.findAll('#count-task-owner-id option').map((option) => option.attributes('value'))

    expect(wrapper.find('#count-task-owner-id').exists()).toBe(false)

    await chooseOwnerType('customer')
    expect(ownerOptions()).toEqual(['C-EAST'])
    await wrapper.find('#count-task-owner-id').setValue('C-EAST')

    // 换成供应商寄售：已选的客户不能带过去当供应商货主。
    await chooseOwnerType('supplier')
    expect(ownerOptions()).toEqual(['V-STEEL'])
    expect((wrapper.find('#count-task-owner-id').element as HTMLSelectElement).value).toBe('')
    await wrapper.find('#count-task-owner-id').setValue('V-STEEL')
    await wrapper.findAll('form')[0]!.trigger('submit')
    expect(inventoryState.createCountTask).toHaveBeenLastCalledWith(
      expect.objectContaining({ ownerType: 'supplier', ownerId: 'V-STEEL' }),
    )

    // 本公司库存没有货主：切回去后货主字段消失，提交也不再带上一轮选的供应商。
    await chooseOwnerType('owned')
    expect(wrapper.find('#count-task-owner-id').exists()).toBe(false)
    await wrapper.findAll('form')[0]!.trigger('submit')
    expect(inventoryState.createCountTask).toHaveBeenLastCalledWith(
      expect.objectContaining({ ownerType: 'owned', ownerId: undefined }),
    )
  })
})
