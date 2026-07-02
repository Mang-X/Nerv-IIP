import {
  createOrUpdateBusinessConsoleBarcodeRuleMutationOptions,
  createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions,
  listBusinessConsoleBarcodeRulesQueryOptions,
  listBusinessConsoleBarcodeTemplatesQueryOptions,
  type BusinessConsoleBarcodeRuleItem,
  type BusinessConsoleBarcodeRuleListEnvelope,
  type BusinessConsoleBarcodeTemplateItem,
  type BusinessConsoleBarcodeTemplateListEnvelope,
  type CreateOrUpdateBusinessConsoleBarcodeRuleData,
  type CreateOrUpdateBusinessConsoleBarcodeTemplateData,
} from '@nerv-iip/api-client'
import { useMutation, useQuery } from '@pinia/colada'
import { computed, reactive } from 'vue'
import { bindBusinessContext, withBusinessContextEnabled, type BusinessContextFields } from './businessContextBinding'

const DEFAULT_TAKE = 100

export interface BarcodeListFilters extends BusinessContextFields {
  skip: number
  take: number
  status?: string
}

export interface BarcodeRuleFilters extends BarcodeListFilters {
  keyword?: string
}

function defaultFilters<T extends BarcodeListFilters>(initial: Partial<T> = {}): T {
  return bindBusinessContext(reactive({
    organizationId: '',
    environmentId: '',
    skip: 0,
    take: DEFAULT_TAKE,
    ...initial,
  }) as T)
}

function optionalQuery<TKey extends string, TValue>(key: TKey, value: TValue | undefined) {
  return value === undefined || value === '' ? {} : { [key]: value }
}

function ruleItems(envelope: BusinessConsoleBarcodeRuleListEnvelope | undefined) {
  if (!envelope?.success) return []
  return envelope.data?.rules ?? []
}

function templateItems(envelope: BusinessConsoleBarcodeTemplateListEnvelope | undefined) {
  if (!envelope?.success) return []
  return envelope.data?.templates ?? []
}

function total(envelope: { success?: boolean, data?: { total?: number } | null } | undefined) {
  if (!envelope?.success) return 0
  return envelope.data?.total ?? 0
}

export function useBarcodeRules(initialFilters: Partial<BarcodeRuleFilters> = {}) {
  const filters = defaultFilters<BarcodeRuleFilters>(initialFilters)
  const rulesQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleBarcodeRulesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('status', filters.status),
        ...optionalQuery('keyword', filters.keyword),
      },
    }), filters),
  )

  const saveRuleMutation = useMutation({
    ...createOrUpdateBusinessConsoleBarcodeRuleMutationOptions(),
    onSuccess() {
      void rulesQuery.refetch()
    },
  })

  return {
    filters,
    rules: computed<BusinessConsoleBarcodeRuleItem[]>(() => ruleItems(rulesQuery.data.value)),
    rulesError: rulesQuery.error,
    rulesPending: rulesQuery.isLoading,
    rulesTotal: computed(() => total(rulesQuery.data.value)),
    refreshRules: rulesQuery.refetch,
    saveRule: (body: CreateOrUpdateBusinessConsoleBarcodeRuleData['body']) =>
      saveRuleMutation.mutateAsync({ body }),
    saveRulePending: saveRuleMutation.isLoading,
    saveRuleError: saveRuleMutation.error,
  }
}

export function useBarcodeTemplates(initialFilters: Partial<BarcodeListFilters> = {}) {
  const filters = defaultFilters<BarcodeListFilters>(initialFilters)
  const templatesQuery = useQuery(() =>
    withBusinessContextEnabled(listBusinessConsoleBarcodeTemplatesQueryOptions({
      query: {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        skip: filters.skip,
        take: filters.take,
        ...optionalQuery('status', filters.status),
      },
    }), filters),
  )

  const saveTemplateMutation = useMutation({
    ...createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions(),
    onSuccess() {
      void templatesQuery.refetch()
    },
  })

  return {
    filters,
    templates: computed<BusinessConsoleBarcodeTemplateItem[]>(() => templateItems(templatesQuery.data.value)),
    templatesError: templatesQuery.error,
    templatesPending: templatesQuery.isLoading,
    templatesTotal: computed(() => total(templatesQuery.data.value)),
    refreshTemplates: templatesQuery.refetch,
    saveTemplate: (body: CreateOrUpdateBusinessConsoleBarcodeTemplateData['body']) =>
      saveTemplateMutation.mutateAsync({ body }),
    saveTemplatePending: saveTemplateMutation.isLoading,
    saveTemplateError: saveTemplateMutation.error,
  }
}
