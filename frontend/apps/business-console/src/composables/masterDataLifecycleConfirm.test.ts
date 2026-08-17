import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import { useMasterDataLifecycleConfirm } from './masterDataLifecycleConfirm'

const stub = vi.hoisted(() => ({
  toastSuccess: vi.fn(),
  toastError: vi.fn(),
}))

vi.mock('@nerv-iip/ui', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/ui')>()),
  toast: { success: stub.toastSuccess, error: stub.toastError },
}))

const disable = vi.fn().mockResolvedValue({})
const enable = vi.fn().mockResolvedValue({})
const actions = {
  disable,
  enable,
  disablePending: shallowRef(false),
  enablePending: shallowRef(false),
}
const activeRow = { resourceType: 'unit-of-measure', code: 'EA', displayName: '个', active: true }
const disabledRow = { ...activeRow, code: 'MPa', displayName: '兆帕', active: false }

beforeEach(() => {
  disable.mockClear()
  enable.mockClear()
  stub.toastSuccess.mockClear()
  stub.toastError.mockClear()
  actions.disablePending.value = false
  actions.enablePending.value = false
})

describe('主数据停用/启用确认（页面层单实例控制器）', () => {
  it('未 request 前不打开，原因为空', () => {
    const c = useMasterDataLifecycleConfirm()
    expect(c.open.value).toBe(false)
    expect(c.reason.value).toBe('')
    expect(c.canConfirm.value).toBe(false)
  })

  it('request 打开确认框并按行状态给出动作文案', () => {
    const c = useMasterDataLifecycleConfirm()
    c.request(activeRow, actions, '计量单位')
    expect(c.open.value).toBe(true)
    expect(c.entityLabel.value).toBe('计量单位')
    expect(c.actionLabel.value).toBe('停用')

    c.request(disabledRow, actions, '计量单位')
    expect(c.actionLabel.value).toBe('启用')
  })

  it('没有 code 的行不开框（列表偶发缺编码时不该弹一个提交不了的确认框）', () => {
    const c = useMasterDataLifecycleConfirm()
    c.request({ ...activeRow, code: undefined }, actions, '计量单位')
    expect(c.open.value).toBe(false)
  })

  it('原因为空或纯空白时不可确认，确认也不发请求', async () => {
    const c = useMasterDataLifecycleConfirm()
    c.request(activeRow, actions, '计量单位')
    expect(c.canConfirm.value).toBe(false)

    await c.confirm()
    expect(disable).not.toHaveBeenCalled()

    c.reason.value = '   '
    expect(c.canConfirm.value).toBe(false)
    await c.confirm()
    expect(disable).not.toHaveBeenCalled()
  })

  it('停用把去空白后的原因原样提交并关框', async () => {
    const c = useMasterDataLifecycleConfirm()
    c.request(activeRow, actions, '计量单位')
    c.reason.value = '  产线拆除，改用公制单位  '
    expect(c.canConfirm.value).toBe(true)

    await c.confirm()

    expect(disable).toHaveBeenCalledWith('EA', { reason: '产线拆除，改用公制单位' })
    expect(stub.toastSuccess).toHaveBeenCalledWith('计量单位已停用。')
    expect(c.open.value).toBe(false)
    expect(c.reason.value).toBe('')
  })

  it('重新启用走 enable 并同样必填原因', async () => {
    const c = useMasterDataLifecycleConfirm()
    c.request(disabledRow, actions, '计量单位')
    c.reason.value = '旧工艺卡仍在用，恢复该单位'
    await c.confirm()

    expect(enable).toHaveBeenCalledWith('MPa', { reason: '旧工艺卡仍在用，恢复该单位' })
    expect(stub.toastSuccess).toHaveBeenCalledWith('计量单位已启用。')
  })

  it('再次 request 时清空上一条原因，不带进下一次审计', () => {
    const c = useMasterDataLifecycleConfirm()
    c.request(activeRow, actions, '计量单位')
    c.reason.value = '供应商终止合作'
    c.request(disabledRow, actions, '计量单位')
    expect(c.reason.value).toBe('')
    expect(c.canConfirm.value).toBe(false)
  })

  it('提交失败时保留原因与目标，便于原地重试', async () => {
    disable.mockRejectedValueOnce(new Error('停用失败'))
    const c = useMasterDataLifecycleConfirm()
    c.request(activeRow, actions, '计量单位')
    c.reason.value = '设备报废'
    await c.confirm()

    expect(stub.toastError).toHaveBeenCalled()
    expect(c.open.value).toBe(true)
    expect(c.reason.value).toBe('设备报废')
    expect(c.row.value?.code).toBe('EA')
  })

  it('进行中不可重复确认（pending 跟随当前这套动作）', () => {
    const c = useMasterDataLifecycleConfirm()
    c.request(activeRow, actions, '计量单位')
    c.reason.value = '设备报废'
    expect(c.canConfirm.value).toBe(true)

    actions.disablePending.value = true
    expect(c.pending.value).toBe(true)
    expect(c.canConfirm.value).toBe(false)
  })

  it('同一个控制器可在多张表之间切换目标（一页多表共用一个确认框）', async () => {
    const conversionDisable = vi.fn().mockResolvedValue({})
    const conversionActions = { ...actions, disable: conversionDisable }
    const c = useMasterDataLifecycleConfirm()

    c.request(activeRow, actions, '计量单位')
    c.reason.value = '产线拆除'
    await c.confirm()
    expect(disable).toHaveBeenCalledTimes(1)

    c.request({ ...activeRow, code: 'BOX→EA' }, conversionActions, '换算关系')
    c.reason.value = '换算系数按新版工艺重定'
    await c.confirm()
    expect(conversionDisable).toHaveBeenCalledWith('BOX→EA', {
      reason: '换算系数按新版工艺重定',
    })
    // 切换目标后不会误用上一张表的动作。
    expect(disable).toHaveBeenCalledTimes(1)
  })
})
