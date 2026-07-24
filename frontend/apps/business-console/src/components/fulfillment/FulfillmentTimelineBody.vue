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
    </div>
  </div>
</template>
