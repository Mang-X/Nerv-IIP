<script setup lang="ts">
import type { BusinessConsoleMesLineSideInventoryBalanceItem } from '@nerv-iip/api-client'
import { lineSideInventoryAgePresentation } from '@nerv-iip/business-core'
import { NvListRow } from '@nerv-iip/ui-mobile'
import RetryableListError from '@/components/RetryableListError.vue'

defineProps<{
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

function quantity(value?: number | null) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
}
</script>

<template>
  <section class="space-y-3" aria-labelledby="pda-line-side-inventory-title">
    <div class="flex items-center justify-between gap-3">
      <h2 id="pda-line-side-inventory-title" class="text-base font-semibold text-foreground">
        线边库存
      </h2>
      <button
        type="button"
        class="min-h-touch shrink-0 rounded-lg border border-border bg-card px-3 text-sm font-medium text-primary disabled:opacity-50"
        :disabled="pending"
        @click="emit('refresh')"
      >
        刷新库存
      </button>
    </div>
    <div class="grid grid-cols-[auto_1fr_auto] items-center gap-2">
      <button
        type="button"
        class="min-h-touch rounded-lg border border-border bg-card px-3 text-sm font-medium text-primary disabled:opacity-50"
        :disabled="pending || !hasPreviousPage"
        @click="emit('previousPage')"
      >
        上一页
      </button>
      <p class="text-center text-xs text-muted-foreground">
        第 {{ page }} / {{ pageCount }} 页<br />本页 {{ items.length }} 条 · 共 {{ total }} 条
      </p>
      <button
        type="button"
        class="min-h-touch rounded-lg border border-border bg-card px-3 text-sm font-medium text-primary disabled:opacity-50"
        :disabled="pending || !hasNextPage"
        @click="emit('nextPage')"
      >
        下一页
      </button>
    </div>

    <RetryableListError
      v-if="error"
      :error="error"
      :pending="pending"
      fallback="加载线边库存失败，请重试。"
      test-id="line-side-inventory-error"
      @retry="emit('refresh')"
    />
    <p v-else-if="pending && !ready" class="py-6 text-center text-sm text-muted-foreground">
      正在加载线边库存…
    </p>
    <div
      v-else-if="ready && items.length === 0"
      class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
    >
      当前组织/环境范围暂无线边库存余额
    </div>
    <div v-else-if="items.length > 0" class="overflow-hidden rounded-lg border border-border">
      <NvListRow
        v-for="item in items"
        :key="`${item.siteCode}-${item.locationCode}-${item.skuCode}-${item.uomCode}`"
        :title="item.skuCode ?? '物料编码未提供'"
        :subtitle="`${item.locationCode ?? '未指定线边库'} · ${item.siteCode ?? '未指定站点'}`"
        :interactive="false"
      >
        <template #meta>
          <div class="mt-2 grid gap-1 text-sm text-foreground">
            <p class="tabular-nums">
              在手 {{ quantity(item.onHandQuantity) }} {{ item.uomCode ?? '单位未提供' }} · 预留
              {{ quantity(item.reservedQuantity) }} · 可用 {{ quantity(item.availableQuantity) }}
              {{ item.uomCode ?? '单位未提供' }}
            </p>
            <p class="text-muted-foreground">{{ item.lotCount ?? 0 }} 批</p>
            <p
              :class="
                lineSideInventoryAgePresentation(item).tone === 'warning'
                  ? 'text-warning-foreground'
                  : 'text-muted-foreground'
              "
            >
              {{ lineSideInventoryAgePresentation(item).detail }} ·
              {{ lineSideInventoryAgePresentation(item).label }}
            </p>
          </div>
        </template>
      </NvListRow>
    </div>
  </section>
</template>
