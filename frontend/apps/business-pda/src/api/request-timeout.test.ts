import { afterEach, describe, expect, it, vi } from 'vitest'
import {
  createTimeoutFetch,
  describeRequestError,
  isIndeterminateError,
  OfflineError,
  REQUEST_TIMEOUT_MS,
  RequestTimeoutError,
  resolveRequestTimeoutMs,
} from './request-timeout'
import {
  BusinessOperationFailedError,
  BusinessOperationUnconfirmedError,
} from '@nerv-iip/api-client'

/** A fetch that never resolves on its own — only rejects (AbortError) when its signal aborts. */
function hangingFetch(): typeof fetch {
  return ((_input: RequestInfo | URL, init?: RequestInit) =>
    new Promise<Response>((_resolve, reject) => {
      const signal = init?.signal
      if (signal) {
        signal.addEventListener('abort', () => {
          reject(new DOMException('The operation was aborted.', 'AbortError'))
        })
      }
    })) as typeof fetch
}

afterEach(() => {
  vi.useRealTimers()
  vi.restoreAllMocks()
})

describe('createTimeoutFetch', () => {
  it('rejects with OfflineError and never dispatches when offline', async () => {
    const baseFetch = vi.fn<typeof fetch>()
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => true })

    await expect(timeoutFetch('/api/business-console/v1/ping')).rejects.toBeInstanceOf(OfflineError)
    expect(baseFetch).not.toHaveBeenCalled()
  })

  it('passes a successful response through (status + body readable)', async () => {
    const response = new Response('ok', { status: 200 })
    const baseFetch = vi.fn<typeof fetch>().mockResolvedValue(response)
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })

    const result = await timeoutFetch('/api/business-console/v1/ping')
    expect(result.status).toBe(200)
    expect(await result.text()).toBe('ok')
    expect(baseFetch).toHaveBeenCalledTimes(1)
  })

  it('bounds a body that stalls AFTER headers (30s covers the whole facade, not just headers)', async () => {
    vi.useFakeTimers()
    // Headers resolve immediately; the body read only settles when the signal aborts.
    const baseFetch = ((_input: RequestInfo | URL, init?: RequestInit) =>
      Promise.resolve({
        ok: true,
        status: 200,
        body: {},
        headers: new Headers(),
        text: () =>
          new Promise((_resolve, reject) => {
            init?.signal?.addEventListener('abort', () =>
              reject(new DOMException('The operation was aborted.', 'AbortError')),
            )
          }),
      })) as unknown as typeof fetch
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false, timeoutMs: 1_000 })

    const response = await timeoutFetch('/api/business-console/v1/slow-body')
    const assertion = expect(response.text()).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
  })

  it('translates its own timeout abort into a RequestTimeoutError', async () => {
    vi.useFakeTimers()
    const timeoutFetch = createTimeoutFetch({
      baseFetch: hangingFetch(),
      isOffline: () => false,
      timeoutMs: 1_000,
    })

    const pending = timeoutFetch('/api/business-console/v1/slow')
    const assertion = expect(pending).rejects.toBeInstanceOf(RequestTimeoutError)
    await vi.advanceTimersByTimeAsync(1_000)
    await assertion
  })

  it('propagates a caller cancellation verbatim instead of masking it as a timeout', async () => {
    const controller = new AbortController()
    const timeoutFetch = createTimeoutFetch({
      baseFetch: hangingFetch(),
      isOffline: () => false,
    })

    const pending = timeoutFetch('/api/business-console/v1/slow', { signal: controller.signal })
    const assertion = expect(pending).rejects.toSatisfy(
      (error: unknown) =>
        !(error instanceof RequestTimeoutError) &&
        (error as { name?: string })?.name === 'AbortError',
    )
    controller.abort()
    await assertion
  })

  it('short-circuits to the base fetch when the caller signal is already aborted', async () => {
    const controller = new AbortController()
    controller.abort()
    const rejected = new DOMException('The operation was aborted.', 'AbortError')
    const baseFetch = vi.fn<typeof fetch>().mockRejectedValue(rejected)
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })

    await expect(
      timeoutFetch('/api/business-console/v1/ping', { signal: controller.signal }),
    ).rejects.toBe(rejected)
    expect(baseFetch).toHaveBeenCalledTimes(1)
  })

  it('passes gateway/business errors through untouched (not every failure is a timeout)', async () => {
    const businessError = { success: false, message: '工序状态非法' }
    const baseFetch = vi.fn<typeof fetch>().mockRejectedValue(businessError)
    const timeoutFetch = createTimeoutFetch({ baseFetch, isOffline: () => false })

    await expect(timeoutFetch('/api/business-console/v1/report')).rejects.toBe(businessError)
  })
})

describe('typed request errors', () => {
  it('default to actionable Chinese copy and remain Error instances', () => {
    const timeout = new RequestTimeoutError()
    const offline = new OfflineError()

    expect(timeout).toBeInstanceOf(Error)
    expect(timeout.message).toBe('网络超时，请检查连接后重试')
    expect(offline).toBeInstanceOf(Error)
    expect(offline.message).toBe('当前离线，请检查网络连接后重试')
  })

  it('exposes a 30s default ceiling', () => {
    expect(REQUEST_TIMEOUT_MS).toBe(30_000)
  })
})

describe('describeRequestError', () => {
  it.each([
    [401, '登录已失效，请重新登录', false],
    [403, '当前账号无此操作权限', false],
    [404, '业务对象已不存在', false],
    [409, '状态已变化', false],
    [422, '未通过业务校验', false],
    [503, '服务暂时不可用', true],
  ])('maps HTTP %s to consistent actionable copy', (status, expected, indeterminate) => {
    expect(describeRequestError({ status, message: 'downstream-request-failed' })).toMatchObject({
      message: expect.stringContaining(expected),
      status,
      indeterminate,
    })
  })

  // #1324：三道开工拦截的反馈必须一致——缺料的服务端真因以前被通用 HTTP 文案吞掉。
  it.each([
    [409, '物料齐套未满足，物料 MAT-OIL 缺口 5'],
    [422, '物料齐套未满足：MAT-OIL 缺口 5'],
    [400, '当前工序尚未派工，不能开工'],
  ])(
    'lets the server business reason through for HTTP %s instead of generic copy',
    (status, reason) => {
      expect(describeRequestError({ status, message: reason })).toMatchObject({
        status,
        message: reason,
        indeterminate: false,
      })
    },
  )

  it('keeps local guidance when the server only returns a technical string or an auth failure', () => {
    expect(
      describeRequestError({ status: 409, message: 'downstream-request-failed' }),
    ).toMatchObject({ message: expect.stringContaining('状态已变化') })
    expect(
      describeRequestError({ status: 403, message: '缺少权限码 business.mes.operations.manage' }),
    ).toMatchObject({ message: expect.stringContaining('当前账号无此操作权限') })
  })

  it('classifies a DISPATCHED timeout / network drop as INDETERMINATE (result unknown, non-idempotent retry unsafe)', () => {
    expect(describeRequestError(new RequestTimeoutError())).toMatchObject({
      kind: 'timeout',
      indeterminate: true,
      message: '网络超时，请检查连接后重试',
    })
    expect(describeRequestError(new TypeError('Failed to fetch'))).toMatchObject({
      kind: 'network',
      indeterminate: true,
      message: '网络连接失败，请检查网络后重试',
    })
  })

  it.each([
    new BusinessOperationUnconfirmedError(
      '请求已受理，但权威状态尚未确认（downstream-invalid-response）',
    ),
    {
      code: 'business-operation-unconfirmed',
      message: 'raw technical readback failure',
    },
  ])(
    'classifies an accepted-but-unconfirmed business receipt as indeterminate with field copy',
    (error) => {
      const described = describeRequestError(error)

      expect(described).toMatchObject({
        kind: 'business',
        indeterminate: true,
        message: '操作已受理，但结果尚未核实。请刷新列表确认状态，勿重复提交',
      })
      expect(described.message).not.toContain('downstream')
      expect(described.message).not.toContain('technical')
    },
  )

  it('surfaces a confirmed business failure as determinate safe copy', () => {
    expect(
      describeRequestError(
        new BusinessOperationFailedError(
          'Stock movement would make on-hand quantity negative.',
          'NEGATIVE_ON_HAND',
        ),
      ),
    ).toMatchObject({
      kind: 'business',
      indeterminate: false,
      message: '库存不足，无法完成本次库存过账，请核对数量后重试',
    })
  })

  it('classifies an OFFLINE pre-check as safe to retry (request never left the device)', () => {
    expect(describeRequestError(new OfflineError())).toMatchObject({
      kind: 'offline',
      indeterminate: false,
      message: '当前离线，请检查网络连接后重试',
    })
  })

  it('treats a standalone gateway business error as determinate', () => {
    expect(describeRequestError({ success: false, message: '工序状态非法' })).toMatchObject({
      kind: 'business',
      indeterminate: false,
      message: '工序状态非法',
    })
    // problem-details fall back to detail/title when message is absent.
    expect(describeRequestError({ detail: '库存不足' }).message).toBe('库存不足')
    expect(describeRequestError({ title: '请求无效' }).message).toBe('请求无效')
  })

  it.each([
    [{ statusCode: 503, message: '服务暂不可用' }],
    [{ response: { status: 503 }, message: '服务暂不可用' }],
  ])('treats a structured HTTP 5xx as indeterminate', (error) => {
    expect(describeRequestError(error)).toMatchObject({
      indeterminate: true,
      message: '服务暂时不可用，请稍后重试；写操作请先刷新核实结果',
    })
  })

  it.each([{ statusCode: 400 }, { statusCode: 422 }, { response: { status: 422 } }])(
    'keeps a structured HTTP 4xx determinate',
    (error) => {
      expect(describeRequestError(error).indeterminate).toBe(false)
    },
  )

  it('classifies a Response 5xx as indeterminate and a Response 4xx as determinate', () => {
    expect(isIndeterminateError(new Response(null, { status: 503 }))).toBe(true)
    expect(isIndeterminateError(new Response(null, { status: 422 }))).toBe(false)
  })

  it('uses the caller fallback for a business error without a usable message', () => {
    expect(describeRequestError({}, '提交失败').message).toBe('提交失败')
    expect(describeRequestError({}, '提交失败').indeterminate).toBe(false)
  })

  it('treats a plain Error as a determinate unknown failure', () => {
    expect(describeRequestError(new Error('boom'))).toMatchObject({
      kind: 'unknown',
      indeterminate: false,
      message: 'boom',
    })
  })

  it('isIndeterminateError is true only for a dispatched-but-unanswered request', () => {
    // Dispatched, outcome unknown → unsafe to blindly retry a non-idempotent write.
    expect(isIndeterminateError(new RequestTimeoutError())).toBe(true)
    // Offline pre-check and explicit application failures are determinate.
    expect(isIndeterminateError(new OfflineError())).toBe(false)
    expect(isIndeterminateError({ message: '业务失败' })).toBe(false)
  })
})

describe('resolveRequestTimeoutMs', () => {
  const envWith = (raw?: string, dev = true) =>
    ({
      DEV: dev,
      ...(raw === undefined ? {} : { VITE_NERV_IIP_REQUEST_TIMEOUT_MS: raw }),
    }) as unknown as ImportMetaEnv

  it('returns the parsed value for a plain positive-integer string in range (DEV-only short ceiling)', () => {
    expect(resolveRequestTimeoutMs(envWith('2000'))).toBe(2000)
    expect(resolveRequestTimeoutMs(envWith(' 1500 '))).toBe(1500)
  })

  it('accepts the clamp-range boundaries [100, 30000]', () => {
    expect(resolveRequestTimeoutMs(envWith('100'))).toBe(100)
    expect(resolveRequestTimeoutMs(envWith('30000'))).toBe(30_000)
  })

  it('falls back to the 30s default when the env var is absent', () => {
    expect(resolveRequestTimeoutMs(envWith())).toBe(REQUEST_TIMEOUT_MS)
  })

  it.each([
    ['a valid short value', '2000'],
    ['the lower clamp boundary', '100'],
    ['the upper clamp boundary', '30000'],
    ['garbage', 'abc'],
  ])(
    'PRODUCTION GATE: falls back unconditionally when DEV !== true, even for %s',
    (_label, raw) => {
      // 生产/APK 构建里 import.meta.env.DEV 被静态替换为 false —— 覆盖通道整体失效，
      // 打包时误带该变量也不可能改动产品 30s 上限。
      expect(resolveRequestTimeoutMs(envWith(raw, false))).toBe(REQUEST_TIMEOUT_MS)
    },
  )

  it.each([
    ['empty string', ''],
    ['whitespace only', '   '],
    ['non-numeric', 'abc'],
    ['zero (would fire instantly)', '0'],
    ['negative', '-5'],
    ['fractional', '1.5'],
    ['scientific notation', '2e3'],
    ['digit separator', '30_000'],
    ['trailing unit', '2000ms'],
    ['signed positive', '+2000'],
    ['below the 100ms floor (instant-abort territory)', '99'],
    ['above the 30s product ceiling', '30001'],
  ])('falls back to the default for %s (a typo must never weaken the ceiling)', (_label, raw) => {
    expect(resolveRequestTimeoutMs(envWith(raw))).toBe(REQUEST_TIMEOUT_MS)
  })

  it('falls back for all-digit values beyond the safe-integer range', () => {
    expect(resolveRequestTimeoutMs(envWith('9007199254740993'))).toBe(REQUEST_TIMEOUT_MS)
  })

  it('defaults to import.meta.env (DEV in vitest, var unset → 30s default)', () => {
    expect(resolveRequestTimeoutMs()).toBe(REQUEST_TIMEOUT_MS)
  })
})
