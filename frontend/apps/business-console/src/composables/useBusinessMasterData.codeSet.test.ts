import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref, shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'

import { getBusinessConsoleMasterDataResourceDetail } from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMasterDataResourceActions } from './useBusinessMasterData'

/**
 * 字典条目（reference-data）的身份是 **(codeSet, code) 两段**，只有 `code` 定位不到对象：
 * 后端 `GetMasterDataResourceDetailQueryHandler.RequireReferenceDataCodeSet` 对空 codeSet 直接
 * 拒绝，`SetMasterDataResourceEnabledCommandHandler.LifecycleIdentity` 的 reference-data 分支
 * 拼的也是 `{codeSet}:{code}`。此前动作层从不发 codeSet，于是字典页的停用/启用/编辑回填一律
 * 400（#1593）。
 *
 * 这份契约把「codeSet 必须随请求发出」钉在动作层：页面换个写法、composable 重构，都不该把它
 * 再次静默丢掉。同时反向钉住「没有 codeSet 的资源类型不得凭空多带这个字段」。
 */
const mutationCalls = vi.hoisted(() => ({
  update: [] as unknown[],
  disable: [] as unknown[],
  enable: [] as unknown[],
}))

// 只覆盖本用例要观测的四个导出，其余走真实模块——否则 composable 顶部新增一个 import
// 就会把这份测试打成 "No export is defined on the mock"，而不是测出真问题。
vi.mock('@nerv-iip/api-client', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/api-client')>()),
  updateBusinessConsoleMasterDataResourceMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars: unknown) => {
      mutationCalls.update.push(vars)
      return { success: true }
    }),
  })),
  disableBusinessConsoleMasterDataResourceMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars: unknown) => {
      mutationCalls.disable.push(vars)
      return { success: true }
    }),
  })),
  enableBusinessConsoleMasterDataResourceMutationOptions: vi.fn(() => ({
    mutation: vi.fn(async (vars: unknown) => {
      mutationCalls.enable.push(vars)
      return { success: true }
    }),
  })),
  getBusinessConsoleMasterDataResourceDetail: vi.fn(async () => ({
    data: { success: true, data: { resourceType: 'reference-data', code: 'consumable' } },
  })),
}))

vi.mock('@pinia/colada', async (orig) => ({
  ...(await orig<typeof import('@pinia/colada')>()),
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars) => {
      const result = await options.mutation(vars)
      await options.onSuccess?.(result)
      return result
    }),
  })),
  useQuery: vi.fn(() => ({
    data: shallowRef(undefined),
    error: shallowRef(),
    isLoading: shallowRef(false),
    refetch: vi.fn(),
  })),
  useQueryCache: vi.fn(() => ({ invalidateQueries: vi.fn(async () => undefined) })),
}))

function lastBody(calls: unknown[]) {
  return (calls.at(-1) as { body: Record<string, unknown> }).body
}

beforeEach(() => {
  setActivePinia(createPinia())
  useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
  mutationCalls.update.length = 0
  mutationCalls.disable.length = 0
  mutationCalls.enable.length = 0
  vi.mocked(getBusinessConsoleMasterDataResourceDetail).mockClear()
})

describe('字典条目动作必须携带 codeSet', () => {
  it('停用把 codeSet 一起发出去', async () => {
    const actions = useMasterDataResourceActions('reference-data', 'material-type')
    await actions.disable('consumable', { reason: '该物料类型不再使用' })

    expect(lastBody(mutationCalls.disable)).toMatchObject({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      codeSet: 'material-type',
      reason: '该物料类型不再使用',
    })
  })

  it('启用把 codeSet 一起发出去', async () => {
    const actions = useMasterDataResourceActions('reference-data', 'material-type')
    await actions.enable('consumable', { reason: '恢复该物料类型' })

    expect(lastBody(mutationCalls.enable)).toMatchObject({ codeSet: 'material-type' })
  })

  it('编辑把 codeSet 一起发出去', async () => {
    const actions = useMasterDataResourceActions('reference-data', 'material-type')
    await actions.update('consumable', { name: '辅料消耗品' })

    expect(lastBody(mutationCalls.update)).toMatchObject({ codeSet: 'material-type' })
  })

  it('编辑回填的详情查询带上 codeSet', async () => {
    const actions = useMasterDataResourceActions('reference-data', 'material-type')
    await actions.fetchDetail('consumable')

    expect(getBusinessConsoleMasterDataResourceDetail).toHaveBeenCalledWith({
      path: { resourceType: 'reference-data', code: 'consumable' },
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        codeSet: 'material-type',
      },
    })
  })

  it('codeSet 跟随页面当前选中的分组切换（不是构造时冻结的快照）', async () => {
    const selected = ref('material-type')
    const actions = useMasterDataResourceActions('reference-data', selected)

    await actions.disable('consumable', { reason: '该物料类型不再使用' })
    expect(lastBody(mutationCalls.disable)).toMatchObject({ codeSet: 'material-type' })

    selected.value = 'storage-condition'
    await actions.disable('cold-chain', { reason: '仓储条件调整' })
    expect(lastBody(mutationCalls.disable)).toMatchObject({ codeSet: 'storage-condition' })
  })

  it('没有 codeSet 的资源类型不得凭空多带这个字段', async () => {
    const actions = useMasterDataResourceActions('unit-of-measure')
    await actions.disable('MPa', { reason: '压力单位统一为 kPa' })

    expect(lastBody(mutationCalls.disable)).not.toHaveProperty('codeSet')

    await actions.fetchDetail('MPa')
    expect(getBusinessConsoleMasterDataResourceDetail).toHaveBeenCalledWith({
      path: { resourceType: 'unit-of-measure', code: 'MPa' },
      query: { organizationId: 'org-001', environmentId: 'env-dev' },
    })
  })
})
