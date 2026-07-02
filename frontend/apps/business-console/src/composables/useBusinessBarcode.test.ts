import { beforeEach, describe, expect, it, vi } from 'vitest'
import { shallowRef } from 'vue'

import {
  createOrUpdateBusinessConsoleBarcodeRuleMutationOptions,
  createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions,
  listBusinessConsoleBarcodeRulesQueryOptions,
  listBusinessConsoleBarcodeTemplatesQueryOptions,
} from '@nerv-iip/api-client'
import { useBarcodeRules, useBarcodeTemplates } from './useBusinessBarcode'

const coladaState = vi.hoisted(() => ({
  queryDataById: new Map<string, unknown>(),
  mutations: [] as ReturnType<typeof vi.fn>[],
}))

vi.mock('@/composables/businessContextBinding', () => ({
  bindBusinessContext: <T extends { organizationId: string, environmentId: string }>(filters: T) => {
    filters.organizationId = 'org-001'
    filters.environmentId = 'env-dev'
    return filters
  },
  withBusinessContextEnabled: (options: object) => options,
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleBarcodeRulesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodeRules' }],
    query: vi.fn(),
  })),
  listBusinessConsoleBarcodeTemplatesQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleBarcodeTemplates' }],
    query: vi.fn(),
  })),
  createOrUpdateBusinessConsoleBarcodeRuleMutationOptions: vi.fn(() => ({})),
  createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions: vi.fn(() => ({})),
}))

vi.mock('@pinia/colada', () => ({
  useQuery: vi.fn((optionsFactory) => {
    const options = optionsFactory()
    const key = Array.isArray(options.key) ? options.key[0] : undefined
    const id = key && typeof key === 'object' && '_id' in key ? String(key._id) : ''

    return {
      data: shallowRef(coladaState.queryDataById.get(id)),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useMutation: vi.fn(() => {
    const mutateAsync = vi.fn().mockResolvedValue({ data: { id: 'ok' } })
    coladaState.mutations.push(mutateAsync)
    return {
      mutateAsync,
      isLoading: shallowRef(false),
      error: shallowRef(),
    }
  }),
  useQueryCache: vi.fn(() => ({ invalidateQueries: vi.fn().mockResolvedValue(undefined) })),
}))

describe('business barcode composables', () => {
  beforeEach(() => {
    vi.clearAllMocks()
    coladaState.queryDataById.clear()
    coladaState.mutations.length = 0
  })

  it('lists barcode rules with context, status, keyword, paging, and total', () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodeRules', {
      success: true,
      data: {
        total: 2,
        rules: [
          { barcodeRuleId: 'rule-1', ruleCode: 'GS1-CASE', barcodeType: 'gs1-128', status: 'active' },
        ],
      },
    })

    const result = useBarcodeRules({ status: 'active', keyword: 'GS1', skip: 20, take: 20 })

    expect(listBusinessConsoleBarcodeRulesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 20,
        take: 20,
        status: 'active',
        keyword: 'GS1',
      },
    })
    expect(result.rules.value).toEqual([
      { barcodeRuleId: 'rule-1', ruleCode: 'GS1-CASE', barcodeType: 'gs1-128', status: 'active' },
    ])
    expect(result.rulesTotal.value).toBe(2)
  })

  it('creates or updates a barcode rule through the stable mutation options', async () => {
    const result = useBarcodeRules()

    await result.saveRule({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      ruleCode: 'GS1-CASE',
      barcodeType: 'gs1-128',
      prefix: '0691234',
      length: 18,
      checksumRule: 'gs1-mod10',
      allowedSourceDocumentTypes: ['inventory.receipt'],
      status: 'active',
      gs1CompanyPrefixLength: 7,
    })

    expect(createOrUpdateBusinessConsoleBarcodeRuleMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      body: expect.objectContaining({
        ruleCode: 'GS1-CASE',
        gs1CompanyPrefixLength: 7,
      }),
    })
  })

  it('lists and saves label templates through the barcode facade', async () => {
    coladaState.queryDataById.set('listBusinessConsoleBarcodeTemplates', {
      success: true,
      data: {
        total: 1,
        templates: [
          { templateId: 'tpl-1', templateCode: 'SKU_BOX', templateName: '外箱标签', status: 'active' },
        ],
      },
    })

    const result = useBarcodeTemplates({ status: 'active', skip: 10, take: 10 })

    expect(listBusinessConsoleBarcodeTemplatesQueryOptions).toHaveBeenCalledWith({
      query: {
        organizationId: 'org-001',
        environmentId: 'env-dev',
        skip: 10,
        take: 10,
        status: 'active',
      },
    })
    expect(result.templates.value).toHaveLength(1)
    expect(result.templatesTotal.value).toBe(1)

    await result.saveTemplate({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      templateCode: 'SKU_BOX',
      templateName: '外箱标签',
      templateFileId: 'file-label-box',
      variableSchemaJson: '{"fields":["skuCode","lotNo"]}',
      status: 'active',
    })

    expect(createOrUpdateBusinessConsoleBarcodeTemplateMutationOptions).toHaveBeenCalled()
    expect(coladaState.mutations[0]).toHaveBeenCalledWith({
      body: expect.objectContaining({ templateCode: 'SKU_BOX', templateFileId: 'file-label-box' }),
    })
  })
})
