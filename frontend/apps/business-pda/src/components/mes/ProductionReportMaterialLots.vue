<script setup lang="ts">
import type { BusinessConsoleMesMaterialIssueRequestRow } from '@nerv-iip/api-client'
import type { AvailableMaterialLotFields } from '@nerv-iip/business-core'

type AvailableMaterialLot = BusinessConsoleMesMaterialIssueRequestRow & AvailableMaterialLotFields

defineProps<{
  availableMaterialLots: readonly AvailableMaterialLot[]
  materialLotsPending: boolean
  materialLotsError: unknown
  submitting: boolean
  materialValidationMessage: string
  refreshMaterialLots: () => unknown
  materialSelected: (requestId: string | undefined) => boolean
  materialQuantity: (requestId: string | undefined) => string
  setMaterialSelected: (requestId: string | undefined, selected: boolean | undefined) => void
  setMaterialQuantity: (requestId: string | undefined, quantity: string | undefined) => void
  materialRemaining: (row: { receivedQuantity?: number; consumedQuantity?: number }) => string
}>()
</script>

<template>
  <section
    data-testid="production-material-lots"
    class="space-y-2 rounded-lg border border-border bg-card p-3"
  >
    <div class="flex items-center justify-between gap-3">
      <h3 class="text-sm font-medium text-foreground">耗料批次</h3>
      <button
        type="button"
        class="text-sm text-primary"
        :disabled="materialLotsPending || submitting"
        @click="refreshMaterialLots"
      >
        刷新
      </button>
    </div>
    <p v-if="materialLotsError" class="text-sm text-destructive">
      已收料批次读取失败，请刷新后重试。
    </p>
    <p v-else-if="materialLotsPending" class="text-sm text-muted-foreground">正在读取已收料批次…</p>
    <p v-else-if="availableMaterialLots.length === 0" class="text-sm text-muted-foreground">
      当前工序暂无可用已收料批次。
    </p>
    <div v-for="row in availableMaterialLots" :key="row.requestId" class="space-y-2">
      <label class="flex items-center gap-2 text-sm text-foreground">
        <input
          :checked="materialSelected(row.requestId)"
          :data-testid="`material-lot-${row.requestId}`"
          type="checkbox"
          class="size-5"
          :disabled="submitting"
          @change="setMaterialSelected(row.requestId, ($event.target as HTMLInputElement).checked)"
        />
        <span>
          {{ row.materialId }} · {{ row.materialLotId }}
          <span class="text-muted-foreground">
            （{{ row.operationTaskId ? '本工序' : '工单级' }}，可用 {{ materialRemaining(row) }}
            {{ row.uomCode }}）
          </span>
        </span>
      </label>
      <input
        :value="materialQuantity(row.requestId)"
        :data-testid="`material-quantity-${row.requestId}`"
        type="number"
        inputmode="decimal"
        min="0"
        step="any"
        placeholder="耗用数量"
        class="min-h-touch w-full rounded-lg border border-border bg-background px-3 text-base outline-none focus:border-primary disabled:opacity-60"
        :disabled="!materialSelected(row.requestId) || submitting"
        @input="setMaterialQuantity(row.requestId, ($event.target as HTMLInputElement).value)"
      />
    </div>
    <p v-if="materialValidationMessage" class="text-sm text-destructive" role="alert">
      {{ materialValidationMessage }}
    </p>
  </section>
</template>
