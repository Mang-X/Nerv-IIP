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
  errorStatusCode,
  friendlyErrorMessage,
  inlineErrorMessage,
  isForbiddenError,
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
  it.each([
    [
      'stored-maintenance-work-order-receipt-is-invalid',
      '工单创建回执异常，请刷新后重试；仍失败请联系管理员。',
    ],
    [
      'source-alarm-already-bound-to-a-different-create-intent',
      '该报警已关联其他维护工单，请刷新后核对。',
    ],
    [
      'stored-maintenance-completion-receipt-is-invalid',
      '工单完工回执异常，请刷新后重试；仍失败请联系管理员。',
    ],
    ['idempotency-conflict', '该操作标识已用于其他内容，请刷新后重新发起。'],
    ['idempotency-key-too-long', '操作标识过长，本次未提交；请重新发起，仍失败请联系管理员。'],
    [
      'idempotency-key-invalid-characters',
      '操作标识含不支持的字符，本次未提交；请重新发起，仍失败请联系管理员。',
    ],
    ['lifecycle-conflict', '状态已被其他操作更新'],
  ])('把稳定错误值 %s 映射为精确中文文案', (wireValue, message) => {
    expect(friendlyErrorMessage({ message: wireValue }, '原有兜底')).toBe(message)
  })

  // #3287：这两条码必须**先**被稳定表短路。下面那条通用正则
  // `/\b409\b|conflict|idempotency|intent|lifecycle|already bound/i` 是按**子串**匹配的，
  // `idempotency-key-too-long` 会命中 `idempotency` 并回「操作意图发生冲突」——
  // 那正是这两条错误码要消灭的误导。这一格断言的是「没有回那句」，不是「表里有条目」。
  it.each(['idempotency-key-too-long', 'idempotency-key-invalid-characters'])(
    '%s 不再落到通用冲突正则的「操作意图发生冲突」文案',
    (wireValue) => {
      const message = friendlyErrorMessage({ message: wireValue }, '原有兜底')
      expect(message).not.toContain('冲突')
      expect(message).not.toContain('刷新列表并核实最新状态')
      expect(message).not.toContain(wireValue)
      expect(message).toContain('本次未提交')
    },
  )

  it('未登记的稳定值继续使用原有兜底，不猜测文案', () => {
    expect(friendlyErrorMessage({ message: 'future-stable-error' }, '原有兜底')).toBe('原有兜底')
  })

  it('把网关 502 / downstream-invalid-response 映射成人话', () => {
    expect(friendlyErrorMessage(new Error('downstream-invalid-response'))).toContain('刷新列表核实')
    expect(friendlyErrorMessage({ message: '502 Bad Gateway' })).toContain('结果可能尚未确认')
    expect(friendlyErrorMessage('Error: 500')).toContain('刷新列表核实')
  })

  it('网关超时（downstream-timeout / 504）→ 可行动提示，任务可能仍在处理（#1306）', () => {
    expect(friendlyErrorMessage(new Error('downstream-timeout'))).toContain('任务可能仍在处理')
    expect(friendlyErrorMessage('504 Gateway Timeout')).toContain('刷新相关列表查看结果')
    // 不能被通用网络分支吞掉。
    expect(friendlyErrorMessage(new Error('downstream-timeout'))).not.toContain('网络异常')
  })

  // #3272：网关熔断打开（`BrokenCircuitException` → 503 + `downstream-circuit-open`）。
  //
  // 这一格断言的**不是**「表里有条目」，而是「正则改写不到它」——这是 #3308 栽过的那一格。
  // 实测过的失效方向：这个串不命中 friendlyErrorMessage 里的任何一条正则
  // （`downstream-timeout` / `\b503\b` / `service unavailable` / `timeout` 都不匹配它），
  // 所以**不登记就会一路落到通用兜底**「操作失败，请稍后重试。」，而不是落到 502/503 那句。
  // 因此下面既要断言拿到了这句，也要断言没有退化成兜底、没有被折进 5xx 通用句、没有裸码上屏。
  it('熔断打开（downstream-circuit-open）→ 说出「本次请求未发出」，不被通用正则改写（#3272）', () => {
    const message = friendlyErrorMessage({ message: 'downstream-circuit-open' }, '原有兜底')
    expect(message).toBe('服务暂时不可用，本次请求未发出；请稍后重试。')
    // 没有退化成兜底 —— 证明这条码确实被稳定表接住了。
    expect(message).not.toBe('原有兜底')
    // 没有被折进 502/503 的通用句 —— 那句说的是「结果可能尚未确认」，与熔断的事实相反。
    expect(message).not.toContain('刷新列表核实')
    // 没有把技术串甩给用户。
    expect(message).not.toContain('downstream')
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
  it('稳定错误值经过共享分层链显示中文，并保留动作前缀', () => {
    notifyOperationFailure(
      '创建工单失败',
      { message: 'stored-maintenance-work-order-receipt-is-invalid' },
      '创建工单失败，请稍后重试',
    )

    expect(toastError).toHaveBeenCalledWith(
      '创建工单失败：工单创建回执异常，请刷新后重试；仍失败请联系管理员。',
    )
  })

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

  // action 实参的主流写法已带「失败」后缀（现网 106 处里 103 处形如 '撤销失败'），
  // 所以兜底文案必须由调用方自己写；若按 action 派生就会拼出「撤销失败失败，请稍后重试」。
  it('动作前缀原样拼接，不额外补「失败」二字', () => {
    notifyOperationFailure('撤销失败', { detail: '工单已关闭，不能撤销' }, '撤销失败，请稍后重试')
    expect(toastError).toHaveBeenCalledWith('撤销失败：工单已关闭，不能撤销')
    expect(toastError).not.toHaveBeenCalledWith(expect.stringContaining('失败失败'))
  })
})

describe('notifyError / notifySuccess', () => {
  it('notifyError 通过共享分层链显示稳定错误中文', () => {
    notifyError({ message: 'idempotency-conflict' }, '操作失败，请稍后重试。')
    expect(toastError).toHaveBeenCalledWith('该操作标识已用于其他内容，请刷新后重新发起。')
  })

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
  it('行内入口通过共享分层链显示稳定错误中文', () => {
    expect(inlineErrorMessage({ message: 'lifecycle-conflict' })).toBe('状态已被其他操作更新')
  })

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

/**
 * MAN-698 批次 A（#1298 规格轴）：页面靠 `error instanceof Error && message.includes('403')`
 * 判权限，而 generated client 在 `throwOnError` 下抛的是**解析后的响应体对象**——
 * 判定永远不成立，真实 403 退化成普通失败态。状态码要从对象上取。
 */
describe('errorStatusCode / isForbiddenError', () => {
  it('从拦截器挂的 response 上取状态码（响应体对象不是 Error 实例）', () => {
    const error: Record<string, unknown> = { title: 'Forbidden' }
    Object.defineProperty(error, 'response', { value: { status: 403 }, enumerable: false })

    expect(error instanceof Error).toBe(false)
    expect(errorStatusCode(error)).toBe(403)
    expect(isForbiddenError(error)).toBe(true)
  })

  it('RFC7807 直接带 status / 包装后的 statusCode 都认', () => {
    expect(errorStatusCode({ status: 403, detail: 'forbidden' })).toBe(403)
    expect(errorStatusCode({ error: { statusCode: 404 } })).toBe(404)
  })

  it('取不到状态码时返回 undefined，不猜', () => {
    expect(errorStatusCode(undefined)).toBeUndefined()
    expect(errorStatusCode('boom')).toBeUndefined()
    expect(errorStatusCode({ message: '库存不足' })).toBeUndefined()
  })

  it('取不到状态码才退回文本匹配；其他状态码一律不是「无权限」', () => {
    expect(isForbiddenError(new Error('403 Forbidden'))).toBe(true)
    expect(isForbiddenError({ status: 500, detail: 'forbidden zone' })).toBe(false)
    expect(isForbiddenError({ message: '物料缺料' })).toBe(false)
  })

  // `status` 也是很常见的业务字段名：领域状态值不能被当成 HTTP 码，
  // 否则一个业务枚举就能把页面骗进「无权限」空态。
  it('只认合法 HTTP 状态码区间，业务响应体里的数值 status 不当 HTTP 码', () => {
    expect(errorStatusCode({ status: 3 })).toBeUndefined()
    expect(errorStatusCode({ status: 0 })).toBeUndefined()
    expect(errorStatusCode({ status: 99 })).toBeUndefined()
    expect(errorStatusCode({ status: 600 })).toBeUndefined()
    expect(errorStatusCode({ status: 403.5 })).toBeUndefined()
    expect(errorStatusCode({ status: 100 })).toBe(100)
    expect(errorStatusCode({ status: 599 })).toBe(599)
    // 业务体里 status 是领域状态时，继续往下层找真正的 HTTP 码。
    expect(errorStatusCode({ status: 3, response: { status: 403 } })).toBe(403)
  })

  // 文本兜底只认技术串：中文领域消息里的 403 是业务数字，不是状态码。
  it('中文领域消息里的 403 不被文本兜底误判成无权限', () => {
    expect(isForbiddenError({ status: 3, message: '任务状态为 403 号工序' })).toBe(false)
    expect(isForbiddenError({ message: '第 403 号检验方案已停用。' })).toBe(false)
    expect(isForbiddenError('403 Forbidden')).toBe(true)
    expect(isForbiddenError(new Error('Request failed with status code 403'))).toBe(true)
  })

  it('循环引用不会把递归拖死（拦截器把 response 挂回 error 很常见）', () => {
    const error: Record<string, unknown> = { detail: 'boom' }
    error.cause = error
    expect(errorStatusCode(error)).toBeUndefined()
  })
})

describe('WMS 拒绝原因代码（#1397 / 台账 #81）', () => {
  it('出库复核的 422 说清「卡在哪 + 去哪解」，而不是「请检查填写项」', () => {
    const message = friendlyErrorMessage({ message: 'outbound-picking-not-completed' }, '兜底', {
      outboundOrderNo: 'OB-WQ-B-PICK-MIR-20260731-02',
    })
    // 点名对象
    expect(message).toContain('OB-WQ-B-PICK-MIR-20260731-02')
    // 说清卡在哪
    expect(message).toContain('拣货任务尚未完成')
    // 给出路
    expect(message).toContain('拣货任务')
    // 这条正是被修掉的无信息量文案，绝不能再出现
    expect(message).not.toContain('请检查填写项')
  })

  it('拿不到单号时退化成不点名的句子，但仍然给出路', () => {
    const message = friendlyErrorMessage({ message: 'outbound-picking-not-completed' }, '兜底')
    expect(message).toContain('该出库单')
    expect(message).not.toContain('请检查填写项')
  })

  it('原因代码必须排在通用 422/403 分支之前，否则又退化成泛化文案', () => {
    expect(friendlyErrorMessage({ message: 'outbound-pack-review-not-passed' })).toContain(
      '复核通过',
    )
    // 403 家族同理：不能被「没有权限执行此操作」一句话盖掉
    const forbidden = friendlyErrorMessage({ message: 'resource-not-assigned-to-self' })
    expect(forbidden).toContain('已派给其他作业员')
    expect(forbidden).not.toBe('没有权限执行此操作。')
  })

  // 替身码在 #3155 换过一次：原来用的是 `unprocessable`，而它其实是 WMS 中间件真会外发的
  // 稳定码（`WmsUnprocessableException.SafeCode`），只是当时两侧没登记。#3155 把后端注册表与
  // 前端词表焊成单向包含之后它被登记了，于是**不再是**「未登记的代码」，拿它做替身就测不到本条性质。
  // 顺带说明为什么登记它是修复而不是回退：`unprocessable` 的语义是业务前置条件不满足，
  // 落到 422 泛化分支拿到的「请检查填写项」根本没有填写项可查——正是本 describe 块要消灭的那类文案。
  it('未登记的代码不猜语义，落回原有分层兜底', () => {
    expect(
      friendlyErrorMessage({ message: 'unprocessable-entity-for-an-unlisted-reason' }),
    ).toContain('请检查填写项')
  })

  it('分层链上的三个入口都能拿到中文原因（toast 与行内同一口径）', () => {
    notifyOperationFailure(
      '提交出库复核失败',
      { message: 'outbound-picking-not-completed' },
      '兜底',
      { outboundOrderNo: 'OB-1' },
    )
    expect(toastError).toHaveBeenCalledWith(
      expect.stringContaining('出库单 OB-1的拣货任务尚未完成'),
    )
    expect(
      inlineErrorMessage({ message: 'outbound-picking-not-completed' }, '兜底', {
        outboundOrderNo: 'OB-1',
      }),
    ).toContain('出库单 OB-1')
  })
})

// #3333：把字符串追到屏幕（PC 侧那一格）。
//
// 为什么这一格不写成 `friendlyErrorMessage('request-payload-invalid')`：那只证明了链路中段。
// #3308 的判例是「治理合规 + 七格变异全绿，却在屏上甩裸英文码」——链路末端才是被修的东西。
// 所以这里喂的是**网关实际写出的整个响应体**（下面那段 JSON 逐字节取自
// `BusinessGatewayValidationFailureEnvelopeTests` 覆盖的同一条通道的实跑输出），
// 走的是页面真正调用的 `notifyOperationFailure`，断言的是**toast 收到的那句话**
// ——toast 就是 PC 侧的屏幕（反馈规范：操作结果一律 toast，不留常驻文字）。
describe('#3333 网关校验失败的响应体在 PC 屏上是中文', () => {
  /**
   * 网关校验失败的**原样响应体**。generated client 在失败时 `JSON.parse` 响应文本后
   * 直接 throw 这个对象（见 `client.gen.ts` 的 `throw jsonError ?? textError`），
   * 所以页面 catch 到的就是它；error 拦截器再把原始 `Response` 以非枚举属性挂上去。
   */
  const gatewayValidationFailureBody = JSON.parse(
    '{"success":false,"message":"request-payload-invalid","code":400,"errorData":' +
      '[{"name":"idempotencyKey","reason":"\'idempotency Key\' 必须小于或等于128个字符。您输入了129个字符。"}]}',
  ) as Record<string, unknown>

  function asThrownByClient() {
    const error = { ...gatewayValidationFailureBody }
    Object.defineProperty(error, 'response', {
      configurable: true,
      enumerable: false,
      value: { status: 400 },
    })
    return error
  }

  it('toast 上的是可操作中文，不是英文常量也不是裸稳定码', () => {
    notifyOperationFailure('提交失败', asThrownByClient(), '提交失败，请稍后重试')

    expect(toastError).toHaveBeenCalledWith(
      '提交失败：提交的内容有误，请检查后重新提交；仍失败请联系管理员。',
    )
    const shown = String(toastError.mock.calls[0][0])
    expect(shown).not.toContain('request-payload-invalid')
    expect(shown).not.toContain('One or more errors occurred')
    // 兜底句也不算修好：它是「什么都没取到」的信号，不是这次失败的原因。
    expect(shown).not.toBe('提交失败，请稍后重试')
  })

  // errorData 里的逐字段原因**不上屏**。这一格是那条边界的护栏：哪天有人让
  // `serverErrorMessage` 去读 errorData，「'idempotency Key' 必须小于或等于128个字符」
  // 这种半英文句子就会顶掉上面那句中文。
  it('errorData 里的字段级句子不进 toast', () => {
    notifyOperationFailure('提交失败', asThrownByClient(), '提交失败，请稍后重试')

    const shown = String(toastError.mock.calls[0][0])
    expect(shown).not.toContain('idempotency Key')
    expect(shown).not.toContain('128')
  })

  // 旧形状（FastEndpoints 默认）作为对照。
  //
  // ⚠️ 这里要如实记一笔：#3333 票面说「用户看到的是英文常量」——那句话对 PDA 成立
  // （`actionableHttpMessage(400)` 返回 undefined，回落链 `actionableMessage ?? serverMessage`
  // 直接把英文常量上屏，见 PDA 那一格），但**对 PC 不成立**。PC 这条链上
  // `friendlyErrorMessage` 的所有分支都匹配不到 `One or more errors occurred!`，
  // 也不含中文，于是返回 fallback ⇒ 屏上是调用方的**通用兜底句**。
  // 缺陷同样成立（用户拿不到任何可操作原因），但成因和症状与票面描述不同，别沿用那句话。
  it('对照：旧的 FastEndpoints 默认形状在 PC 上退化成通用兜底（不是英文常量）', () => {
    notifyOperationFailure(
      '提交失败',
      {
        statusCode: 400,
        message: 'One or more errors occurred!',
        errors: { idempotencyKey: ["'idempotency Key' 必须小于或等于128个字符。"] },
      },
      '提交失败，请稍后重试',
    )

    expect(toastError).toHaveBeenCalledWith('提交失败，请稍后重试')
    const shown = String(toastError.mock.calls[0][0])
    expect(shown).not.toContain('One or more errors occurred')
    expect(shown).not.toContain('提交的内容有误')
  })
})
