<script setup lang="ts">
import type { BusinessConsoleMesTraceabilityResponse } from '@nerv-iip/api-client'
import { reportTraceSources } from '@/composables/mes/reportTraceability'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { NvButton } from '@nerv-iip/ui'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

const props = defineProps<{
  node: { nodeId?: string; nodeType?: string }
  graph?: BusinessConsoleMesTraceabilityResponse
  returnTo: string
}>()
const auth = useAuthStore()
const canRead = computed(() => (auth.principal?.permissionCodes ?? []).includes(P.mesReportingRead))
const supported = computed(() =>
  [
    'ProductionReport',
    'ProducedLot',
    'Serial',
    'ProducedLotOrSerial',
    'Material',
    'MaterialLot',
    'MaterialIssueRequest',
  ].includes(props.node.nodeType ?? ''),
)
const sources = computed(() => reportTraceSources(props.graph, props.node))
</script>

<template>
  <div v-if="supported" class="flex flex-col items-start gap-1">
    <span v-if="!canRead" class="text-sm text-muted-foreground">无报工读取权限</span>
    <span v-else-if="!sources.length" class="text-sm text-muted-foreground">无匹配的来源报工</span>
    <template v-else>
      <span v-if="sources.length > 1" class="text-sm text-muted-foreground">选择来源报工</span>
      <NvButton
        v-for="source in sources"
        :key="`${source.reportNo}:${source.nodeType}`"
        variant="link"
        size="sm"
        as-child
      >
        <RouterLink
          :to="{
            path: '/mes/production-reports',
            query: {
              reportNo: source.reportNo,
              sourceNodeType: source.nodeType,
              sourceNodeId: source.nodeId,
              traceabilityReturn: returnTo,
            },
          }"
        >
          {{
            source.nodeType === 'Serial'
              ? '序列'
              : source.nodeType === 'ProducedLot'
                ? '批次'
                : source.nodeType === 'ProductionReport'
                  ? '报工'
                  : '耗料'
          }}
          · {{ source.reportNo }}
        </RouterLink>
      </NvButton>
    </template>
  </div>
</template>
