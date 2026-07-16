<script setup lang="ts">
import { describeMesReadinessReason, useMesWorkOrderDetail } from '@/composables/useBusinessMes'
import {
  isScheduleInvalidated,
  scheduleInvalidationHint,
} from '@/composables/useScheduleInvalidation'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  Spinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { ExternalLinkIcon } from '@lucide/vue'
import { computed, watch } from 'vue'
import { useRouter } from 'vue-router'

// 当前要速览的工单（null = 关闭）。任何页面只要 v-model:work-order-id 一个 ref 即可就地速览，不跳页。
const workOrderId = defineModel<string | null>('workOrderId', { default: null })
const router = useRouter()
const { detail, detailError, detailPending, filters } = useMesWorkOrderDetail()

watch(
  workOrderId,
  (id) => {
    filters.workOrderId = id ?? ''
  },
  { immediate: true },
)

const open = computed({
  get: () => !!workOrderId.value,
  set: (value) => {
    if (!value) workOrderId.value = null
  },
})

const errorMessage = computed(() =>
  detailError.value instanceof Error ? detailError.value.message : '',
)

const operations = computed(() =>
  [...(detail.value?.operationTasks ?? [])].sort(
    (a, b) => (a.operationSequence ?? 0) - (b.operationSequence ?? 0),
  ),
)
const blockingReasons = computed(() =>
  (detail.value?.blockingReasons ?? []).map(describeMesReadinessReason),
)

// 状态 / 就绪 / 工序状态统一交给共享 StatusBadge（:value）解析为中文标签 + 语义色，
// 与各列表页同一口径，避免本组件再维护一份不全的映射而漏出英文裸值（如 created/Queued）。

// 想看全貌再跳完整详情页；速览本身不跳页。
function openFull() {
  const id = workOrderId.value
  workOrderId.value = null
  if (id) void router.push(`/mes/work-orders/${encodeURIComponent(id)}`)
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>工单速览</NvDialogTitle>
        <NvDialogDescription>{{ workOrderId }}</NvDialogDescription>
      </NvDialogHeader>

      <div v-if="detailPending" class="flex items-center gap-2 py-6 text-sm text-muted-foreground">
        <Spinner aria-hidden="true" />
        加载工单概要…
      </div>
      <p
        v-else-if="errorMessage"
        class="rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"
        role="alert"
      >
        {{ errorMessage }}
      </p>
      <div v-else-if="detail" class="grid max-h-[60vh] content-start gap-4 overflow-y-auto px-1">
        <!-- 概要 -->
        <div class="grid gap-2 text-sm">
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">状态</span>
            <NvStatusBadge :value="detail.status" />
          </div>
          <div class="flex items-center justify-between gap-3">
            <span class="text-muted-foreground">就绪</span>
            <NvStatusBadge :value="detail.readinessStatus" />
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">数量</span>
            <span class="font-medium tabular-nums">{{ detail.quantity ?? '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">物料</span>
            <span v-if="detail.skuId" class="font-medium">{{ detail.skuId }}</span>
            <span v-else class="text-muted-foreground">—</span>
          </div>
        </div>

        <!-- 阻塞（有就先显，最要紧） -->
        <div
          v-if="blockingReasons.length"
          class="grid gap-1 rounded-md border border-warning/30 bg-warning/10 p-3 text-sm"
        >
          <span class="font-medium text-warning"
            >{{ blockingReasons.length }} 项卡点，需先处理：</span
          >
          <span v-for="(reason, i) in blockingReasons" :key="i" class="text-muted-foreground"
            >· {{ reason.label }}（{{ reason.nextStep }}）</span
          >
        </div>

        <!-- 工序 -->
        <div>
          <p class="mb-1 text-sm font-medium text-foreground">工序（{{ operations.length }}）</p>
          <div v-if="operations.length" class="overflow-hidden rounded-md border">
            <div
              v-for="op in operations"
              :key="op.operationTaskId ?? op.operationSequence ?? ''"
              class="flex items-center justify-between gap-3 border-b px-3 py-2 text-sm last:border-b-0"
            >
              <span class="font-medium">工序 {{ op.operationSequence ?? '—' }}</span>
              <span
                class="inline-flex"
                :title="
                  isScheduleInvalidated(op.status)
                    ? scheduleInvalidationHint(op.scheduleInvalidationReasonCode)
                    : undefined
                "
              >
                <NvStatusBadge :value="op.status" />
              </span>
            </div>
          </div>
          <p v-else class="rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该工单暂无工序。
          </p>
        </div>
      </div>
      <p v-else class="py-6 text-sm text-muted-foreground">未找到工单概要。</p>

      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="workOrderId = null">关闭</NvButton>
        <NvButton type="button" :disabled="!detail" @click="openFull">
          <ExternalLinkIcon aria-hidden="true" />
          打开完整详情
        </NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>
</template>
