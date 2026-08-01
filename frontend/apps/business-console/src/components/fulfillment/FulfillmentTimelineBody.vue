<script setup lang="ts">
import type { BusinessConsoleErpSalesOrderItem } from '@nerv-iip/api-client'
import type { FulfillmentNode, FulfillmentNodeStatus } from '@/composables/useFulfillmentTimeline'
import type { TimelineItem, TimelineTone } from '@nerv-iip/ui'
import { useFulfillmentTimeline } from '@/composables/useFulfillmentTimeline'
import { NvButton, NvTimeline } from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, toRef } from 'vue'
import FulfillmentTimelineNode from './FulfillmentTimelineNode.vue'

const props = defineProps<{ order: BusinessConsoleErpSalesOrderItem | null | undefined }>()

const timeline = useFulfillmentTimeline(toRef(props, 'order'))

const toneByStatus: Record<FulfillmentNodeStatus, TimelineTone> = {
  established: 'success',
  loading: 'brand',
  pending: 'neutral',
  unlinked: 'neutral',
  restricted: 'warning',
  failed: 'danger',
}

const items = computed<TimelineItem[]>(() =>
  timeline.nodes.value.map((node) => ({
    key: node.key,
    tone: toneByStatus[node.status],
    dotType: node.status === 'established' ? 'solid' : 'hollow',
  })),
)

function nodeFor(key: string): FulfillmentNode | undefined {
  return timeline.nodes.value.find((node) => node.key === key)
}
</script>

<template>
  <div>
    <div class="flex items-center justify-end px-4 pb-2">
      <NvButton
        size="sm"
        variant="outline"
        type="button"
        :disabled="timeline.pending.value"
        @click="timeline.refreshAll()"
      >
        <RefreshCwIcon aria-hidden="true" />
        刷新
      </NvButton>
    </div>

    <div class="px-4 pb-6">
      <NvTimeline :items="items">
        <template v-for="node in timeline.nodes.value" :key="node.key" #[node.key]>
          <FulfillmentTimelineNode
            v-if="nodeFor(node.key)"
            :node="nodeFor(node.key)!"
            @retry="timeline.retry(node.key)"
          />
        </template>
      </NvTimeline>

      <!--
        参考指标区（#1418 B1）：排程紧急度按交期直接计算、不依赖 MRP/工单进度，
        不能摆进上方因果链——否则会出现「MRP 尚未产生、紧急度却已有结论」的假因果。
      -->
      <section
        v-if="timeline.referenceNodes.value.length > 0"
        class="mt-6 rounded-lg border border-border bg-muted/30 p-4"
        aria-label="参考指标"
      >
        <h3 class="text-sm font-medium text-foreground">参考指标</h3>
        <p class="mt-1 text-xs text-muted-foreground">
          以下指标按销售单号独立计算，不依赖上方各环节的先后进度。
        </p>
        <div class="mt-3 space-y-4">
          <FulfillmentTimelineNode
            v-for="node in timeline.referenceNodes.value"
            :key="node.key"
            :node="node"
            @retry="timeline.retry(node.key)"
          />
        </div>
      </section>
    </div>
  </div>
</template>
