import type {
  BusinessConsoleMrpPeggingItem,
  BusinessConsoleMrpRunItem,
  BusinessConsolePlanningSuggestionItem,
} from '@nerv-iip/api-client'
import type { useBusinessPlanning } from '@/composables/planning/useBusinessPlanning'
import { isForbiddenError } from '@/utils/notify'
import { computed, shallowRef, watch } from 'vue'
import { hasBusinessContext } from '@/composables/shared/businessContextBinding'
import type { RouteLocationNormalizedLoaded, Router } from 'vue-router'
import type { Ref } from 'vue'

export function usePlanningSuggestionLocation(
  {
    mrpRuns,
    suggestions,
    suggestionFilters,
    suggestionTypeFilter,
    mrpRunsError,
    suggestionsError,
    demandsError,
    runRequest,
    runSelection,
  }: Pick<
    ReturnType<typeof useBusinessPlanning>,
    | 'mrpRuns'
    | 'suggestions'
    | 'suggestionFilters'
    | 'suggestionTypeFilter'
    | 'mrpRunsError'
    | 'suggestionsError'
    | 'demandsError'
    | 'runRequest'
    | 'runSelection'
  >,
  {
    route,
    router,
    activeTab,
    focusedSuggestionId,
    defaultTab,
    sourceOrder,
    canReadMrp,
    canReadMps,
    canReadDemands,
    canReadDeliveries,
    latestCompletedRun,
  }: {
    route: RouteLocationNormalizedLoaded
    router: Router
    activeTab: Ref<string>
    focusedSuggestionId: Ref<string>
    defaultTab: Readonly<Ref<string>>
    sourceOrder: Readonly<Ref<string>>
    canReadMrp: Readonly<Ref<boolean>>
    canReadMps: Readonly<Ref<boolean>>
    canReadDemands: Readonly<Ref<boolean>>
    canReadDeliveries: Readonly<Ref<boolean>>
    latestCompletedRun: Readonly<Ref<BusinessConsoleMrpRunItem | null>>
  },
) {
  const manualSuggestionRunId = shallowRef('latest')
  const suggestionRunChoice = computed({
    get: () =>
      mrpRuns.value.some((run) => run.runId === manualSuggestionRunId.value)
        ? manualSuggestionRunId.value
        : 'latest',
    set: (runId: string) => {
      if (sourceSuggestionId.value || sourceRunId.value) {
        const nextRunId = runId === 'latest' ? latestCompletedRun.value?.runId : runId
        if (nextRunId)
          void router.push({ query: { ...route.query, suggestionId: undefined, runId: nextRunId } })
        return
      }
      manualSuggestionRunId.value = runId
      focusedSuggestionId.value = ''
    },
  })
  const suggestionRun = computed(() =>
    suggestionRunChoice.value === 'latest'
      ? latestCompletedRun.value
      : (mrpRuns.value.find((run) => run.runId === suggestionRunChoice.value) ?? null),
  )
  const scopedSuggestions = computed(() =>
    suggestions.value.filter((item) => item.runId === suggestionRun.value?.runId),
  )
  function locatePlanningSuggestion(suggestionId: string, runId: string) {
    manualSuggestionRunId.value = runId
    suggestionFilters.status = 'all'
    suggestionTypeFilter.type = 'all'
    focusedSuggestionId.value = suggestionId
    activeTab.value = 'suggestions'
  }
  // 来源强 ID 来自工单 sourcePlanReference；运行从实际建议读回，不猜最近批次。
  const sourceSuggestionId = computed(() =>
    typeof route.query.suggestionId === 'string' ? route.query.suggestionId.trim() : '',
  )
  const sourceRunId = computed(() =>
    typeof route.query.runId === 'string' ? route.query.runId.trim() : '',
  )
  watch(
    [
      sourceSuggestionId,
      sourceRunId,
      sourceOrder,
      defaultTab,
      canReadMrp,
      canReadMps,
      canReadDeliveries,
    ],
    ([suggestionId, runId]) => {
      suggestionFilters.status = 'all'
      suggestionTypeFilter.type = 'all'
      focusedSuggestionId.value = ''
      activeTab.value =
        (suggestionId || runId) && canReadMrp.value ? 'suggestions' : defaultTab.value
      manualSuggestionRunId.value = runId || 'latest'
    },
    { immediate: true },
  )
  watch(
    [
      sourceSuggestionId,
      sourceRunId,
      suggestions,
      mrpRuns,
      defaultTab,
      canReadMrp,
      canReadMps,
      canReadDeliveries,
    ],
    ([suggestionId, runId]) => {
      if (!canReadMrp.value || !suggestionId || focusedSuggestionId.value === suggestionId) return
      const source = suggestions.value.find((row) => row.suggestionId === suggestionId)
      if (!source?.runId || (runId && source.runId !== runId)) return
      if (!mrpRuns.value.some((run) => run.runId === source.runId)) return
      locatePlanningSuggestion(suggestionId, source.runId)
    },
    { immediate: true },
  )
  const deniedSource = computed(() => {
    if (
      (sourceSuggestionId.value || sourceRunId.value) &&
      (!canReadMrp.value ||
        isForbiddenError(mrpRunsError.value) ||
        isForbiddenError(suggestionsError.value))
    )
      return '当前岗位没有来源建议或运行的读取权限。'
    if (sourceOrder.value && (!canReadDemands.value || isForbiddenError(demandsError.value)))
      return '当前岗位没有来源需求的读取权限。'
    return ''
  })
  const returnLabel = computed(() =>
    defaultTab.value === 'demands' ? '返回需求池' : '返回岗位入口',
  )
  const sourceUnresolved = computed(() =>
    Boolean(
      ((sourceSuggestionId.value || sourceRunId.value) &&
        (mrpRunsError.value || suggestionsError.value || !hasBusinessContext(runRequest))) ||
      (sourceSuggestionId.value &&
        (suggestionFilters.status === 'all' ||
          focusedSuggestionId.value !== sourceSuggestionId.value) &&
        !suggestions.value.some(
          (row) =>
            row.suggestionId === sourceSuggestionId.value &&
            row.runId === suggestionRun.value?.runId,
        )) ||
      (sourceRunId.value && sourceRunId.value !== suggestionRun.value?.runId),
    ),
  )
  function navigateToSuggestion(suggestionId: string, runId: string) {
    if (sourceSuggestionId.value || sourceRunId.value) {
      void router.push({ query: { ...route.query, suggestionId, runId } })
    }
    locatePlanningSuggestion(suggestionId, runId)
  }
  function focusSuggestion(row: BusinessConsoleMrpPeggingItem) {
    if (!row.suggestionId) return
    navigateToSuggestion(row.suggestionId, runSelection.runId)
  }
  function focusPegging(row: BusinessConsolePlanningSuggestionItem) {
    if (!row.suggestionId || !row.runId) return
    runSelection.runId = row.runId
    focusedSuggestionId.value = row.suggestionId
    activeTab.value = 'runs'
  }

  return {
    suggestionRunChoice,
    scopedSuggestions,
    deniedSource,
    returnLabel,
    sourceUnresolved,
    focusSuggestion,
    focusPegging,
    navigateToSuggestion,
  }
}
