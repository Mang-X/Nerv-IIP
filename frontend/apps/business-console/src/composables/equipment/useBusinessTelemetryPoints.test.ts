import { beforeEach, describe, expect, it, vi } from 'vitest'
import { ref, shallowRef } from 'vue'
import { createPinia, setActivePinia } from 'pinia'
import { listBusinessConsoleTelemetryTagsQueryOptions } from '@nerv-iip/api-client'

import { useBusinessContextStore } from '@/stores/businessContext'
import { useBusinessTelemetryPoints } from './useBusinessTelemetryPoints'

const coladaState = vi.hoisted(() => ({
  mutationVars: [] as Array<{ body: Record<string, unknown> }>,
  queryOptions: [] as Array<() => { enabled: boolean }>,
  invalidated: [] as Array<{ predicate: (entry: { key: unknown }) => boolean }>,
  listData: undefined as unknown,
}))

vi.mock('@nerv-iip/api-client', () => ({
  listBusinessConsoleTelemetryTagsQueryOptions: vi.fn(() => ({
    key: [{ _id: 'listBusinessConsoleTelemetryTags' }],
    query: vi.fn(),
  })),
  createOrUpdateBusinessConsoleTelemetryTagMutationOptions: vi.fn(() => ({
    key: [{ _id: 'createOrUpdateBusinessConsoleTelemetryTag' }],
    mutation: vi.fn(),
  })),
  disableBusinessConsoleTelemetryTagMutationOptions: vi.fn(() => ({
    key: [{ _id: 'disableBusinessConsoleTelemetryTag' }],
    mutation: vi.fn(),
  })),
}))

vi.mock('@pinia/colada', () => ({
  useMutation: vi.fn((options) => ({
    error: shallowRef(),
    isLoading: shallowRef(false),
    mutateAsync: vi.fn(async (vars: { body: Record<string, unknown> }) => {
      coladaState.mutationVars.push(vars)
      await options.onSuccess?.()
      return { success: true, data: { telemetryTagId: 'tag-1' } }
    }),
  })),
  useQuery: vi.fn((options: () => { enabled: boolean }) => {
    coladaState.queryOptions.push(options)
    return {
      data: shallowRef(coladaState.listData),
      error: shallowRef(),
      isLoading: shallowRef(false),
      refetch: vi.fn(),
    }
  }),
  useQueryCache: vi.fn(() => ({
    invalidateQueries: vi.fn((filter) => coladaState.invalidated.push(filter)),
  })),
}))

describe('useBusinessTelemetryPoints', () => {
  beforeEach(() => {
    setActivePinia(createPinia())
    coladaState.mutationVars = []
    coladaState.queryOptions = []
    coladaState.invalidated = []
    coladaState.listData = undefined
    vi.mocked(listBusinessConsoleTelemetryTagsQueryOptions).mockClear()
    useBusinessContextStore().patchContext({ organizationId: 'org-001', environmentId: 'env-dev' })
  })

  it('reads every point of the device including disabled ones', () => {
    useBusinessTelemetryPoints(ref(' EQ00001 '))
    const options = coladaState.queryOptions[0]!()

    expect(options.enabled).toBe(true)
    expect(vi.mocked(listBusinessConsoleTelemetryTagsQueryOptions)).toHaveBeenCalledWith({
      query: expect.objectContaining({
        organizationId: 'org-001',
        environmentId: 'env-dev',
        deviceAssetId: 'EQ00001',
        includeDisabled: true,
      }),
    })
  })

  it('does not query without a device', () => {
    useBusinessTelemetryPoints(ref(''))

    expect(coladaState.queryOptions[0]!().enabled).toBe(false)
  })

  it('sorts enabled points first and keeps disabled ones visible', () => {
    coladaState.listData = {
      data: {
        items: [
          { tagKey: 'a_old', isEnabled: false },
          { tagKey: 'z_count', isEnabled: true },
          { tagKey: 'b_speed', isEnabled: true },
        ],
      },
    }
    const { points } = useBusinessTelemetryPoints(ref('EQ00001'))

    expect(points.value.map((point) => point.tagKey)).toEqual(['b_speed', 'z_count', 'a_old'])
  })

  it('saves with the full control payload and refreshes every point catalog', async () => {
    const { savePoint } = useBusinessTelemetryPoints(ref('EQ00001'))

    await savePoint({
      tagKey: 'spindle.speed',
      displayName: '主轴转速',
      valueType: 'number',
      unitCode: 'rpm',
      samplingPolicy: 'sample-10s',
      isWritable: true,
      controlMinValue: 100,
      controlMaxValue: 3000,
      controlAllowedValues: ['1500'],
    })

    expect(coladaState.mutationVars[0]!.body).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      deviceAssetId: 'EQ00001',
      tagKey: 'spindle.speed',
      displayName: '主轴转速',
      valueType: 'number',
      unitCode: 'rpm',
      samplingPolicy: 'sample-10s',
      isWritable: true,
      controlMinValue: 100,
      controlMaxValue: 3000,
      controlAllowedValues: ['1500'],
    })
    const predicate = coladaState.invalidated[0]!.predicate
    expect(predicate({ key: [{ _id: 'listBusinessConsoleTelemetryTags' }] })).toBe(true)
    expect(predicate({ key: [{ _id: 'listBusinessConsoleTelemetryAlarmRules' }] })).toBe(false)
  })

  it('disables by device and code and refreshes every point catalog', async () => {
    const { disablePoint } = useBusinessTelemetryPoints(ref('EQ00001'))

    await disablePoint('parts_count')

    expect(coladaState.mutationVars[0]!.body).toEqual({
      organizationId: 'org-001',
      environmentId: 'env-dev',
      deviceAssetId: 'EQ00001',
      tagKey: 'parts_count',
    })
    expect(coladaState.invalidated).toHaveLength(1)
  })
})
