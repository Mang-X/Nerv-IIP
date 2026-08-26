<script setup lang="ts">
import type { BusinessConsoleMesLineSideInventoryBalanceItem } from '@nerv-iip/api-client'
import { lineSideInventoryAgePresentation } from '@nerv-iip/business-core'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { NvButton, NvDataTable, NvStatusBadge } from '@nerv-iip/ui'
import { RefreshCwIcon } from '@lucide/vue'
import { computed } from 'vue'
import { inlineErrorMessage } from '@/utils/notify'

const props = defineProps<{
  error: unknown
  items: BusinessConsoleMesLineSideInventoryBalanceItem[]
  hasNextPage: boolean
  hasPreviousPage: boolean
  page: number
  pageCount: number
  pending: boolean
  ready: boolean
  total: number
}>()

const emit = defineEmits<{ nextPage: []; previousPage: []; refresh: [] }>()

const columns: NvDataTableColumn<BusinessConsoleMesLineSideInventoryBalanceItem>[] = [
  { key: 'skuCode', header: '物料', cellClass: 'font-medium' },
  { key: 'locationCode', header: '站点 / 线边库' },
  { key: 'onHandQuantity', header: '库存数量' },
  { key: 'lotCount', header: '批次', width: 'w-20' },
  { key: 'ageDays', header: '账龄', width: 'w-64' },
]

const errorMessage = computed(() => inlineErrorMessage(props.error))

function quantity(value?: number | null) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}

function rowKey(item: BusinessConsoleMesLineSideInventoryBalanceItem) {
  return `${item.siteCode}-${item.locationCode}-${item.skuCode}-${item.uomCode}`
}
</script>

<template>
  <section class="space-y-3" aria-labelledby="line-side-inventory-title">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 id="line-side-inventory-title" class="text-base font-semibold text-foreground">
          线边库存余额与账龄
        </h2>
        <p class="mt-1 text-sm text-muted-foreground">
          库存服务权威余额；第 {{ page }} / {{ pageCount }} 页，本页 {{ items.length }} 条，共
          {{ total }} 条。
        </p>
      </div>
      <div class="flex flex-wrap items-center gap-2">
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="pending || !hasPreviousPage"
          @click="emit('previousPage')"
        >
          上一页
        </NvButton>
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="pending || !hasNextPage"
          @click="emit('nextPage')"
        >
          下一页
        </NvButton>
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="pending"
          @click="emit('refresh')"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新库存
        </NvButton>
      </div>
    </div>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">
      线边库存加载失败：{{ errorMessage }}
    </p>
    <p v-else-if="pending && !ready" class="text-sm text-muted-foreground" aria-live="polite">
      正在加载线边库存余额与账龄…
    </p>
    <div
      v-else-if="ready && items.length === 0"
      class="rounded-md border border-dashed bg-muted/20 px-4 py-8 text-center text-sm text-muted-foreground"
    >
      当前组织/环境范围暂无线边库存余额。
    </div>

    <template v-else-if="items.length > 0">
      <div data-testid="line-side-inventory-mobile" class="grid gap-3 md:hidden">
        <article
          v-for="item in items"
          :key="`${item.siteCode}-${item.locationCode}-${item.skuCode}-${item.uomCode}`"
          class="grid gap-2 rounded-md border bg-card p-4"
        >
          <div class="flex items-start justify-between gap-3">
            <div>
              <p class="font-medium text-foreground">{{ item.skuCode ?? '物料编码未提供' }}</p>
              <p class="text-sm text-muted-foreground">
                {{ item.locationCode ?? '未指定线边库' }} · {{ item.siteCode ?? '未指定站点' }}
              </p>
            </div>
            <span class="shrink-0 text-sm text-muted-foreground">{{ item.lotCount ?? 0 }} 批</span>
          </div>
          <p class="text-sm tabular-nums text-foreground">
            在手 {{ quantity(item.onHandQuantity) }} {{ item.uomCode ?? '单位未提供' }} · 预留
            {{ quantity(item.reservedQuantity) }} · 可用 {{ quantity(item.availableQuantity) }}
            {{ item.uomCode ?? '单位未提供' }}
          </p>
          <div class="flex flex-wrap items-center gap-2">
            <span class="text-sm text-muted-foreground">
              {{ lineSideInventoryAgePresentation(item).detail }}
            </span>
            <NvStatusBadge
              :label="lineSideInventoryAgePresentation(item).label"
              :tone="lineSideInventoryAgePresentation(item).tone"
            />
          </div>
        </article>
      </div>

      <div class="hidden md:block">
        <NvDataTable
          :columns="columns"
          :rows="items"
          :row-key="rowKey"
          :loading="pending"
          :searchable="false"
          :column-settings="false"
          :pagination="false"
        >
          <template #cell-locationCode="{ row }">
            <div class="flex flex-col">
              <span>{{ row.locationCode ?? '未指定线边库' }}</span>
              <span class="text-xs text-muted-foreground">{{ row.siteCode ?? '未指定站点' }}</span>
            </div>
          </template>
          <template #cell-onHandQuantity="{ row }">
            <div class="grid gap-0.5 text-sm tabular-nums">
              <span>在手 {{ quantity(row.onHandQuantity) }} {{ row.uomCode ?? '单位未提供' }}</span>
              <span class="text-muted-foreground">
                预留 {{ quantity(row.reservedQuantity) }} · 可用
                {{ quantity(row.availableQuantity) }} {{ row.uomCode ?? '单位未提供' }}
              </span>
            </div>
          </template>
          <template #cell-lotCount="{ row }">{{ row.lotCount ?? 0 }} 批</template>
          <template #cell-ageDays="{ row }">
            <div class="flex flex-col items-start gap-1">
              <span class="text-sm">{{ lineSideInventoryAgePresentation(row).detail }}</span>
              <NvStatusBadge
                :label="lineSideInventoryAgePresentation(row).label"
                :tone="lineSideInventoryAgePresentation(row).tone"
              />
            </div>
          </template>
        </NvDataTable>
      </div>
    </template>
  </section>
</template>
