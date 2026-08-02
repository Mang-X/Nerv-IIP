<script setup lang="ts">
import type {
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
} from '../maintenanceWorkOrderPresentation'
import type {
  AuthoritativeMaintenanceWorkOrderDetail,
  MaintenanceWorkOrderIdentityDirectory,
} from '@/composables/useMaintenanceSelfWorkOrders'

const props = defineProps<{
  workOrder: AuthoritativeMaintenanceWorkOrderDetail
  device?: BusinessConsoleResourceItem | BusinessConsoleMasterDataResourceDetail
  identities?: MaintenanceWorkOrderIdentityDirectory
  identitiesUnavailable?: boolean
  identityPending?: boolean
}>()

const terminal = computed(() => isMaintenanceTerminal(props.workOrder))
const allowedActions = computed(() => props.workOrder.allowedActions)
const blockReasons = computed(() => props.workOrder.blockReasons)
const lifecycle = computed(() => props.workOrder.lifecycle)
const assignment = computed(() => {
  if (props.identityPending || props.identitiesUnavailable || !props.identities) {
    return '身份资料暂不可用'
  }
  const technician = props.identities.users[props.workOrder.assignedTechnicianUserId]
  const team = props.workOrder.assignedTeamId
    ? props.identities.teams[props.workOrder.assignedTeamId]
    : undefined
  if (!technician || (props.workOrder.assignedTeamId && !team)) return '身份资料暂不可用'
  const parts = [
    `维修人员 ${technician}`,
    props.workOrder.assignedTeamId ? `班组 ${team}` : '未指派班组',
  ].filter((part): part is string => Boolean(part))
  return parts.join(' · ')
})

function userName(userId: string | null | undefined) {
  if (!userId) return '未指派'
  return props.identities?.users[userId] ?? '身份资料暂不可用'
}

function teamName(teamId: string | null | undefined) {
  if (!teamId) return '未指派'
  return props.identities?.teams[teamId] ?? '身份资料暂不可用'
}
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
        <dd class="text-foreground">版本 {{ workOrder.version }}</dd>
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
          :key="`${event.resultingVersion}:${event.occurredAtUtc}:${index}`"
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
            操作人 {{ userName(event.actorPrincipalId) }} · 技师快照
            {{ userName(event.technicianUserId) }} · 班组快照 {{ teamName(event.teamId) }} · 版本
            {{ event.resultingVersion }} ·
            {{ formatMaintenanceDateTime(event.occurredAtUtc) }}
          </p>
        </li>
      </ol>
      <p v-else class="text-sm text-muted-foreground">暂无生命周期记录</p>
    </section>
  </article>
</template>
