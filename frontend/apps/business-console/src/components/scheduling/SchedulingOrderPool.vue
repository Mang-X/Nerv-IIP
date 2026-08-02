<script setup lang="ts">
import type { BusinessConsoleMesWorkOrderItem } from '@nerv-iip/api-client'
import type { WorkingScheduleOrder } from '@/composables/useWorkingScheduleDraft'
import CodeWithNameCell from '@/components/business/CodeWithNameCell.vue'
import OrderUrgencyBadge from '@/components/urgency/OrderUrgencyBadge.vue'
import { useOrderUrgencies } from '@/composables/useOrderUrgency'
import { DEFAULT_URGENCY_DISPLAY_MODE } from '@/composables/useUrgencyDisplayMode'
import { useSkuNames } from '@/composables/useSkuNames'
import { AlertTriangleIcon, RefreshCwIcon, SearchIcon, XIcon } from '@lucide/vue'
import { NvButton, NvCheckbox, NvInput, Spinner } from '@nerv-iip/ui'
import { computed, ref } from 'vue'

const props = withDefaults(
  defineProps<{
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
    /**
     * 主体授权作业范围是否就绪（#1288）。未就绪时候选查询根本没发（enabled=false），
     * candidates 为空是「没查」而不是「没有」——必须自己出形态，不许下「没有待排产的工单」结论。
     * 不传（undefined）视为不启用该门禁，保持向后兼容；默认 undefined 用 withDefaults
     * 显式声明，避免 Vue 把缺省布尔 prop 铸成 false 误触发未就绪形态。
     */
    scopeReady?: boolean
    /** 作业范围未就绪时的原因说明（缺什么、去哪配）。 */
    scopeMessage?: string
  }>(),
  { scopeReady: undefined },
)

const emit = defineEmits<{
  include: [workOrderIds: string[], included: boolean]
  update: [workOrderId: string, patch: { priority?: number; isRush?: boolean }]
  retry: []
}>()

const scopeBlocked = computed(() => !props.loading && props.scopeReady === false)
const failed = computed(() => !props.loading && !scopeBlocked.value && props.error != null)
const isEmpty = computed(
  () => !props.loading && !scopeBlocked.value && !failed.value && props.candidates.length === 0,
)

// 工单池只回 SKU 编码，物料名在主数据里；查不到就只显编码，不编造物料名。
const { resolveSkuName } = useSkuNames()

/**
 * 待排池的紧迫度（第五轮走查补）。
 *
 * 池子原来只有交期、优先级、急单三列——**排产员要先自己拿交期跟今天比**才知道哪张紧。
 * 需求池（#1424）与 MES 工单页都已经有这一列，同一个人在三个页面之间切换却看到三种口径。
 * 键与 MES 工单页保持一致：`workOrderId`（紧急度读面按工单登记）。
 */
const orderUrgencies = useOrderUrgencies(
  computed(() => props.candidates.map((candidate) => candidate.workOrderId ?? '')),
)

const byId = computed(() => new Map(props.draftOrders.map((order) => [order.workOrderId, order])))

/**
 * 池内搜索（#1399 M5）。池子一次最多 500 条，此前**一个搜索框都没有**——排产员找一张急单
 * 只能滚，成本高于浏览器 Ctrl+F，于是他去 Excel。
 *
 * 纯前端过滤，不改查询:候选集已经整批在手（最多 500 条），再发一次请求既慢又会把
 * scopeReady/失败态那套形态判定重新绕一遍。
 */
const search = ref('')
const filteredCandidates = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return props.candidates
  return props.candidates.filter((candidate) =>
    [
      candidate.workOrderNo,
      candidate.workOrderId,
      candidate.skuCode,
      candidate.skuId,
      resolveSkuName(candidate.skuCode),
    ].some((field) => field && String(field).toLowerCase().includes(q)),
  )
})
/** 搜索中但一条没中。与「本来就没有待排工单」是两回事,必须分开出形态。 */
const noSearchHit = computed(
  () =>
    !props.loading &&
    !scopeBlocked.value &&
    !failed.value &&
    props.candidates.length > 0 &&
    filteredCandidates.value.length === 0,
)

// 「全部加入/移出」跟随当前可见行:筛出 3 条却把 500 条全加进去,是这类批量按钮最经典的事故。
const candidateIds = computed(
  () =>
    filteredCandidates.value.map((candidate) => candidate.workOrderId).filter(Boolean) as string[],
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
        <p class="text-sm text-muted-foreground">
          从 MES 权威工单中一次选择最多 10 条。批量越大排程耗时越长，超过上限会被拒绝。
        </p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <!-- 作业范围选择入口由宿主页面注入（与 MES 工单页共享同一份选择）。 -->
        <slot name="scope" />
        <div v-if="!loading && !scopeBlocked && !failed && candidates.length" class="relative">
          <SearchIcon
            class="pointer-events-none absolute top-1/2 left-2.5 size-3.5 -translate-y-1/2 text-muted-foreground"
            aria-hidden="true"
          />
          <!-- type="text" 而非 "search":Chrome 给 search 框加原生清除 ×，会和下面这个
               带无障碍名的清除按钮并排出现两个 ×。Esc 清空我们自己接了。 -->
          <NvInput
            v-model="search"
            type="text"
            class="h-8 w-60 pr-8 pl-8 text-sm"
            aria-label="搜索待排工单"
            placeholder="搜工单号 / 物料编码 / 物料名"
            @keydown.esc.prevent="search = ''"
          />
          <NvButton
            v-if="search"
            size="icon"
            variant="ghost"
            class="absolute top-1/2 right-0.5 size-7 -translate-y-1/2 text-muted-foreground"
            aria-label="清空搜索"
            type="button"
            @click="search = ''"
          >
            <XIcon class="size-3.5" aria-hidden="true" />
          </NvButton>
        </div>
        <span v-if="search.trim()" class="text-xs text-muted-foreground tabular-nums" role="status"
          >{{ filteredCandidates.length }} / {{ candidates.length }}</span
        >
        <!-- 批量按钮作用于当前可见行:筛选中时把这一点写进按钮文案与 title,不让人猜。 -->
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="readOnly || scopeBlocked || failed || candidateIds.length === 0"
          :title="
            search.trim()
              ? `把当前筛选出的 ${candidateIds.length} 张工单加入本次排程`
              : '把池内全部工单加入本次排程'
          "
          @click="emit('include', candidateIds, true)"
          >{{ search.trim() ? `加入筛选结果（${candidateIds.length}）` : '全部加入' }}</NvButton
        >
        <NvButton
          size="sm"
          variant="ghost"
          type="button"
          :disabled="readOnly"
          :title="
            search.trim()
              ? `把当前筛选出的 ${candidateIds.length} 张工单移出本次排程`
              : '把池内全部工单移出本次排程'
          "
          @click="emit('include', candidateIds, false)"
          >{{ search.trim() ? '移出筛选结果' : '全部移出' }}</NvButton
        >
      </div>
    </header>

    <div
      v-if="loading"
      class="flex min-h-32 items-center justify-center gap-2 text-sm text-muted-foreground"
    >
      <Spinner aria-hidden="true" />正在读取 MES 工单
    </div>

    <!-- 作业范围未就绪：候选查询根本没发，不许下「没有待排产的工单」结论（#1288） -->
    <div
      v-else-if="scopeBlocked"
      class="flex min-h-32 flex-col items-center justify-center gap-2 rounded-md border border-warning/40 bg-warning/[0.06] px-6 py-6 text-center"
      role="alert"
      data-testid="scheduling-order-pool-scope-blocked"
    >
      <span class="grid size-10 place-items-center rounded-full bg-warning/15">
        <AlertTriangleIcon class="size-5 text-warning" aria-hidden="true" />
      </span>
      <p class="text-sm font-medium text-foreground">作业范围未就绪，尚未读取待排工单</p>
      <p class="text-sm leading-6 text-muted-foreground">
        {{
          scopeMessage ||
          '请先在上方选择已授权的作业范围；若没有可选项，请联系管理员在 IAM 为账号配置数据范围。'
        }}
      </p>
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

    <!-- 搜了但没中：和「本来就没有待排工单」是两回事，说清是筛没了并给一键清空 -->
    <div
      v-else-if="noSearchHit"
      class="flex min-h-32 flex-col items-center justify-center gap-2 rounded-md border border-dashed px-6 py-6 text-center"
      role="status"
      data-testid="scheduling-order-pool-no-search-hit"
    >
      <p class="text-sm font-medium text-foreground">
        池内 {{ candidates.length }} 张工单里没有匹配「{{ search.trim() }}」的
      </p>
      <p class="text-sm leading-6 text-muted-foreground">
        搜索按工单号、物料编码与物料名匹配；换个关键词，或清空后看全部。
      </p>
      <NvButton class="mt-1" size="sm" type="button" variant="outline" @click="search = ''">
        清空搜索
      </NvButton>
    </div>

    <div v-else class="max-h-80 overflow-auto rounded-md border">
      <table class="w-full text-sm">
        <thead class="sticky top-0 z-10 bg-muted text-left [&_th]:whitespace-nowrap">
          <tr>
            <th class="p-2">加入</th>
            <th class="p-2">工单</th>
            <th class="p-2">物料</th>
            <th class="p-2">交期</th>
            <th class="p-2">紧迫度</th>
            <th class="p-2">优先级</th>
            <th class="p-2">急单</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="candidate in filteredCandidates" :key="candidate.workOrderId" class="border-t">
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
              <OrderUrgencyBadge
                :order-reference="candidate.workOrderId ?? ''"
                :mode="DEFAULT_URGENCY_DISPLAY_MODE"
                :urgency="
                  candidate.workOrderId
                    ? orderUrgencies.byReference.value.get(candidate.workOrderId)
                    : undefined
                "
                :source-unavailable="orderUrgencies.error?.value != null"
                @refresh="orderUrgencies.refresh"
              />
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
