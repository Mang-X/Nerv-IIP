<script setup lang="ts">
import {
  NvButton,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  Spinner,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed, watch } from 'vue'

import { useQualityInspectionRecordDetail } from '@/composables/useBusinessQuality'
import { notifyError } from '@/utils/notify'

// 来源检验记录只读详情，从质检页抽出（路由页只负责按 ?inspectionRecordId= 编排开合，Vue best-practices §2）。
const props = defineProps<{
  open: boolean
  recordId: string
  organizationId: string
  environmentId: string
}>()
const emit = defineEmits<{
  'update:open': [value: boolean]
}>()

const openModel = computed({
  get: () => props.open,
  set: (value: boolean) => emit('update:open', value),
})

const { record, recordPending, recordError, refreshRecord } = useQualityInspectionRecordDetail(
  () => ({
    organizationId: props.organizationId,
    environmentId: props.environmentId,
    inspectionRecordId: props.recordId,
  }),
)

// 记录详情加载失败（403/5xx/断网）走 toast，并保留可重试；仅真实成功空响应才显示「未找到」。
watch(
  recordError,
  (err) => {
    if (err) notifyError(err, '检验记录加载失败，请稍后重试。')
  },
  { immediate: true },
)
</script>

<template>
  <NvSheet v-model:open="openModel">
    <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
      <NvSheetHeader>
        <NvSheetTitle>检验记录 {{ recordId }}</NvSheetTitle>
        <NvSheetDescription>来源检验记录只读详情，含判定结论与特性实测值。</NvSheetDescription>
      </NvSheetHeader>
      <div class="grid content-start gap-4 p-4">
        <div v-if="recordPending" class="flex items-center gap-2 text-muted-foreground">
          <Spinner aria-hidden="true" />
          <span>正在加载检验记录…</span>
        </div>
        <!-- 加载失败（403/5xx/断网）：错误已 toast，此处给可重试出口，不误报为「未找到」空态。 -->
        <div v-else-if="recordError" class="grid justify-items-start gap-2">
          <p class="text-sm text-muted-foreground">检验记录加载失败，请稍后重试。</p>
          <NvButton size="sm" variant="outline" type="button" @click="refreshRecord">
            <RefreshCwIcon aria-hidden="true" />
            重试
          </NvButton>
        </div>
        <div v-else-if="record" class="grid gap-4">
          <dl class="grid grid-cols-2 gap-x-4 gap-y-2 text-sm">
            <dt class="text-muted-foreground">判定结论</dt>
            <dd><NvStatusBadge :value="record.result" /></dd>
            <dt class="text-muted-foreground">成品</dt>
            <dd>{{ record.skuCode ?? '—' }}</dd>
            <dt class="text-muted-foreground">来源单据</dt>
            <dd>{{ record.sourceDocumentId ?? '—' }}</dd>
            <dt class="text-muted-foreground">批次 / 序列号</dt>
            <dd>{{ record.batchNo ?? '—' }} / {{ record.serialNo ?? '—' }}</dd>
            <dt class="text-muted-foreground">检验数量</dt>
            <dd class="tabular-nums">{{ record.inspectedQuantity ?? 0 }}</dd>
            <template v-if="record.dispositionReason">
              <dt class="text-muted-foreground">处置说明</dt>
              <dd>{{ record.dispositionReason }}</dd>
            </template>
          </dl>
          <p v-if="record.nonconformanceReportId" class="text-sm text-muted-foreground">
            关联不合格品：{{ record.nonconformanceReportId }}
          </p>
          <div v-if="(record.resultLines?.length ?? 0) > 0" class="grid gap-2">
            <span class="text-xs font-semibold uppercase tracking-wide text-muted-foreground"
              >特性实测值</span
            >
            <ul class="grid gap-1">
              <li
                v-for="(line, i) in record.resultLines"
                :key="`${line.characteristicCode}-${i}`"
                class="flex items-center justify-between gap-2 rounded-md border bg-background px-3 py-1.5 text-sm"
              >
                <span class="truncate">{{ line.characteristicCode }}</span>
                <span class="shrink-0 tabular-nums text-muted-foreground">{{
                  line.measuredValue ?? line.observedValue ?? '—'
                }}</span>
              </li>
            </ul>
          </div>
        </div>
        <p v-else class="text-sm text-muted-foreground">
          未找到该检验记录，可能已被清理或无访问权限。
        </p>
      </div>
    </NvSheetContent>
  </NvSheet>
</template>
