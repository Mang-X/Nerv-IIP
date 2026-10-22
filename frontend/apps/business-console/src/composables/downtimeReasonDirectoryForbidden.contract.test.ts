import { afterEach, describe, expect, it, vi } from 'vitest'

import {
  configureApiClient,
  listBusinessConsoleSearchableDirectoryQueryOptions,
} from '@nerv-iip/api-client'

import { errorStatusCode, isForbiddenError } from '@/utils/notify'

// #2793：停机页把「没有停机原因词表读权限」说成「组织尚未配置停机原因」。
//
// 改文案之前必须先钉死 **403 到底落在前端的哪一条分支**——页面有两条互斥的降级路径：
//   (a) `downtimeReasonsQuery.error` 有值      → recordEntryBlocker 的「读取失败」分支
//   (b) 200 + 信封 `status === 'unavailable'`  → downtimeReasonOptions 变空 → 「组织尚未配置」分支
// 本文件不桩 `@nerv-iip/api-client`，而是让**真实生成客户端**跑一次真实 fetch 桩，
// 直接观测落点，避免用「我以为客户端会怎么做」的复刻夹具自证。
//
// 桩出的响应体照抄网关实际形状：`BusinessGatewayAuthorization.RequireAnyPermissionAsync`
// 在权限全部不允许时写 `new ResponseData(false, "Forbidden.", 403, [])`，
// 由 `ResponseDataEndpointResults` 落成 JSON —— 不是 ProblemDetails，也不带 HTTP 头以外的状态提示。
//
// 不能证明项：本用例不证明网关真的对缺权限主体回 403（那是后端 lane 的
// `BusinessConsoleSearchableDirectoryWireTests` 在证），只证明「给定一个 403 响应，
// 前端这条通道把它交付成什么」。

const directoryParams = {
  path: { directoryType: 'downtime-reason' },
  query: {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    pageIndex: 1,
    pageSize: 100,
    rankingMode: 'default',
  },
} as const

function jsonResponse(status: number, body: unknown) {
  return new Response(JSON.stringify(body), {
    status,
    headers: { 'Content-Type': 'application/json' },
  })
}

function runDirectoryQuery(response: Response) {
  const fetchStub = vi.fn(async () => response)
  configureApiClient({ baseUrl: 'http://business-gateway.test', fetch: fetchStub as never })
  const options = listBusinessConsoleSearchableDirectoryQueryOptions(directoryParams as never)
  return { fetchStub, result: options.query({} as never) }
}

afterEach(() => {
  configureApiClient()
})

describe('#2793 停机原因目录 403 的前端落点', () => {
  it('把权限不足的 403 交付成 query 失败，并且能被识别为「无权限」', async () => {
    const { fetchStub, result } = runDirectoryQuery(
      jsonResponse(403, {
        success: false,
        message: 'Forbidden.',
        code: 403,
        data: null,
        errorData: [],
      }),
    )

    // 落点 (a)：请求确实发出去了，而且这条通道是 reject 而不是「解析出一个信封」。
    const error = await result.then(
      (data) => {
        throw new Error(`期望 reject，实际 resolve：${JSON.stringify(data)}`)
      },
      (reason: unknown) => reason,
    )
    expect(fetchStub).toHaveBeenCalledTimes(1)

    // 生成客户端在 throwOnError 下抛的是**解析后的响应体**，体里 `code: 403` 不在
    // errorStatusCode 认的字段集里（它只认 status/statusCode）——真正让状态码拿得到的是
    // configureApiClient 的 error 拦截器挂上去的原始 Response。这两件事任缺一件本断言即红。
    expect(errorStatusCode(error)).toBe(403)
    expect(isForbiddenError(error)).toBe(true)
  })

  it('负向对照：同形状的 503 走同一条通道，但不得被认成「无权限」', async () => {
    // 这条对照通过被测路径的其它所有谓词——同一个 queryOptions、同一个客户端、
    // 同一个 `success:false` 信封形状、同样 reject——唯一的差别就是状态码。
    // （503 是网关真实存在的另一支：IAM 不可达时写 "Authorization service unavailable."）
    const { result } = runDirectoryQuery(
      jsonResponse(503, {
        success: false,
        message: 'Authorization service unavailable.',
        code: 503,
        data: null,
        errorData: [],
      }),
    )

    const error = await result.then(
      (data) => {
        throw new Error(`期望 reject，实际 resolve：${JSON.stringify(data)}`)
      },
      (reason: unknown) => reason,
    )
    expect(errorStatusCode(error)).toBe(503)
    expect(isForbiddenError(error)).toBe(false)
  })

  it('落点 (b) 是另一条路：200 + status=unavailable 会正常 resolve，不进失败分支', async () => {
    // 证明两条分支互斥：403 永远走不到「组织尚未配置」那句，所以本票只需要改失败分支的归因。
    const { result } = runDirectoryQuery(
      jsonResponse(200, {
        success: true,
        data: {
          status: 'unavailable',
          reasonCode: 'directory-authority-unconfigured',
          items: [],
          total: 0,
        },
      }),
    )

    await expect(result).resolves.toMatchObject({
      success: true,
      data: { status: 'unavailable' },
    })
  })
})
