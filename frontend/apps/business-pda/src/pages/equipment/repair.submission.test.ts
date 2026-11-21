import {
  push,
  route,
  createWorkOrder,
  canReadWorkOrderDetail,
  confirmedCreatedWorkOrder,
  createdDetailHasSuccessfulResponse,
  createdDeviceHasFailedResponse,
  refreshCreatedWorkOrder,
  selectPriority,
  selectReason,
  asThrownByGeneratedClient,
  resetFixture,
} from './repair.fixture'
import { OfflineError, RequestTimeoutError } from '@/api/request-timeout'
import { flushPromises, mount } from '@vue/test-utils'
import { beforeEach, describe, expect, it } from 'vitest'
import RepairPage from './repair.vue'

describe('PDA equipment repair page', () => {
  beforeEach(resetFixture)

  it('submits a new repair with an operation key but WITHOUT org/env/openedBy', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '主轴异响')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(1)
    const body = createWorkOrder.mock.calls[0][0]
    expect(body).toMatchObject({
      deviceAssetId: 'DEV-9',
      priority: 'high',
      assetUnavailableReasonCode: 'Spindle-Noise',
    })
    expect(body.idempotencyKey).toBeTruthy()
    expect(body).not.toHaveProperty('organizationId')
    expect(body).not.toHaveProperty('environmentId')
    expect(body).not.toHaveProperty('openedBy')
  })

  it('shows a success Result after a successful submit', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    const result = wrapper.find('[data-result][data-status="success"]')
    expect(result.exists()).toBe(true)
    expect(wrapper.text()).toContain('报修已提交')
    expect(wrapper.get('[data-testid="created-work-order-assignment-state"]').text()).toContain(
      '尚未确认工单指派给当前账号',
    )
    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    expect(wrapper.get('[data-testid="recheck-created-work-order-assignment"]').text()).toContain(
      '重新核验指派状态',
    )
  })

  it('fails closed when payload and receipt repeat the same non-GUID identifier', async () => {
    createWorkOrder.mockResolvedValueOnce({
      success: true,
      data: {
        workOrderId: 'WO-INVALID',
        operationReceipt: { resourceId: 'WO-INVALID' },
      },
    })
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="success"]').exists()).toBe(false)
    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="recheck-created-work-order-assignment"]').exists()).toBe(
      false,
    )
  })

  it('keeps confirmed assignment visible when device enrichment fails', async () => {
    route.query = { deviceAssetId: 'DEV-ROUTE-1' }
    confirmedCreatedWorkOrder.value = {
      workOrderId: '33333333-3333-3333-3333-333333333333',
    }
    createdDetailHasSuccessfulResponse.value = true
    createdDeviceHasFailedResponse.value = true
    const wrapper = mount(RepairPage, { attachTo: document.body })

    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.get('[data-testid="created-work-order-assignment-state"]').text()).toContain(
      '已确认工单指派给当前维修人员',
    )
    expect(wrapper.get('[data-testid="created-work-order-device-state"]').text()).toContain(
      '设备资料暂不可用',
    )
  })

  it('rechecks authoritative assignment before opening alarm-sourced repair detail', async () => {
    route.query = { deviceAssetId: 'DEV-9', sourceAlarmId: 'ALM-9' }
    refreshCreatedWorkOrder.mockImplementationOnce(async () => {
      confirmedCreatedWorkOrder.value = {
        workOrderId: '33333333-3333-3333-3333-333333333333',
        assignedTechnicianUserId: 'principal-1',
        sourceAlarmId: 'ALM-9',
      }
      createdDetailHasSuccessfulResponse.value = true
    })
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    await wrapper.get('[data-testid="recheck-created-work-order-assignment"]').trigger('click')
    await flushPromises()

    expect(refreshCreatedWorkOrder).toHaveBeenCalledTimes(1)
    expect(wrapper.get('[data-testid="created-work-order-assignment-state"]').text()).toContain(
      '已确认工单指派给当前维修人员',
    )
    await wrapper.get('[data-testid="view-created-work-order"]').trigger('click')

    expect(push).toHaveBeenCalledWith({
      path: '/equipment/work-orders/33333333-3333-3333-3333-333333333333',
      query: { sourceAlarmId: 'ALM-9' },
    })
  })

  it('does not offer a detail link until the self detail is authoritatively confirmed', async () => {
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="recheck-created-work-order-assignment"]').exists()).toBe(
      true,
    )
  })

  it('does not offer detail navigation without both maintenance and device location reads', async () => {
    canReadWorkOrderDetail.value = false
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-testid="view-created-work-order"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="recheck-created-work-order-assignment"]').exists()).toBe(
      false,
    )
    canReadWorkOrderDetail.value = true
  })

  it('shows an error Result with retry when submit fails', async () => {
    createWorkOrder.mockRejectedValueOnce(new Error('网络错误'))
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
  })

  it('结果未知时锁定原 payload 并用相同 idempotencyKey 安全重试', async () => {
    createWorkOrder.mockRejectedValueOnce(new RequestTimeoutError())
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '主轴异响')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('网络超时，请检查连接后重试')
    expect(wrapper.text()).toContain('可直接重试，不会重复')
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
    const firstPayload = createWorkOrder.mock.calls[0][0]

    await wrapper.get('[data-testid="retry"]').trigger('click')
    route.query = { deviceAssetId: 'DEV-ROUTE-CHANGED', sourceAlarmId: 'ALM-CHANGED' }
    await wrapper.vm.$nextTick()
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-CHANGED')
    await scanInput.trigger('keydown.enter')
    await wrapper.get('[data-testid="priority-trigger"]').trigger('click')
    // 意图锁定期间原因入口整体失活：点了也打不开抽屉，所以原因码根本无从篡改。
    await wrapper.get('[data-testid="reason-trigger"]').trigger('click')
    await flushPromises()
    expect(document.body.querySelector('[data-testid="reason-option-Hyd-Leak_01"]')).toBeNull()
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(2)
    expect(createWorkOrder.mock.calls[1][0]).toEqual(firstPayload)
  })

  it('确定业务失败后编辑设备、优先级、原因会形成新意图并旋转 idempotencyKey', async () => {
    createWorkOrder.mockRejectedValueOnce({ success: false, message: '设备不存在' })
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('设备不存在')
    // 服务端已明确失败、无副作用 → 给"重试"，不给"核实"入口。
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
    expect(wrapper.find('[data-testid="retry"]').exists()).toBe(true)
    const firstKey = createWorkOrder.mock.calls[0][0].idempotencyKey

    await wrapper.get('[data-testid="retry"]').trigger('click')
    const scanInput = wrapper.find('input[placeholder*="扫描"]')
    await scanInput.setValue('DEV-10')
    await scanInput.trigger('keydown.enter')
    await selectPriority(wrapper, '中')
    await selectReason(wrapper, '主轴异响')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(createWorkOrder).toHaveBeenCalledTimes(2)
    expect(createWorkOrder.mock.calls[1][0]).toMatchObject({
      deviceAssetId: 'DEV-10',
      priority: 'medium',
      assetUnavailableReasonCode: 'Spindle-Noise',
    })
    expect(createWorkOrder.mock.calls[1][0].idempotencyKey).not.toBe(firstKey)
  })

  // 离线预检在请求发出前抛出 → 服务端从未收到 → 安全重试（不逼用户绕路核实，#814 离线可操作）。
  it('离线（请求未发出）时给安全重试而非核实', async () => {
    createWorkOrder.mockRejectedValueOnce(new OfflineError())
    route.query = { deviceAssetId: 'DEV-9' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    expect(wrapper.text()).toContain('当前离线，请检查网络连接后重试')
    expect(wrapper.find('[data-testid="retry"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
  })
  // #3333：把字符串追到屏幕（PDA 侧那一格）。
  //
  // 喂进去的是**网关校验失败实际写出的整个响应体**（逐字节取自后端
  // `BusinessGatewayValidationFailureEnvelopeTests` 覆盖的同一条通道的实跑输出），
  // 按 generated client 失败时的形态构造（`JSON.parse` 后的响应体被直接 throw，
  // error 拦截器再把原始 `Response` 以非枚举属性挂上去）。
  // 断言落在**渲染出来的 DOM 文本**上——这是操作工真正看到的那一块。
  it.each([
    [
      '新形状（稳定码信封）',
      '{"success":false,"message":"request-payload-invalid","code":400,"errorData":' +
        '[{"name":"idempotencyKey","reason":"\'idempotency Key\' 必须小于或等于128个字符。您输入了129个字符。"}]}',
      '提交的内容有误，请检查后重新提交；仍失败请联系管理员。',
      'request-payload-invalid',
    ],
  ])('网关校验失败的 %s 在 PDA 屏上是中文', async (_label, rawBody, expectedCopy, wireCode) => {
    createWorkOrder.mockRejectedValueOnce(asThrownByGeneratedClient(rawBody))
    route.query = { deviceAssetId: 'DEV-3333' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.find('[data-result][data-status="error"]').exists()).toBe(true)
    const shown = wrapper.text()
    expect(shown).toContain(expectedCopy)
    // 裸稳定码不得上屏（#3308 判例：400 在 actionableHttpMessage 里没有本地文案，
    // 回落链 `actionableMessage ?? serverMessage ?? fallback` 会把 wire 值直接甩给操作工）。
    expect(shown).not.toContain(wireCode)
    // errorData 里的半英文字段级句子同样不得上屏。
    expect(shown).not.toContain('idempotency Key')
    // 确定性失败 ⇒ 给重试，不给「核实」——本条不得顺带改掉这个判定。
    expect(wrapper.find('[data-testid="retry"]').exists()).toBe(true)
    expect(wrapper.find('[data-testid="verify-list"]').exists()).toBe(false)
  })

  // 对照：被修掉的旧形状。PDA 这一侧票面那句「用户看到英文常量」是**成立**的
  // （与 PC 不同，PC 那条链会退化成通用兜底，见 notify.test.ts 里的同名对照格）。
  it('对照：旧的 FastEndpoints 默认形状把英文常量送上 PDA 屏', async () => {
    createWorkOrder.mockRejectedValueOnce(
      asThrownByGeneratedClient(
        '{"statusCode":400,"message":"One or more errors occurred!","errors":' +
          '{"idempotencyKey":["\'idempotency Key\' 必须小于或等于128个字符。"]}}',
      ),
    )
    route.query = { deviceAssetId: 'DEV-3333' }
    const wrapper = mount(RepairPage)
    await selectPriority(wrapper, '高')
    await selectReason(wrapper, '液压泄漏')
    await wrapper.get('[data-testid="submit"]').trigger('click')
    await flushPromises()

    expect(wrapper.text()).toContain('One or more errors occurred!')
  })
})
