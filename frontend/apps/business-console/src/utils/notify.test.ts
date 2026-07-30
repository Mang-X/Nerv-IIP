import { beforeEach, describe, expect, it, vi } from 'vitest'

const toastError = vi.fn()
const toastSuccess = vi.fn()
vi.mock('@nerv-iip/ui', () => ({
  toast: {
    error: (...a: unknown[]) => toastError(...a),
    success: (...a: unknown[]) => toastSuccess(...a),
  },
}))

const {
  friendlyErrorMessage,
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
  serverErrorMessage,
} = await import('./notify')

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

describe('serverErrorMessage', () => {
  it('透传信封 message（200 + success:false 与 4xx 信封同款）', () => {
    expect(serverErrorMessage({ success: false, message: '工单缺少生产版本，无法排程' })).toBe(
      '工单缺少生产版本，无法排程',
    )
  })

  it('透传 problem detail：detail 优先于 title', () => {
    expect(
      serverErrorMessage({
        title: 'Bad Request',
        detail: '排程窗口内没有可用资源日历',
        status: 400,
      }),
    ).toBe('排程窗口内没有可用资源日历')
    expect(serverErrorMessage({ title: '排程服务内部错误', status: 500 })).toBe('排程服务内部错误')
  })

  it('汇总 problem detail 的字段校验错误', () => {
    expect(
      serverErrorMessage({
        title: '',
        errors: { HorizonEndUtc: ['结束时间必须晚于开始时间'], Orders: ['至少选择一个工单'] },
      }),
    ).toBe('结束时间必须晚于开始时间；至少选择一个工单')
  })

  it('generated client 抛出的是响应体对象，不是 Error —— 也要能取到消息', () => {
    // hey-api 在 throwOnError 下 throw 的是解析后的响应体，`error instanceof Error` 为 false。
    const thrown: unknown = { error: { message: '方案已失效，请重排后再发布' } }
    expect(thrown instanceof Error).toBe(false)
    expect(serverErrorMessage(thrown)).toBe('方案已失效，请重排后再发布')
  })

  it('Error 实例、字符串同样取得到；取不到时返回空串交给调用方兜底', () => {
    expect(serverErrorMessage(new Error('排程服务未确认发布结果。'))).toBe(
      '排程服务未确认发布结果。',
    )
    expect(serverErrorMessage('Internal Server Error')).toBe('Internal Server Error')
    expect(serverErrorMessage(undefined)).toBe('')
    expect(serverErrorMessage({ status: 500 })).toBe('')
  })

  it('循环引用不炸栈；超长消息截断到与中文透传同一阈值', () => {
    const cyclic: Record<string, unknown> = { status: 500 }
    cyclic.response = cyclic
    expect(serverErrorMessage(cyclic)).toBe('')
    expect(serverErrorMessage({ message: '排'.repeat(400) })).toHaveLength(60)
  })
})

describe('notifyOperationFailure', () => {
  it('服务端领域消息（中文、可行动）带动作前缀原样透传', () => {
    notifyOperationFailure(
      '生成失败',
      { title: 'Bad Request', detail: '工单缺少生产版本，无法排程', status: 400 },
      '生成失败，请检查工单生产版本与排程基础数据',
    )
    expect(toastError).toHaveBeenCalledWith('生成失败：工单缺少生产版本，无法排程')
  })

  it('英文通用 HTTP 文案不上屏：无可映射语义时退到调用方兜底，原文只进 console', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    notifyOperationFailure('发布失败', { title: 'Internal Server Error' }, '发布失败，请稍后重试')

    expect(toastError).toHaveBeenCalledWith('发布失败，请稍后重试')
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('Internal Server Error'))
    expect(consoleError).toHaveBeenCalledWith(
      expect.stringContaining('发布失败'),
      'Internal Server Error',
      expect.anything(),
    )
    consoleError.mockRestore()
  })

  it('可识别的技术串走 friendlyErrorMessage 映射成人话，不甩英文错误码', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    notifyOperationFailure('发布失败', { detail: '502 Bad Gateway' }, '发布失败，请稍后重试')

    expect(toastError).toHaveBeenCalledWith(
      '发布失败：服务暂时不可用，操作结果可能尚未确认；请刷新列表核实后再重试。',
    )
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('502'))
    consoleError.mockRestore()
  })

  it('服务端什么都没说 → 调用方的领域兜底文案', () => {
    notifyOperationFailure('撤销失败', { status: 500 }, '撤销失败，请稍后重试')
    expect(toastError).toHaveBeenCalledWith('撤销失败，请稍后重试')
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

  // MAN-700 / #1289：generated client 抛的是响应体对象，旧实现只判 instanceof Error，
  // 于是 ERP 报价转订单的 400 领域理由全被吞成「创建销售订单失败，请稍后重试。」。
  it('notifyError 透传响应体对象里的中文领域消息，不吞成兜底文案', () => {
    notifyError({ detail: '报价单已过期，不能转订单' }, '创建销售订单失败，请稍后重试。')
    expect(toastError).toHaveBeenCalledWith('报价单已过期，不能转订单')
  })

  it('notifyError 遇英文 500 body 用调用方兜底，原文只进 console', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    notifyError({ title: 'Internal Server Error', status: 500 }, '创建销售订单失败，请稍后重试。')

    expect(toastError).toHaveBeenCalledWith('创建销售订单失败，请稍后重试。')
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('Internal Server'))
    expect(consoleError).toHaveBeenCalled()
    consoleError.mockRestore()
  })
})

describe('inlineErrorMessage', () => {
  it('无错误时返回空串，模板可直接判空', () => {
    expect(inlineErrorMessage(undefined)).toBe('')
    expect(inlineErrorMessage(null)).toBe('')
  })

  it('与 toast 同源：中文领域消息原样显示', () => {
    expect(inlineErrorMessage({ message: '当前业务范围内没有该工作中心' })).toBe(
      '当前业务范围内没有该工作中心',
    )
  })

  it('行内错误条同样不许出现英文错误码 / 5xx 原文', () => {
    const consoleError = vi.spyOn(console, 'error').mockImplementation(() => {})
    expect(inlineErrorMessage({ detail: '502 Bad Gateway' })).toBe(
      '服务暂时不可用，操作结果可能尚未确认；请刷新列表核实后再重试。',
    )
    expect(inlineErrorMessage({ title: 'Internal Server Error' }, '库存台账读取失败。')).toBe(
      '库存台账读取失败。',
    )
    consoleError.mockRestore()
  })
})
