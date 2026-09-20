import { flushPromises, mount } from '@vue/test-utils'
import { PiniaColada } from '@pinia/colada'
import { createPinia } from 'pinia'
import { defineComponent, nextTick } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { useAuthStore } from '@/stores/auth'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMesAndon } from './useMesAndon'

const api = vi.hoisted(() => ({
  list: vi.fn(),
  claim: vi.fn(),
  close: vi.fn(),
  workContext: vi.fn(),
}))
vi.mock('@nerv-iip/api-client', async (original) => ({
  ...(await original<typeof import('@nerv-iip/api-client')>()),
  listBusinessConsoleMesAndonCalls: api.list,
  claimBusinessConsoleMesAndonCall: api.claim,
  closeBusinessConsoleMesAndonCall: api.close,
  getBusinessConsolePrincipalWorkContextQueryOptions: ({
    query,
  }: {
    query: Record<string, string>
  }) => ({
    key: ['andon-work-context', query],
    query: () => api.workContext(query),
  }),
}))

const call = {
  id: 'andon-1',
  status: 'open' as const,
  workOrderId: 'WO-20260920-001',
  operationTaskId: 'WO-20260920-001-OP-20',
}
const envelope = (data: unknown) => ({ data: { success: true, data } })
async function setup(
  permissions = ['business.mes.operations.read', 'business.mes.operations.manage'],
) {
  const pinia = createPinia()
  useAuthStore(pinia).$patch({
    principal: { principalId: 'responder-1', principalType: 'User', permissionCodes: permissions },
  })
  useBusinessContextStore(pinia).patchContext({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
  let result!: ReturnType<typeof useMesAndon>
  const wrapper = mount(
    defineComponent({
      setup() {
        result = useMesAndon()
        return () => null
      },
    }),
    {
      global: { plugins: [pinia, [PiniaColada, { queryOptions: { gcTime: 0 } }]] },
    },
  )
  await flushPromises()
  await flushPromises()
  return { result, wrapper }
}

describe('安灯队列与当前主体动作（#3655 PublicContract / DomainInvariant）', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    api.workContext.mockImplementation(async (query: Record<string, string>) => ({
      success: true,
      data: {
        authorizedScopes: [
          { kind: 'work-center', id: 'WC-A', displayName: '总装一线' },
          { kind: 'work-center', id: 'WC-B', displayName: '总装二线' },
        ],
        selectedScope: query.scopeId ? { kind: query.scopeKind, id: query.scopeId } : null,
      },
    }))
    api.list.mockResolvedValue(envelope({ items: [call], total: 31 }))
    api.claim.mockResolvedValue(
      envelope({ ...call, status: 'claimed', responderId: 'user:responder-1' }),
    )
    api.close.mockResolvedValue(envelope({ ...call, status: 'closed' }))
  })

  it('将队列、类别、工作中心和分页交给服务端，total 不取当前页长度', async () => {
    const { result, wrapper } = await setup()
    Object.assign(result.filters, {
      queue: 'unclosed',
      category: 'equipment',
      workCenterId: 'WC-ASSEMBLY',
      skip: 20,
      take: 10,
    })
    await nextTick()
    await flushPromises()
    expect(api.list).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: {
          organizationId: 'org-001',
          environmentId: 'env-dev',
          queue: 'unclosed',
          category: 'equipment',
          workCenterId: 'WC-ASSEMBLY',
          skip: 20,
          take: 10,
          scopeKind: 'work-center',
          scopeId: 'WC-A',
        },
      }),
    )
    expect(result.total.value).toBe(31)
    expect(result.items.value).toHaveLength(1)
    wrapper.unmount()
  })

  it('读失败保留错误，手动刷新可以恢复', async () => {
    api.list.mockRejectedValueOnce({ status: 503 })
    const { result, wrapper } = await setup()
    expect(result.error.value).toBeTruthy()
    await result.refresh()
    await flushPromises()
    expect(result.error.value).toBeNull()
    expect(result.total.value).toBe(31)
    wrapper.unmount()
  })

  it('HTTP 成功中的业务失败不能冒充空队列', async () => {
    api.list.mockResolvedValue({ data: { success: false, message: '范围无权读取' } })
    const { result, wrapper } = await setup()
    expect(result.error.value).toBeTruthy()
    wrapper.unmount()
  })

  it('认领只传当前上下文与幂等键，成功后读回队列', async () => {
    const { result, wrapper } = await setup()
    await result.act(call, 'claim')
    expect(api.claim).toHaveBeenCalledWith({
      path: { id: 'andon-1' },
      body: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        idempotencyKey: expect.any(String),
        scopeKind: 'work-center',
        scopeId: 'WC-A',
      },
      throwOnError: true,
    })
    expect(api.list).toHaveBeenCalledTimes(2)
    wrapper.unmount()
  })

  it('另一主体认领冲突会抛出失败并刷新权威响应人', async () => {
    const { result, wrapper } = await setup()
    api.claim.mockRejectedValue({ status: 409, message: '呼叫已由其他人员认领' })
    api.list.mockResolvedValue(
      envelope({
        items: [{ ...call, status: 'claimed', responderId: 'user:responder-2' }],
        total: 1,
      }),
    )
    await expect(result.act(call, 'claim')).rejects.toMatchObject({ status: 409 })
    expect(result.items.value[0]?.responderId).toBe('user:responder-2')
    wrapper.unmount()
  })

  it('无管理权限和他人认领的呼叫都不能发出关闭请求', async () => {
    const { result, wrapper } = await setup(['business.mes.operations.read'])
    await expect(result.act(call, 'claim')).rejects.toThrow()
    await expect(
      result.act({ ...call, status: 'claimed', responderId: 'user:responder-2' }, 'close'),
    ).rejects.toThrow()
    expect(api.claim).not.toHaveBeenCalled()
    expect(api.close).not.toHaveBeenCalled()
    wrapper.unmount()
  })

  it('只有当前认领人可以关闭；业务失败不作为成功返回', async () => {
    const { result, wrapper } = await setup()
    expect(
      result.canAct({ ...call, status: 'claimed', responderId: 'user:responder-2' }, 'close'),
    ).toBe(false)
    const mine = { ...call, status: 'claimed' as const, responderId: 'user:responder-1' }
    expect(result.canAct(mine, 'close')).toBe(true)
    api.close.mockResolvedValue({ data: { success: false, message: '当前呼叫不能关闭' } })
    await expect(result.act(mine, 'close')).rejects.toMatchObject({ message: '当前呼叫不能关闭' })
    wrapper.unmount()
  })

  it('多授权范围选择后列表、认领和本人关闭都携带同一工作中心范围', async () => {
    const { result, wrapper } = await setup()
    result.scope.scopeSelectionValue.value = 'work-center:WC-B'
    await flushPromises()
    await flushPromises()
    expect(api.list).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({ scopeKind: 'work-center', scopeId: 'WC-B' }),
      }),
    )
    await result.act(call, 'claim')
    await result.act({ ...call, status: 'claimed', responderId: 'user:responder-1' }, 'close')
    for (const command of [api.claim, api.close]) {
      expect(command).toHaveBeenLastCalledWith(
        expect.objectContaining({
          body: expect.objectContaining({ scopeKind: 'work-center', scopeId: 'WC-B' }),
        }),
      )
    }
    wrapper.unmount()
  })

  it('班组读取与工作中心管理分别授权同一呼叫时仍可认领和本人关闭', async () => {
    api.workContext.mockImplementation(async (query: Record<string, string>) => {
      const authorized =
        query.permissionCode === 'business.mes.operations.read'
          ? { kind: 'team', id: 'TEAM-A', displayName: '总装一班' }
          : { kind: 'work-center', id: 'WC-A', displayName: '总装一线' }
      return {
        success: true,
        data: { authorizedScopes: [authorized], selectedScope: authorized },
      }
    })
    const { result, wrapper } = await setup()
    expect(api.list).toHaveBeenLastCalledWith(
      expect.objectContaining({
        query: expect.objectContaining({ scopeKind: 'team', scopeId: 'TEAM-A' }),
      }),
    )
    expect(result.items.value[0]?.id).toBe('andon-1')
    expect(result.canAct(call, 'claim')).toBe(true)
    await result.act(call, 'claim')
    const mine = { ...call, status: 'claimed' as const, responderId: 'user:responder-1' }
    expect(result.canAct(mine, 'close')).toBe(true)
    await result.act(mine, 'close')
    for (const command of [api.claim, api.close]) {
      expect(command).toHaveBeenLastCalledWith(
        expect.objectContaining({
          path: { id: 'andon-1' },
          body: expect.objectContaining({ scopeKind: 'work-center', scopeId: 'WC-A' }),
        }),
      )
    }
    wrapper.unmount()
  })
})
