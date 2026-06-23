import { beforeEach, describe, expect, it, vi } from 'vitest'

const toastError = vi.fn()
const toastSuccess = vi.fn()
vi.mock('@nerv-iip/ui', () => ({
  toast: { error: (...a: unknown[]) => toastError(...a), success: (...a: unknown[]) => toastSuccess(...a) },
}))

const { friendlyErrorMessage, notifyError, notifySuccess } = await import('./notify')

beforeEach(() => {
  toastError.mockClear()
  toastSuccess.mockClear()
})

describe('friendlyErrorMessage', () => {
  it('把网关 502 / downstream-invalid-response 映射成人话', () => {
    expect(friendlyErrorMessage(new Error('downstream-invalid-response'))).toBe('服务暂时不可用，请稍后重试。')
    expect(friendlyErrorMessage({ message: '502 Bad Gateway' })).toBe('服务暂时不可用，请稍后重试。')
    expect(friendlyErrorMessage('Error: 500')).toBe('服务暂时不可用，请稍后重试。')
  })

  it('网络错误 → 人话', () => {
    expect(friendlyErrorMessage(new Error('Failed to fetch'))).toBe('网络异常，请检查连接后重试。')
    expect(friendlyErrorMessage('NetworkError when attempting')).toBe('网络异常，请检查连接后重试。')
  })

  it('鉴权 / 权限 / 冲突分别映射', () => {
    expect(friendlyErrorMessage('401 unauthorized')).toBe('登录已过期，请重新登录。')
    expect(friendlyErrorMessage('403 forbidden')).toBe('没有权限执行此操作。')
    expect(friendlyErrorMessage('409 conflict: already exists')).toBe('编码或名称已存在，请更换后重试。')
  })

  it('系统管理项不可改 → 人话', () => {
    expect(friendlyErrorMessage(new Error("system-managed reference data 'uom-dimension:time' cannot be updated.")))
      .toBe('该项由系统管理（平台固化），不可修改。')
  })

  it('后端可读中文业务校验信息直接透传（短文本）', () => {
    expect(friendlyErrorMessage(new Error('该编码已被占用'))).toBe('该编码已被占用')
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
    expect(toastError).toHaveBeenCalledWith('服务暂时不可用，请稍后重试。')
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('downstream'))
  })

  it('notifySuccess 透传到 toast.success', () => {
    notifySuccess('物料「A」已创建。')
    expect(toastSuccess).toHaveBeenCalledWith('物料「A」已创建。')
  })
})
