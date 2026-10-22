// PublicContract / DomainInvariant：#3656，D 的授权范围与安灯队列契约。
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { fetchAndonQueue, fetchAndonScopes } from '@/data/real/andon'

const api = vi.hoisted(() => ({ context: vi.fn(), queue: vi.fn() }))
vi.mock('@nerv-iip/api-client', () => ({
  getBusinessConsolePrincipalWorkContext: api.context,
  listBusinessConsoleMesAndonCalls: api.queue,
}))
const session = { organizationId: 'org-real', environmentId: 'env-real' }
const scope = { kind: 'workCenter', id: 'wc-real', displayName: '装配一线' }
const ok = (data: unknown) => ({ response: new Response(), data: { success: true, data } })

beforeEach(() => {
  vi.resetAllMocks()
  api.context.mockResolvedValue(ok({ authorizedScopes: [scope], selectedScope: scope }))
  api.queue.mockResolvedValue(ok({ items: [], total: 0 }))
})

describe('真实安灯 fetcher', () => {
  it('只将当前 principal 的读权限范围提供给选择器', async () => {
    expect(await fetchAndonScopes(session)).toEqual([scope])
    expect(api.context.mock.calls[0][0].query).toEqual({
      ...session,
      permissionCode: 'business.mes.operations.read',
    })
  })
  it('验证所选范围后读取未关闭队列并保留服务器升级与响应事实', async () => {
    const call = {
      id: 'call-1',
      status: 'claimed',
      category: 'quality',
      escalatedAtUtc: '2026-09-20T01:00:00Z',
      responseDurationSeconds: 65,
    }
    api.queue.mockResolvedValue(ok({ items: [call], total: 21 }))
    expect(await fetchAndonQueue(session, scope, 1)).toEqual({ items: [call], total: 21 })
    expect(api.context.mock.calls[0][0].query).toEqual({
      ...session,
      permissionCode: 'business.mes.operations.read',
      scopeKind: 'workCenter',
      scopeId: 'wc-real',
    })
    expect(api.queue.mock.calls[0][0].query).toEqual({
      ...session,
      scopeKind: 'workCenter',
      scopeId: 'wc-real',
      queue: 'unclosed',
      skip: 5,
      take: 5,
    })
  })
  it('范围失效时不读取队列', async () => {
    api.context.mockResolvedValue(ok({ selectedScope: null, authorizedScopes: [] }))
    await expect(fetchAndonQueue(session, scope, 0)).rejects.toThrow('未配置真实范围')
    expect(api.queue).not.toHaveBeenCalled()
  })
  it.each([401, 403])('HTTP %i 不是空队列', async (status) => {
    api.context.mockResolvedValue({ response: new Response(null, { status }) })
    await expect(fetchAndonScopes(session)).rejects.toThrow('未授权')
  })
  it('业务失败和缺少数据均不能伪装成无呼叫', async () => {
    api.queue.mockResolvedValue({ response: new Response(), data: { success: false } })
    await expect(fetchAndonQueue(session, scope, 0)).rejects.toThrow('读取失败')
    api.queue.mockResolvedValue(ok(null))
    await expect(fetchAndonQueue(session, scope, 0)).rejects.toThrow('读取失败')
  })
  it('SDK 网络失败没有 Response 时标记失联', async () => {
    api.context.mockResolvedValue({ error: new TypeError('Failed to fetch') })
    await expect(fetchAndonScopes(session)).rejects.toThrow(TypeError)
  })
})
