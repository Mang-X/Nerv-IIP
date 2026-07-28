import { beforeEach, describe, expect, it, vi } from 'vitest'

const toastError = vi.fn()
const toastSuccess = vi.fn()
vi.mock('@nerv-iip/ui', () => ({
  toast: {
    error: (...a: unknown[]) => toastError(...a),
    success: (...a: unknown[]) => toastSuccess(...a),
  },
}))

const { friendlyErrorMessage, notifyError, notifySuccess } = await import('./notify')

beforeEach(() => {
  toastError.mockClear()
  toastSuccess.mockClear()
})

describe('friendlyErrorMessage', () => {
  it('把网关 502 / downstream-invalid-response 映射成人话', () => {
    expect(friendlyErrorMessage(new Error('downstream-invalid-response'))).toContain('刷新列表核实')
    expect(friendlyErrorMessage({ message: '502 Bad Gateway' })).toContain('结果可能尚未确认')
    expect(friendlyErrorMessage('Error: 500')).toContain('刷新列表核实')
  })

  it('网络错误 → 人话', () => {
    expect(friendlyErrorMessage(new Error('Failed to fetch'))).toContain('刷新列表核实')
    expect(friendlyErrorMessage('NetworkError when attempting')).toContain('结果可能尚未确认')
  })

  it('鉴权 / 权限分别映射', () => {
    expect(friendlyErrorMessage('401 unauthorized')).toBe('登录已过期，请重新登录。')
    expect(friendlyErrorMessage('403 forbidden')).toBe('没有权限执行此操作。')
  })

  it('404 / 409 生命周期冲突 / 422 给出可执行恢复动作', () => {
    expect(friendlyErrorMessage('404 not found')).toContain('不存在或已不在当前业务范围')
    expect(friendlyErrorMessage('409 conflict: idempotency intent mismatch')).toContain(
      '状态或操作意图发生冲突',
    )
    expect(friendlyErrorMessage('422 validation failed')).toContain('检查填写项')
  })

  it('只有明确的编码/名称重复冲突才提示更换编码或名称', () => {
    expect(friendlyErrorMessage('409 conflict: code already exists')).toBe(
      '编码或名称已存在，请更换后重试。',
    )
    expect(friendlyErrorMessage('409 conflict: work order already completed')).not.toContain('更换')
  })

  it('未确认业务回执要求保留当前操作并先回读', () => {
    expect(
      friendlyErrorMessage(
        new Error('BusinessOperationUnconfirmedError: business-operation-unconfirmed'),
      ),
    ).toContain('保留当前操作')
  })

  it('系统管理项不可改 → 人话', () => {
    expect(
      friendlyErrorMessage(
        new Error("system-managed reference data 'uom-dimension:time' cannot be updated."),
      ),
    ).toBe('该项由系统管理（平台固化），不可修改。')
  })

  it('后端可读中文业务校验信息直接透传（短文本）', () => {
    expect(friendlyErrorMessage(new Error('业务规则校验未通过'))).toBe('业务规则校验未通过')
  })

  it('空 / 无法识别 → 兜底文案', () => {
    expect(friendlyErrorMessage(null)).toBe('操作失败，请稍后重试。')
    expect(friendlyErrorMessage(new Error(''))).toBe('操作失败，请稍后重试。')
    expect(friendlyErrorMessage({})).toBe('操作失败，请稍后重试。')
    expect(friendlyErrorMessage('x', '自定义兜底')).toBe('自定义兜底')
  })
})

describe('notifyError / notifySuccess', () => {
  it('notifyError 用映射后的人话调用 toast.error，不暴露原始技术串', () => {
    notifyError(new Error('downstream-invalid-response'))
    expect(toastError).toHaveBeenCalledWith(
      '服务暂时不可用，操作结果可能尚未确认；请刷新列表核实后再重试。',
    )
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('downstream'))
  })

  it('notifySuccess 透传到 toast.success', () => {
    notifySuccess('物料「A」已创建。')
    expect(toastSuccess).toHaveBeenCalledWith('物料「A」已创建。')
  })
})
