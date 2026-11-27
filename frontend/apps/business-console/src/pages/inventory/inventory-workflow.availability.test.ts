import {
  UUID_PATTERN,
  inventoryState,
  siteStockState,
  routeState,
  mountInventoryPage,
  resetFixture,
} from './inventory-workflow.fixture'
import { nextTick, ref } from 'vue'
import { beforeEach, describe, expect, it } from 'vitest'
import AvailabilityPage from './availability.vue'
import LotsPage from './lots.vue'

describe('inventory workflow pages', () => {
  beforeEach(resetFixture)

  it('shows current availability without inventing history when quantity changes or is zero', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    const metric = () =>
      wrapper
        .findAllComponents({ name: 'NvMetricCard' })
        .find((card) => card.props('label') === '可用量')!

    for (const quantity of [7, 14, 0]) {
      inventoryState.availabilityQuantity!.value = quantity
      await nextTick()
      expect(metric().props('value')).toBe(String(quantity))
      expect(metric().props('trend')).toBeUndefined()
      expect(metric().props('series')).toBeUndefined()
      expect(wrapper.text()).not.toMatch(/近\s*14\s*日|较\s*14\s*日前/)
    }

    inventoryState.availabilityPending!.value = true
    await nextTick()
    expect(metric().props('value')).toBe('—')
    expect(wrapper.find('[data-page-count]').text()).toBe('加载中')
    inventoryState.availabilityPending!.value = false
    inventoryState.availabilityError!.value = new Error('unavailable')
    await nextTick()
    expect(wrapper.find('[data-page-count]').text()).toBe('读取失败')
    expect(wrapper.find('[data-ui-table]').attributes('data-has-error')).toBe('true')
    expect(metric().props('trend')).toBeUndefined()
    expect(metric().props('series')).toBeUndefined()
  })

  it('shows count-frozen stock outside available and reserved quantities', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    inventoryState.availabilityQuantity!.value = 0
    await nextTick()

    const ring = wrapper.findAllComponents({ name: 'NvMetricRing' }).at(0)!
    expect(ring.props('segments')).toEqual([
      { key: 'available', label: '可用', value: 0, tone: 'success' },
      { key: 'reserved', label: '预留', value: 2, tone: 'warning' },
      { key: 'frozen', label: '冻结/其他', value: 8, tone: 'danger' },
    ])
  })

  it('shows current expiry risk counts without a synthetic period change', () => {
    inventoryState.initialSkuCode = ''
    const wrapper = mountInventoryPage(AvailabilityPage)
    const metric = wrapper
      .findAllComponents({ name: 'NvMetricCard' })
      .find((card) => card.props('label') === '全厂效期风险批次')!

    expect(metric.props('value')).toBe(51)
    expect(metric.props('trend')).toBeUndefined()
    expect(wrapper.text()).toContain('已过期')
    expect(wrapper.text()).toContain('30天内到期')
    expect(wrapper.text()).not.toMatch(/近\s*14\s*日|较\s*14\s*日前/)
  })

  it('availability read-face guard：目录可解析时显示货主名称和工单号', () => {
    routeState.query = { workOrderId: 'WO-2026-08001' }
    inventoryState.availabilityRows = ref([
      {
        locationCode: 'A-01',
        lotNo: 'LOT-001',
        ownerType: 'customer',
        ownerId: '019fbb41-1111-7111-8111-111111111111',
        qualityStatus: 'available',
        availableQuantity: 7,
      },
    ])

    const visibleText = mountInventoryPage(AvailabilityPage).text()
    expect(visibleText).toContain('华东客户')
    expect(visibleText).toContain('WO-2026-08001')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  it('availability read-face guard：货主目录失败和工单为技术标识时显示占位符', () => {
    const technicalId = '019fbb41-2222-7222-8222-222222222222'
    routeState.query = { workOrderId: technicalId }
    inventoryState.partnerResolved = false
    inventoryState.availabilityRows = ref([
      {
        locationCode: 'A-01',
        ownerType: 'customer',
        ownerId: technicalId,
        qualityStatus: 'available',
        availableQuantity: 7,
      },
    ])

    const visibleText = mountInventoryPage(AvailabilityPage).text()
    expect(visibleText).toContain('未解析货主')
    expect(visibleText).toContain('返回工单')
    expect(visibleText).not.toMatch(UUID_PATTERN)
    expect(visibleText).not.toContain('user-emp-')
  })

  describe('全厂库存首屏（不选物料也要看到库存表）', () => {
    it('未选物料时直接渲染全厂库存表，而不是要求先填查询条件', () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(AvailabilityPage)

      const table = wrapper.find('[data-ui-table]')
      expect(table.exists()).toBe(true)
      // 两个物料各一行，说明进页面就有货可看。
      expect(table.findAll('tbody tr')).toHaveLength(2)
      expect(wrapper.text()).toContain('前减振器总成')
      expect(wrapper.text()).toContain('45号钢棒料')
      expect(wrapper.text()).not.toContain('请选择物料')
    })

    it('如实交代扫描覆盖范围并给出继续扫描的出路', async () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(AvailabilityPage)

      expect(wrapper.text()).toContain('已扫描 24/48 个物料')
      const scanMore = wrapper
        .findAll('button')
        .find((button) => button.text().includes('继续扫描'))
      expect(scanMore).toBeDefined()
      await scanMore!.trigger('click')
      expect(siteStockState.scanMore).toHaveBeenCalledTimes(1)
    })

    it('点总览行的物料即下钻到该物料台账，无需回到筛选条重填', async () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(AvailabilityPage)

      const skuCell = wrapper.find('[data-cell="skuCode"] button')
      expect(skuCell.exists()).toBe(true)
      await skuCell.trigger('click')

      expect(inventoryState.availabilityFilters?.skuCode).toBe('SKU-001')
    })

    it('进入明细后提供返回全厂库存的出口，并清掉下钻带上的库位/批次条件', async () => {
      const wrapper = mountInventoryPage(AvailabilityPage)
      inventoryState.availabilityFilters!.locationCode = 'A-01'

      const back = wrapper
        .findAll('button')
        .find((button) => button.text().includes('返回全厂库存'))
      expect(back).toBeDefined()
      await back!.trigger('click')

      expect(inventoryState.availabilityFilters?.skuCode).toBe('')
      expect(inventoryState.availabilityFilters?.locationCode).toBe('')
    })

    it('批次与预留页未选物料时铺全厂批次台账，而不是拦一个选择物料的空态', () => {
      inventoryState.initialSkuCode = ''
      const wrapper = mountInventoryPage(LotsPage)

      const table = wrapper.find('[data-ui-table]')
      expect(table.exists()).toBe(true)
      expect(table.findAll('tbody tr')).toHaveLength(2)
      expect(wrapper.text()).toContain('LOT-001')
      expect(wrapper.text()).toContain('LOT-2026-0714')
      expect(wrapper.text()).not.toContain('选择物料，查看批次')
      expect(wrapper.text()).toContain('已扫描 24/48 个物料')
    })

    it('库位/批次/序列号走库存可搜目录选择器，不再让仓管手输', () => {
      const wrapper = mountInventoryPage(AvailabilityPage)

      const pickers = wrapper
        .findAll('[data-directory-picker]')
        .map((picker) => [picker.attributes('aria-label'), picker.attributes('directory-type')])
      expect(pickers).toContainEqual(['库位', 'location'])
      expect(pickers).toContainEqual(['批次', 'batch'])
      expect(pickers).toContainEqual(['序列号', 'serial'])
      // 这三项过去是自由文本框，改造后不允许再出现同名输入框。
      const inputLabels = wrapper.findAll('input').map((input) => input.attributes('aria-label'))
      expect(inputLabels).not.toContain('库位')
      expect(inputLabels).not.toContain('批次')
      expect(inputLabels).not.toContain('序列号')
    })
  })

  it('uses a business-safe toast instead of exposing raw availability errors', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    const error = new Error('HTTP 500 downstream stack trace')

    inventoryState.availabilityError!.value = error
    await nextTick()

    expect(inventoryState.notifyError).toHaveBeenCalledWith(
      error,
      '库存可用量加载失败，请稍后重试。',
    )
    // 读失败必须走错误态，而不是折进空态文案——一个 500 不能长得跟「真的没有货」一样。
    const table = wrapper.get('[data-ui-table]')
    expect(table.attributes('data-has-error')).toBe('true')
    expect(table.attributes('data-error-message')).toBe(
      '库存可用量读取失败，现在无法判断这个物料有多少货。',
    )
    expect(table.attributes('data-empty-message')).not.toContain('失败')
    expect(wrapper.text()).not.toContain('downstream stack trace')
  })

  it('distinguishes a selected factory from business context still loading', async () => {
    const wrapper = mountInventoryPage(AvailabilityPage)
    inventoryState.expiryFilters!.organizationId = ''

    await wrapper
      .findAll('button')
      .find((button) => button.text().includes('效期预警'))!
      .trigger('click')
    await nextTick()

    expect(wrapper.get('[data-page-count]').text()).toBe('尚未确定当前组织')
    // 「还没查」是中性态，不是空态：不能渲染成「当前范围没有临期批次」那种结论。
    const contextTable = wrapper.get('[data-ui-table]')
    expect(contextTable.attributes('data-awaiting-scope')).toBe('true')
    expect(contextTable.attributes('data-awaiting-scope-message')).toBe(
      '尚未确定当前组织，暂无法查看效期预警批次。',
    )
    expect(contextTable.attributes('data-has-error')).toBe('false')
    expect(wrapper.text()).not.toContain('请选择工厂')
  })
})
