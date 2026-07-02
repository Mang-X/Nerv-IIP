/**
 * 编码规则治理 composable（#395 facade，真实接线）。
 *
 * 契约（以生成层为准）：
 * - list `{ rules }`（无分页，一次取全），CodeRuleItem 已内含 segments。
 * - get-by-id `{ rule, versions }`（版本历史）。
 * - preview：给 segments（+可选字段值）→ 返回 sampleCode（样例编码）。
 * - create-version：版本化配置，需 createdBy + changeReason；不影响历史已分配编码。
 */
import {
  createBusinessConsoleCodeRuleVersionMutationOptions,
  getBusinessConsoleCodeRule,
  listBusinessConsoleCodeRulesQueryOptions,
  previewBusinessConsoleCodeRule,
  type BusinessConsoleCodeRuleDetailEnvelope,
  type BusinessConsoleCodeRuleDetailResponse,
  type BusinessConsoleCodeRuleItem,
  type BusinessConsoleCodeRuleListEnvelope,
  type BusinessConsoleCodeRulePreviewEnvelope,
  type BusinessConsoleCodeRuleSegment,
  type BusinessConsoleCreateCodeRuleVersionRequest,
} from '@nerv-iip/api-client'
import { useBusinessContextStore } from '@/stores/businessContext'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { bindBusinessContext, withBusinessContextEnabled } from './businessContextBinding'

export function useCodeRules() {
  const context = useBusinessContextStore()
  const filters = bindBusinessContext(reactive({
    organizationId: context.organizationId,
    environmentId: context.environmentId,
  }))

  const listQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleCodeRulesQueryOptions({
      query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
    }), filters),
  )
  const refresh = () => listQuery.refetch()

  const createMutation = useMutation({
    ...createBusinessConsoleCodeRuleVersionMutationOptions(),
    onSuccess: refresh,
  })

  return {
    filters,
    rules: computed<BusinessConsoleCodeRuleItem[]>(() => {
      const env = listQuery.data.value as BusinessConsoleCodeRuleListEnvelope | undefined
      return env?.success ? env.data?.rules ?? [] : []
    }),
    rulesError: listQuery.error,
    rulesPending: listQuery.isLoading,
    refresh,

    // 详情（含版本历史）：按需拉取。
    async fetchRuleDetail(ruleKey: string): Promise<BusinessConsoleCodeRuleDetailResponse | undefined> {
      const res = await getBusinessConsoleCodeRule({
        path: { ruleKey },
        query: { organizationId: filters.organizationId, environmentId: filters.environmentId },
      })
      const env = (res as { data?: BusinessConsoleCodeRuleDetailEnvelope }).data
      return env?.success ? env.data ?? undefined : undefined
    },

    // 预览：按当前 segments 生成一个样例编码（验证格式用）。
    async previewCode(
      ruleKey: string,
      segments: BusinessConsoleCodeRuleSegment[],
      fields?: Record<string, string>,
    ): Promise<string | undefined> {
      const res = await previewBusinessConsoleCodeRule({
        path: { ruleKey },
        body: {
          organizationId: filters.organizationId,
          environmentId: filters.environmentId,
          segments,
          fields: fields ?? null,
        },
      })
      const env = (res as { data?: BusinessConsoleCodeRulePreviewEnvelope }).data
      return env?.success ? env.data?.sampleCode ?? undefined : undefined
    },

    // 新建版本（版本化配置发布）。
    createRuleVersion: (ruleKey: string, body: BusinessConsoleCreateCodeRuleVersionRequest) =>
      (createMutation.mutateAsync as unknown as (vars: unknown) => Promise<unknown>)({
        path: { ruleKey },
        body,
      }),
    createPending: createMutation.isLoading,
  }
}
