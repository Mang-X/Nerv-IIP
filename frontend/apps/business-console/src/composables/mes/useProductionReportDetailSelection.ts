import { computed, shallowRef, watch } from 'vue'
import { useRoute } from 'vue-router'
import { firstTraceQuery } from './reportTraceability'
import { traceabilityReturnPath } from './traceWorkOrderLink'

/** 只读详情的选择状态：列表选择和追溯深链共用，来源身份仅绑定原深链报工。 */
export function useProductionReportDetailSelection() {
  const route = useRoute()
  const detailReportNo = shallowRef<string | null>(null)
  const linkedReportNo = computed(() => firstTraceQuery(route.query.reportNo))

  watch(
    () => route.query.reportNo,
    (value) => {
      detailReportNo.value = firstTraceQuery(value) || null
    },
    { immediate: true },
  )

  const detailSource = computed(() => {
    if (detailReportNo.value !== linkedReportNo.value) return { nodeType: '', nodeId: '' }
    return {
      nodeType: firstTraceQuery(route.query.sourceNodeType),
      nodeId: firstTraceQuery(route.query.sourceNodeId),
    }
  })
  const reportReturnTo = computed(() =>
    traceabilityReturnPath(firstTraceQuery(route.query.traceabilityReturn)),
  )

  return { detailReportNo, detailSource, reportReturnTo }
}
