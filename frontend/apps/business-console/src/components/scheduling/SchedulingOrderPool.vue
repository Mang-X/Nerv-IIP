<script setup lang="ts">
import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { WorkingScheduleOrder } from '@/composables/useWorkingScheduleDraft'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import { useSkuNames } from '@/composables/useSkuNames'
import { AlertTriangleIcon, RefreshCwIcon } from '@lucide/vue'
import { NvButton, NvCheckbox, NvInput, Spinner } from '@nerv-iip/ui'
import { computed } from 'vue'

const props = defineProps<{
  candidates: BusinessConsoleMesWorkOrderItem[]
  draftOrders: WorkingScheduleOrder[]
  loading?: boolean
  readOnly?: boolean
  /**
   * MES 工单读取失败（非空即失败）。曾踩坑：这里只有加载态和表体，空数组直接渲染
   * 一个空 tbody——「MES 接口挂了」和「今天真的没有待排工单」长得一模一样，
   * 排产员会据此认为"没活要排"。失败必须自己出形态。
   */
  error?: unknown
}>()

const emit = defineEmits<{
  include: [workOrderIds: string[], included: boolean]
  update: [workOrderId: string, patch: { priority?: number; isRush?: boolean }]
  retry: []
}>()

const failed = computed(() => !props.loading && props.error != null)
const isEmpty = computed(() => !props.loading && !failed.value && props.candidates.length === 0)

// 工单池只回 SKU 编码，物料名在主数据里；查不到就只显编码，不编造物料名。
const { resolveSkuName } = useSkuNames()

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
          :disabled="readOnly || failed || candidateIds.length === 0"
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

    <!-- 失败态：说清取不到、无法判断，并给重试；绝不退化成一张空表 -->
    <div
      v-else-if="failed"
      class="flex min-h-32 flex-col items-center justify-center gap-2 rounded-md border border-destructive/30 bg-destructive/[0.04] px-6 py-6 text-center"
      role="alert"
    >
      <span class="grid size-10 place-items-center rounded-full bg-destructive/10">
        <AlertTriangleIcon class="size-5 text-destructive-strong" aria-hidden="true" />
      </span>
      <p class="text-sm font-medium text-destructive-strong">待排工单读取失败</p>
      <p class="text-sm leading-6 text-muted-foreground">
        没有取到 MES 工单，无法判断当前是否有需要排产的工单。
      </p>
      <NvButton class="mt-1" size="sm" type="button" variant="outline" @click="emit('retry')">
        <RefreshCwIcon aria-hidden="true" />
        重试
      </NvButton>
    </div>

    <!-- 真的没有待排工单：这才允许下"没有活要排"的结论，并指出下一步去哪儿 -->
    <div
      v-else-if="isEmpty"
      class="flex min-h-32 flex-col items-center justify-center gap-1 rounded-md border border-dashed px-6 py-6 text-center"
    >
      <p class="text-sm font-medium text-foreground">当前没有待排产的工单</p>
      <p class="text-sm leading-6 text-muted-foreground">
        MES 里已下达且未完工的工单会自动进入这里；也可以调整上方筛选条件再看一次。
      </p>
    </div>

    <div v-else class="max-h-80 overflow-auto rounded-md border">
      <table class="w-full text-sm">
        <thead class="sticky top-0 bg-muted/90 text-left">
          <tr>
            <th class="p-2">加入</th>
            <th class="p-2">工单</th>
            <th class="p-2">物料</th>
            <th class="p-2">交期</th>
            <th class="p-2">优先级</th>
            <th class="p-2">急单</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="candidate in candidates" :key="candidate.workOrderId" class="border-t">
            <td class="p-2">
              <NvCheckbox
                :model-value="byId.get(candidate.workOrderId ?? '')?.included ?? false"
                :disabled="readOnly"
                :aria-label="`加入工单 ${candidate.workOrderId}`"
                @update:model-value="
                  emit('include', [candidate.workOrderId ?? ''], Boolean($event))
                "
              />
            </td>
            <td class="p-2 font-medium">{{ candidate.workOrderNo || candidate.workOrderId }}</td>
            <td class="p-2">
              <CodeWithNameCell
                :code="candidate.skuCode || candidate.skuId"
                :name="resolveSkuName(candidate.skuCode)"
              />
            </td>
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
                :model-value="byId.get(candidate.workOrderId ?? '')?.isRush ?? false"
                :disabled="readOnly"
                :aria-label="`急单 ${candidate.workOrderId}`"
                @update:model-value="
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
