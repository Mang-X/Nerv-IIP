<script setup lang="ts">
import {
  listBusinessConsolePlanningDemands,
  type BusinessConsoleMesWorkOrderDetailResponse,
} from '@nerv-iip/api-client'
import { useQuery } from '@pinia/colada'
import { computed } from 'vue'
import { NvButton } from '@nerv-iip/ui'
import { useAuthStore } from '@/stores/auth'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { assertEnvelopeSuccess } from '@/composables/serviceEnvelope'
import { inlineErrorMessage } from '@/utils/notify'

const props = defineProps<{
  source?: BusinessConsoleMesWorkOrderDetailResponse['sourcePlanReference']
  organizationId: string
  environmentId: string
}>()
const auth = useAuthStore()
const canReadDemands = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.planningDemandsRead),
)
const demandReference = computed(() => props.source?.sourceDemandReference ?? '')
const { data, error, isPending, refetch } = useQuery(() => ({
  key: ['mes-work-order-source', props.organizationId, props.environmentId, demandReference.value],
  enabled:
    canReadDemands.value &&
    Boolean(props.organizationId && props.environmentId && demandReference.value),
  query: async () => {
    const response = await listBusinessConsolePlanningDemands({
      query: {
        organizationId: props.organizationId,
        environmentId: props.environmentId,
        keyword: demandReference.value,
      },
      throwOnError: true,
    })
    return assertEnvelopeSuccess(response.data!, '读取工单来源需求失败。')
  },
}))
const relatedDemands = computed(() =>
  canReadDemands.value
    ? (data.value?.data?.items ?? []).filter(
        (item) => item.sourceReference === demandReference.value,
      )
    : [],
)
const sourceLabel = computed(() =>
  props.source?.sourceDocumentType === 'PlanningSuggestion' ? '来源建议' : '来源计划',
)
const errorMessage = computed(() => inlineErrorMessage(error.value, '读取工单来源需求失败。'))
</script>

<template>
  <section class="grid gap-3 rounded-lg border p-3" data-testid="work-order-source-facts">
    <h3 class="text-sm font-semibold">工单来源</h3>
    <dl v-if="source" class="grid gap-2 text-sm sm:grid-cols-2">
      <div v-if="source.sourceDocumentId" class="grid gap-1">
        <dt class="text-xs text-muted-foreground">{{ sourceLabel }}</dt>
        <dd class="break-all font-medium">{{ source.sourceDocumentId }}</dd>
      </div>
      <div v-if="demandReference" class="grid gap-1">
        <dt class="text-xs text-muted-foreground">来源需求</dt>
        <dd class="break-all font-medium">{{ demandReference }}</dd>
      </div>
    </dl>
    <p v-else class="text-sm text-muted-foreground">未记录来源计划或需求。</p>
    <template v-if="demandReference">
      <p v-if="!canReadDemands" class="text-sm text-muted-foreground">
        没有需求读取权限，无法查看关联来源详情。
      </p>
      <div v-else-if="error" class="flex items-center gap-2 text-sm text-destructive" role="alert">
        {{ errorMessage }}
        <NvButton size="sm" variant="outline" @click="refetch()">重试</NvButton>
      </div>
      <p v-else-if="isPending" class="text-sm text-muted-foreground" role="status">
        正在读取关联需求…
      </p>
      <div v-else-if="relatedDemands.length" class="grid gap-2">
        <dl
          v-for="demand in relatedDemands"
          :key="demand.demandSourceId ?? demand.sourceLineReference"
          class="grid gap-2 rounded-md bg-muted/30 p-3 text-sm sm:grid-cols-2"
        >
          <div class="grid gap-1">
            <dt class="text-xs text-muted-foreground">
              {{ demand.demandType === 'sales-order' ? '销售订单' : '需求来源' }}
            </dt>
            <dd class="font-medium">
              {{ demand.sourceReference
              }}<span v-if="demand.sourceLineReference">
                · 行 {{ demand.sourceLineReference }}</span
              >
            </dd>
          </div>
          <div v-if="demand.customerCode" class="grid gap-1">
            <dt class="text-xs text-muted-foreground">客户</dt>
            <dd>{{ demand.customerCode }}</dd>
          </div>
          <div v-if="demand.skuCode" class="grid gap-1">
            <dt class="text-xs text-muted-foreground">需求物料</dt>
            <dd>{{ demand.skuCode }}</dd>
          </div>
          <div v-if="demand.quantity != null" class="grid gap-1">
            <dt class="text-xs text-muted-foreground">需求数量</dt>
            <dd class="tabular-nums">{{ demand.quantity }} {{ demand.uomCode }}</dd>
          </div>
        </dl>
      </div>
      <p v-else class="text-sm text-muted-foreground">未取得对应需求来源，暂无法确认销售关联。</p>
    </template>
  </section>
</template>
