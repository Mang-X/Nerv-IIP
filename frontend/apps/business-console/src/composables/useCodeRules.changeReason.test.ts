import type {
  BusinessConsoleCreateCodeRuleVersionRequest,
  createBusinessConsoleCodeRuleVersionMutationOptions,
} from '@nerv-iip/api-client'
import { createPinia, setActivePinia } from 'pinia'
import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import { useBusinessContextStore } from '@/stores/businessContext'
import { useCodeRules } from './useCodeRules'

type CreateVersionVariables = Parameters<
  ReturnType<typeof createBusinessConsoleCodeRuleVersionMutationOptions>['mutation']
>[0]

const stub = vi.hoisted(() => ({
  createVersion: vi.fn(async (_variables: CreateVersionVariables) => ({ success: true })),
  refetch: vi.fn(async () => undefined),
}))

vi.mock('@nerv-iip/api-client', async (orig) => ({
  ...(await orig<typeof import('@nerv-iip/api-client')>()),
  createBusinessConsoleCodeRuleVersionMutationOptions: vi.fn(() => ({
    mutation: stub.createVersion,
  })),
}))

vi.mock('@pinia/colada', async (orig) => ({
  ...(await orig<typeof import('@pinia/colada')>()),
  useMutation: vi.fn(
    (options: {
      mutation: (variables: CreateVersionVariables) => Promise<unknown>
      onSuccess?: () => Promise<unknown>
    }) => ({
      isLoading: shallowRef(false),
      mutateAsync: vi.fn(async (variables: CreateVersionVariables) => {
        const result = await options.mutation(variables)
        await options.onSuccess?.()
        return result
      }),
    }),
  ),
  useQuery: vi.fn(() => ({
    data: shallowRef(undefined),
    error: shallowRef(undefined),
    isLoading: shallowRef(false),
    refetch: stub.refetch,
  })),
}))

beforeEach(() => {
  setActivePinia(createPinia())
  useBusinessContextStore().patchContext({
    organizationId: 'org-001',
    environmentId: 'env-dev',
  })
  stub.createVersion.mockClear()
  stub.refetch.mockClear()
})

describe('编码规则版本 composable 的变更原因契约', () => {
  it('通过生成 mutation 的类型边界原样传递必填原因', async () => {
    const body: BusinessConsoleCreateCodeRuleVersionRequest = {
      organizationId: 'org-001',
      environmentId: 'env-dev',
      displayName: 'SKU 编码规则',
      scope: 'organization',
      segments: [],
      createdBy: 'user-admin',
      changeReason: '统一物料标签编码',
    }

    await useCodeRules().createRuleVersion('sku', body)

    expect(stub.createVersion).toHaveBeenCalledWith({
      path: { ruleKey: 'sku' },
      body,
    })
    expect(stub.refetch).toHaveBeenCalledTimes(1)
  })
})

type CodeRules = ReturnType<typeof useCodeRules>
declare const createRuleVersion: CodeRules['createRuleVersion']

  // eslint-disable-next-line @typescript-eslint/no-unused-expressions
;() => {
  void createRuleVersion('sku', {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    displayName: 'SKU 编码规则',
    segments: [],
    createdBy: 'user-admin',
    changeReason: '统一物料标签编码',
  })

  // @ts-expect-error 手写 composable 边界不得允许省略变更原因。
  void createRuleVersion('sku', {
    organizationId: 'org-001',
    environmentId: 'env-dev',
    displayName: 'SKU 编码规则',
    segments: [],
    createdBy: 'user-admin',
  })
}
