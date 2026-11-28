<script setup lang="ts">
import {
  deviceControlApprovalLabel,
  deviceControlCommandTypeLabel,
  deviceControlStatusLabel,
  deviceControlStatusTone,
  deviceReceiptLabel,
} from '@/composables/equipment/useBusinessDeviceControl'
import { NvBadge, NvButton, NvDataTable } from '@nerv-iip/ui'
import { SlidersHorizontalIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useDeviceDetailPage } from '../useDeviceDetailPage'

type View = ShallowUnwrapRef<ReturnType<typeof useDeviceDetailPage>>

defineProps<{
  canControlDevice: View['canControlDevice']
  controlColumns: View['controlColumns']
  controlCommandRowKey: View['controlCommandRowKey']
  controlCommands: View['controlCommands']
  controlCommandsErrorMessage: View['controlCommandsErrorMessage']
  controlCommandsPending: View['controlCommandsPending']
  controlCommandsTotal: View['controlCommandsTotal']
}>()

defineEmits<{ retry: [] }>()

const controlSheetOpen = defineModel<View['controlSheetOpen']>('controlSheetOpen', {
  required: true,
})

const controlPage = defineModel<View['controlPage']>('controlPage', { required: true })

const controlPageSize = defineModel<View['controlPageSize']>('controlPageSize', { required: true })
</script>

<template>
  <section class="grid gap-4">
    <div class="flex flex-wrap items-start justify-between gap-3">
      <div>
        <h2 class="text-base font-semibold text-foreground">控制命令记录</h2>
      </div>
      <NvButton
        v-if="canControlDevice"
        size="sm"
        type="button"
        variant="outline"
        @click="controlSheetOpen = true"
      >
        <SlidersHorizontalIcon aria-hidden="true" />
        设备控制
      </NvButton>
    </div>

    <div v-if="controlCommandsErrorMessage" class="flex items-center gap-3">
      <p class="text-sm text-destructive" role="alert">{{ controlCommandsErrorMessage }}</p>
      <NvButton
        size="sm"
        variant="outline"
        :disabled="controlCommandsPending"
        @click="$emit('retry')"
      >
        重试
      </NvButton>
    </div>

    <NvDataTable
      v-else
      manual
      :page="controlPage"
      :page-size="controlPageSize"
      :total-items="controlCommandsTotal"
      @update:page="controlPage = $event"
      @update:page-size="(v) => (controlPageSize = String(v))"
      :columns="controlColumns"
      :rows="controlCommands"
      :row-key="controlCommandRowKey"
      :loading="controlCommandsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="该设备还没有控制命令记录。点击「设备控制」下发第一条命令。"
    >
      <template #cell-commandType="{ row }">
        <div class="grid gap-0.5">
          <span class="font-medium text-foreground">{{
            deviceControlCommandTypeLabel(row.commandType)
          }}</span>
          <span v-if="row.tagKey" class="text-xs text-muted-foreground">{{ row.tagKey }}</span>
        </div>
      </template>
      <template #cell-approvalStatus="{ row }">
        <NvBadge class="rounded-sm" variant="neutral">{{
          deviceControlApprovalLabel(row.approvalStatus)
        }}</NvBadge>
      </template>
      <template #cell-status="{ row }">
        <div class="grid gap-0.5">
          <NvBadge
            class="rounded-sm justify-self-start"
            :variant="deviceControlStatusTone(row.status)"
            >{{ deviceControlStatusLabel(row.status) }}</NvBadge
          >
          <span
            v-if="row.deviceReceiptCode || row.failureCode"
            class="text-xs"
            :class="
              row.status?.toLowerCase() === 'failed' ? 'text-destructive' : 'text-muted-foreground'
            "
            >{{ deviceReceiptLabel(row.deviceReceiptCode) }}</span
          >
        </div>
      </template>
    </NvDataTable>
  </section>
</template>
