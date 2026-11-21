import { useScopeBoundListResponse } from '@nerv-iip/business-core'
import { computed, shallowRef, toValue, watch, type MaybeRefOrGetter } from 'vue'
import { serializeMaintenanceKey } from '../maintenancePublicIds'

let maintenanceQueryInstanceSequence = 0

export function nextMaintenanceQueryInstanceNonce() {
  maintenanceQueryInstanceSequence += 1
  return maintenanceQueryInstanceSequence
}

export interface FreshGenerationValue<TValue> {
  generation: number
  identity: string
  value: TValue
}

export function useMaintenanceQueryGeneration(
  identity: MaybeRefOrGetter<string>,
  enabled: MaybeRefOrGetter<boolean>,
) {
  const generation = shallowRef(0)
  watch(
    [() => toValue(identity), () => toValue(enabled)],
    ([nextIdentity, ready], [previousIdentity, wasReady]) => {
      if (!ready || !wasReady || nextIdentity !== previousIdentity) generation.value += 1
    },
    { flush: 'sync' },
  )
  return generation
}

export function useFreshGenerationProjection<TValue>(
  data: MaybeRefOrGetter<FreshGenerationValue<TValue> | undefined>,
  identity: MaybeRefOrGetter<string>,
  generation: MaybeRefOrGetter<number>,
  enabled: MaybeRefOrGetter<boolean>,
) {
  const generationIdentity = computed(() =>
    toValue(enabled) ? serializeMaintenanceKey([toValue(identity), toValue(generation)]) : '',
  )
  const bound = useScopeBoundListResponse(data, generationIdentity, enabled)
  return computed(() => {
    const result = bound.value
    return result?.identity === toValue(identity) && result.generation === toValue(generation)
      ? result.value
      : undefined
  })
}
