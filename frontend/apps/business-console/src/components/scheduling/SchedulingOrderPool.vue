<script setup lang="ts">
import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { WorkingScheduleOrder } from '@/composables/useWorkingScheduleDraft'
import { NvButton, NvCheckbox, NvInput, Spinner } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  candidates: BusinessConsoleMesWorkOrderItem[]
  draftOrders: WorkingScheduleOrder[]
  loading?: boolean
  readOnly?: boolean
}>()

const emit = defineEmits<{
  include: [workOrderIds: string[], included: boolean]
  update: [workOrderId: string, patch: { priority?: number; isRush?: boolean }]
}>()

const byId = computed(() => new Map(props.draftOrders.map((order) => [order.workOrderId, order])))
const candidateIds = computed(
  () => props.candidates.map((candidate) => candidate.workOrderId).filter(Boolean) as string[],
)

function setPriority(workOrderId: string, value: string | number) {
  const priority = Number(value)
  if (Number.isFinite(priority)) emit('update', workOrderId, { priority })
}
</script>

<template>
  <section class="grid gap-3 rounded-lg border bg-card p-4" data-testid="scheduling-order-pool">
    <header class="flex flex-wrap items-center justify-between gap-3">
      <div>
        <h2 class="font-semibold">待排工单池</h2>
        <p class="text-sm text-muted-foreground">从 MES 权威工单中一次选择最多 500 条。</p>
      </div>
      <div class="flex gap-2">
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="readOnly || candidateIds.length === 0"
          @click="emit('include', candidateIds, true)"
          >全部加入</NvButton
        >
        <NvButton
          size="sm"
          variant="ghost"
          type="button"
          :disabled="readOnly"
          @click="emit('include', candidateIds, false)"
          >全部移出</NvButton
        >
      </div>
    </header>

    <div
      v-if="loading"
      class="flex min-h-32 items-center justify-center gap-2 text-sm text-muted-foreground"
    >
      <Spinner aria-hidden="true" />正在读取 MES 工单
    </div>
    <div v-else class="max-h-80 overflow-auto rounded-md border">
      <table class="w-full text-sm">
        <thead class="sticky top-0 bg-muted/90 text-left">
          <tr>
            <th class="p-2">加入</th>
            <th class="p-2">工单</th>
            <th class="p-2">SKU</th>
            <th class="p-2">交期</th>
            <th class="p-2">优先级</th>
            <th class="p-2">急单</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="candidate in candidates" :key="candidate.workOrderId" class="border-t">
            <td class="p-2">
              <NvCheckbox
                :checked="byId.get(candidate.workOrderId ?? '')?.included ?? false"
                :disabled="readOnly"
                :aria-label="`加入工单 ${candidate.workOrderId}`"
                @update:checked="emit('include', [candidate.workOrderId ?? ''], Boolean($event))"
              />
            </td>
            <td class="p-2 font-medium">{{ candidate.workOrderNo || candidate.workOrderId }}</td>
            <td class="p-2">{{ candidate.skuCode || candidate.skuId }}</td>
            <td class="p-2">
              {{ candidate.dueUtc ? new Date(candidate.dueUtc).toLocaleString() : '—' }}
            </td>
            <td class="p-2">
              <NvInput
                class="h-8 w-24"
                type="number"
                min="0"
                :disabled="readOnly"
                :model-value="
                  String(
                    byId.get(candidate.workOrderId ?? '')?.priority ?? candidate.priority ?? 100,
                  )
                "
                @update:model-value="setPriority(candidate.workOrderId ?? '', $event)"
              />
            </td>
            <td class="p-2">
              <NvCheckbox
                :checked="byId.get(candidate.workOrderId ?? '')?.isRush ?? false"
                :disabled="readOnly"
                :aria-label="`急单 ${candidate.workOrderId}`"
                @update:checked="
                  emit('update', candidate.workOrderId ?? '', { isRush: Boolean($event) })
                "
              />
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </section>
</template>
