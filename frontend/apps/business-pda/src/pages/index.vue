<script setup lang="ts">
import type { Component } from 'vue'
import {
  BadgeCheck,
  Bell,
  ClipboardCheck,
  ClipboardList,
  Cog,
  FilePen,
  ListChecks,
  PackageCheck,
  PackageMinus,
  PackagePlus,
  PackageSearch,
  Wrench,
} from '@lucide/vue'
import { operationTaskStatusLabel, PDA_TASK_KINDS } from '@nerv-iip/business-core'
import { useUnacknowledgedAlarmCount } from '@/composables/useBusinessEquipmentAlarms'
import {
  HOME_PERMISSIONS,
  useMyDispatchTasks,
  usePdaIdentity,
  usePendingInspectionSummary,
  useWarehouseSummary,
} from '@/composables/useWorkbenchHome'
import {
  NvAppShellMobile,
  NvCell,
  NvCellGroup,
  NvMobileAvatar,
  NvMobileGrid,
  NvMobileSkeleton,
  NvMobileTag,
  NvScanBar,
  type GridItem,
} from '@nerv-iip/ui-mobile'
import { computed, ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工作台',
  },
})

const router = useRouter()
const identity = usePdaIdentity()
const myTasks = useMyDispatchTasks()
const warehouse = useWarehouseSummary()
const inspection = usePendingInspectionSummary()

// 工作台报警角标：服务端 status=raised total（全量未确认数，不受列表首页 take 上限影响），
// 与「查看报警」入口联动（确认/搁置后经查询失效自动回落）。仅有报警读权限时查询。
const canSeeAlarms = computed(() => identity.can(HOME_PERMISSIONS.alarms))
const { unacknowledgedCount } = useUnacknowledgedAlarmCount(canSeeAlarms)

const lastScan = ref('')

function onScan(value: string) {
  // TODO(M5): 扫码直达（/scan 路由 + 扫码解析端点）落地后改为按解析结果导航。
  // 现阶段 /scan 尚不存在，只做诚实的页内反馈，不做假跳转。
  lastScan.value = value
}

/** 首页身份行：岗位 · 班组（都有才拼接，缺失不占位）。 */
const identitySubtitle = computed(() => {
  const worker = identity.worker.value
  const parts = [worker?.jobTitle, worker?.teams?.[0]?.teamName].filter(Boolean)
  return parts.join(' · ')
})

const MY_TASKS_PREVIEW = 5
const myTasksPreview = computed(() => myTasks.openTasks.value.slice(0, MY_TASKS_PREVIEW))

type TagVariant = 'default' | 'brand' | 'success' | 'warning' | 'danger'
function taskTagVariant(status?: string): TagVariant {
  switch (status) {
    case 'InProgress':
      return 'brand'
    case 'Paused':
      return 'warning'
    case 'ScheduleInvalidated':
      return 'danger'
    default:
      return 'default'
  }
}

function taskNote(task: (typeof myTasksPreview.value)[number]) {
  const parts = [
    task.operationCode ? `工序 ${task.operationCode}` : '',
    task.workCenterName || task.workCenterCode || task.workCenterId || '',
    task.deviceAssetName || task.deviceAssetCode || '',
  ].filter(Boolean)
  return parts.join(' · ')
}

const INSPECTION_PREVIEW = 3
const inspectionPreview = computed(() => inspection.tasks.value.slice(0, INSPECTION_PREVIEW))

/** 快捷应用按登录人权限裁剪：无读权限的入口不出现（点了也是 403）。 */
const KIND_PERMISSIONS: Record<string, string> = {
  'wms.inbound': 'business.wms.receipts.read',
  'wms.putaway': 'business.wms.receipts.read',
  'wms.pick': 'business.wms.shipments.read',
  'wms.review': 'business.wms.receipts.read',
  'wms.count': 'business.wms.receipts.read',
  'mes.report': 'business.mes.reporting.read',
  'mes.issue': 'business.mes.materials.read',
  'mes.receipt': 'business.mes.receipts.read',
  'mes.operation': 'business.mes.operations.read',
  'equipment.repair': 'business.maintenance.work-orders.read',
  'equipment.inspect': 'business.maintenance.plans.read',
  'equipment.alarms': 'business.iiot.alarms.read',
  'quality.tasks': 'business.quality.inspection-records.read',
}

const KIND_ICONS: Record<string, Component> = {
  'wms.inbound': PackageCheck,
  'wms.putaway': PackagePlus,
  'wms.pick': PackageSearch,
  'wms.review': ClipboardCheck,
  'wms.count': ListChecks,
  'mes.report': FilePen,
  'mes.issue': PackageMinus,
  'mes.receipt': PackagePlus,
  'mes.operation': Cog,
  'equipment.repair': Wrench,
  'equipment.inspect': ClipboardList,
  'equipment.alarms': Bell,
  'quality.tasks': BadgeCheck,
}

const visibleKinds = computed(() =>
  PDA_TASK_KINDS.filter((kind) => {
    const permission = KIND_PERMISSIONS[kind.id]
    return kind.routeReady && (!permission || identity.can(permission))
  }),
)

const gridItems = computed<GridItem[]>(() =>
  visibleKinds.value.map((kind) => ({
    key: kind.id,
    text: kind.label,
    icon: KIND_ICONS[kind.id],
    badge:
      kind.id === 'equipment.alarms' && canSeeAlarms.value && unacknowledgedCount.value > 0
        ? unacknowledgedCount.value
        : undefined,
  })),
)

function onGridSelect(item: GridItem) {
  const kind = visibleKinds.value.find((k) => k.id === item.key)
  if (kind) router.push(kind.route).catch(() => {})
}

function openRoute(route: string) {
  router.push(route).catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="flex items-center gap-3 px-4 py-3">
        <NvMobileAvatar :name="identity.displayName.value" size="md" />
        <div class="min-w-0 flex-1">
          <h1 class="truncate text-base font-semibold text-foreground" data-testid="home-name">
            {{ identity.displayName.value || '工作台' }}
          </h1>
          <p v-if="identitySubtitle" class="truncate text-xs text-muted-foreground">
            {{ identitySubtitle }}
          </p>
        </div>
        <NvMobileTag v-if="identity.worker.value?.employeeNo" size="sm">
          {{ identity.worker.value.employeeNo }}
        </NvMobileTag>
      </div>
    </template>

    <div class="space-y-5 p-4">
      <NvScanBar placeholder="扫描工单 / 库位 / 物料 / 设备" @scan="onScan" />

      <p v-if="lastScan" data-testid="last-scan" class="-mt-2 text-sm text-foreground">
        已扫码：{{ lastScan }}
      </p>

      <!-- 我的任务（派工到本人的工序任务，仅有派工读权限的产线角色可见） -->
      <section v-if="myTasks.enabled.value" data-testid="home-my-tasks">
        <div class="mb-2 flex items-baseline justify-between">
          <h2 class="text-sm font-medium text-muted-foreground">我的任务</h2>
          <div class="flex items-center gap-2 text-xs text-muted-foreground">
            <span>
              进行中
              <span class="font-semibold text-foreground">{{ myTasks.inProgressCount.value }}</span>
            </span>
            <span>
              待开工
              <span class="font-semibold text-foreground">{{ myTasks.queuedCount.value }}</span>
            </span>
          </div>
        </div>

        <div v-if="myTasks.pending.value" class="space-y-2">
          <NvMobileSkeleton variant="rect" class="h-12" />
          <NvMobileSkeleton variant="rect" class="h-12" />
        </div>
        <NvCellGroup
          v-else-if="myTasksPreview.length > 0"
          class="overflow-hidden rounded-xl border border-border"
        >
          <NvCell
            v-for="task in myTasksPreview"
            :key="task.operationTaskId"
            :title="task.workOrderNo || task.workOrderId || '工单'"
            :note="taskNote(task)"
            arrow
            @click="openRoute('/mes/operation')"
          >
            <template #value>
              <NvMobileTag :variant="taskTagVariant(task.status)" size="sm">
                {{ operationTaskStatusLabel(task.status) }}
              </NvMobileTag>
            </template>
          </NvCell>
          <NvCell
            v-if="myTasks.openTasks.value.length > myTasksPreview.length"
            :title="`查看全部 ${myTasks.openTasks.value.length} 项任务`"
            arrow
            class="text-muted-foreground"
            @click="openRoute('/mes/operation')"
          />
        </NvCellGroup>
        <div
          v-else
          class="rounded-xl border border-dashed border-border bg-card px-4 py-6 text-center text-sm text-muted-foreground"
        >
          暂无派给我的任务
        </div>
      </section>

      <!-- 仓储任务（有 WMS 读权限的仓储角色可见） -->
      <section v-if="warehouse.enabled.value" data-testid="home-warehouse">
        <h2 class="mb-2 text-sm font-medium text-muted-foreground">仓储任务</h2>
        <div class="grid grid-cols-4 gap-2">
          <button
            v-for="entry in warehouse.entries.value"
            :key="entry.key"
            type="button"
            class="flex min-h-touch flex-col items-center justify-center gap-0.5 rounded-xl border border-border bg-card py-3 active:bg-accent"
            @click="openRoute(entry.route)"
          >
            <span class="text-lg font-semibold tabular-nums text-foreground">{{
              entry.count
            }}</span>
            <span class="text-xs text-muted-foreground">{{ entry.label }}</span>
          </button>
        </div>
      </section>

      <!-- 检验任务（有质检读权限的检验员可见） -->
      <section v-if="inspection.enabled.value" data-testid="home-inspection">
        <div class="mb-2 flex items-baseline justify-between">
          <h2 class="text-sm font-medium text-muted-foreground">待检任务</h2>
          <span class="text-xs text-muted-foreground">
            共 <span class="font-semibold text-foreground">{{ inspection.total.value }}</span> 项
          </span>
        </div>
        <NvCellGroup
          v-if="inspectionPreview.length > 0"
          class="overflow-hidden rounded-xl border border-border"
        >
          <NvCell
            v-for="task in inspectionPreview"
            :key="task.inspectionTaskId"
            :title="task.skuCode || '检验任务'"
            :note="task.batchNo ? `批次 ${task.batchNo}` : ''"
            :value="task.quantity != null ? `${task.quantity} ${task.uomCode ?? ''}` : ''"
            arrow
            @click="openRoute('/quality/tasks')"
          />
        </NvCellGroup>
        <div
          v-else-if="!inspection.pending.value"
          class="rounded-xl border border-dashed border-border bg-card px-4 py-6 text-center text-sm text-muted-foreground"
        >
          暂无待检任务
        </div>
      </section>

      <!-- 快捷应用（按权限裁剪） -->
      <section data-testid="home-apps">
        <h2 class="mb-2 text-sm font-medium text-muted-foreground">快捷应用</h2>
        <NvMobileGrid :items="gridItems" :columns="4" bordered @select="onGridSelect" />
      </section>
    </div>
  </NvAppShellMobile>
</template>
