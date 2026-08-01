<script setup lang="ts">
import type {
  BusinessConsoleMaintenanceWorkOrderItem,
  BusinessConsoleMasterDataResourceDetail,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import {
  maintenancePriorityLabel,
  maintenanceWorkOrderActionLabel,
  maintenanceWorkOrderBlockReasonLabel,
  maintenanceWorkOrderStatusLabel,
} from '@nerv-iip/business-core'
import { NvMobileTag } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

import {
  formatMaintenanceDateTime,
  isMaintenanceTerminal,
  maintenanceDeviceLocation,
  maintenanceDeviceTitle,
} from './maintenanceWorkOrderPresentation'

const props = defineProps<{
  workOrder: BusinessConsoleMaintenanceWorkOrderItem
  device?: BusinessConsoleResourceItem | BusinessConsoleMasterDataResourceDetail
}>()

const terminal = computed(() => isMaintenanceTerminal(props.workOrder))
const allowedActions = computed(() => props.workOrder.allowedActions ?? [])
const blockReasons = computed(() => props.workOrder.blockReasons ?? [])
const lifecycle = computed(() => props.workOrder.lifecycle ?? [])
const assignment = computed(() => {
  const parts = [
    props.workOrder.assignedTechnicianUserId ? '当前维修人员' : '未指派维修人员',
    props.workOrder.assignedTeamId ? '班组信息已记录但不可解析' : undefined,
  ].filter((part): part is string => Boolean(part))
  return parts.join(' · ')
})
</script>

<template>
  <article class="space-y-4 p-4" data-testid="maintenance-work-order-detail">
    <section
      class="space-y-3 rounded-xl border border-border bg-card p-4"
      aria-labelledby="maintenance-summary-title"
    >
      <div class="flex items-start justify-between gap-3">
        <div class="min-w-0">
          <h2
            id="maintenance-summary-title"
            class="truncate text-base font-semibold text-foreground"
          >
            {{ workOrder.sourceReferenceId || '维修工单详情' }}
          </h2>
        </div>
        <NvMobileTag :variant="terminal ? 'default' : 'brand'">
          {{ maintenanceWorkOrderStatusLabel(workOrder.status) }}
        </NvMobileTag>
      </div>

      <dl class="grid grid-cols-[5rem_1fr] gap-x-3 gap-y-2 text-sm">
        <dt class="text-muted-foreground">设备</dt>
        <dd class="min-w-0 break-words text-foreground">
          {{ maintenanceDeviceTitle(workOrder, device) }}
          <span v-if="device?.code" class="text-muted-foreground"> · {{ device.code }}</span>
        </dd>
        <dt class="text-muted-foreground">位置</dt>
        <dd class="break-words text-foreground">{{ maintenanceDeviceLocation(device) }}</dd>
        <dt class="text-muted-foreground">优先级</dt>
        <dd class="text-foreground">{{ maintenancePriorityLabel(workOrder.priority) }}</dd>
        <dt class="text-muted-foreground">指派</dt>
        <dd class="break-words text-foreground">{{ assignment }}</dd>
        <dt class="text-muted-foreground">版本</dt>
        <dd class="text-foreground">版本 {{ workOrder.version ?? 0 }}</dd>
        <dt class="text-muted-foreground">开单时间</dt>
        <dd class="text-foreground">{{ formatMaintenanceDateTime(workOrder.openedAtUtc) }}</dd>
      </dl>
    </section>

    <section
      data-testid="maintenance-read-only-state"
      class="rounded-xl border border-brand/30 bg-brand/5 p-4 text-sm"
    >
      <h2 class="font-semibold text-foreground">{{ terminal ? '终态只读' : '工单详情只读' }}</h2>
      <p class="mt-1 text-muted-foreground">
        {{
          terminal
            ? '工单已进入终态，仅可查看。'
            : '本页面只展示系统确认的动作资格，不自行推断或执行生命周期动作。'
        }}
      </p>
    </section>

    <section class="space-y-2 rounded-xl border border-border bg-card p-4">
      <h2 class="text-sm font-semibold text-foreground">当前可执行动作</h2>
      <div v-if="allowedActions.length" class="flex flex-wrap gap-2">
        <NvMobileTag v-for="action in allowedActions" :key="action" variant="brand">
          {{ maintenanceWorkOrderActionLabel(action) }}
        </NvMobileTag>
      </div>
      <p v-else class="text-sm text-muted-foreground">无可执行动作</p>

      <div v-if="blockReasons.length" class="space-y-1 pt-2">
        <p v-for="reason in blockReasons" :key="reason" class="text-sm text-destructive">
          {{ maintenanceWorkOrderBlockReasonLabel(reason) }}
        </p>
      </div>
    </section>

    <section class="space-y-3 rounded-xl border border-border bg-card p-4">
      <h2 class="text-sm font-semibold text-foreground">生命周期</h2>
      <ol v-if="lifecycle.length" class="space-y-3">
        <li
          v-for="(event, index) in lifecycle"
          :key="`${event.resultingVersion ?? index}:${event.occurredAtUtc ?? ''}`"
          class="border-l-2 border-brand/30 pl-3"
        >
          <p class="text-sm font-medium text-foreground">
            {{ maintenanceWorkOrderStatusLabel(event.fromStatus) }} →
            {{ maintenanceWorkOrderStatusLabel(event.toStatus) }}
          </p>
          <p class="mt-1 text-sm text-muted-foreground">
            {{ maintenanceWorkOrderActionLabel(event.action) }} · {{ event.reason || '原因未记录' }}
          </p>
          <p class="mt-1 text-xs text-muted-foreground">
            {{ event.actorPrincipalId ? '操作人已记录' : '操作人未记录' }} · 版本
            {{ event.resultingVersion ?? '未记录' }} ·
            {{ formatMaintenanceDateTime(event.occurredAtUtc) }}
          </p>
        </li>
      </ol>
      <p v-else class="text-sm text-muted-foreground">暂无生命周期记录</p>
    </section>
  </article>
</template>
