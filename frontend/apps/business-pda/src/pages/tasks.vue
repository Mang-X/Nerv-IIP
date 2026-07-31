<script setup lang="ts">
import { ClipboardCheck, Factory, PackageOpen } from '@lucide/vue'
import { operationTaskStatusLabel } from '@nerv-iip/business-core'
import { NvAppShellMobile, NvCell, NvCellGroup, NvMobileTag, NvNavBar } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import {
  HOME_PERMISSIONS,
  useMyDispatchTasks,
  usePdaIdentity,
} from '@/composables/useWorkbenchHome'

definePage({ meta: { requiresAuth: true, title: '任务' } })

const router = useRouter()
const identity = usePdaIdentity()
const myTasks = useMyDispatchTasks()

const personalTasks = computed(() => myTasks.openTasks.value)
const canSeeQualitySelfTasks = computed(() => identity.can(HOME_PERMISSIONS.quality))
const warehouseEntrances = computed(() => {
  const entries: Array<{ title: string; note: string; route: string }> = []
  if (identity.can(HOME_PERMISSIONS.wmsReceipts)) {
    entries.push({ title: '收货与上架', note: '按当前作业范围查看', route: '/wms/inbound' })
  }
  if (identity.can(HOME_PERMISSIONS.wmsShipments)) {
    entries.push({ title: '拣货与复核', note: '按当前作业范围查看', route: '/wms/pick' })
  }
  if (identity.can(HOME_PERMISSIONS.wmsCounts)) {
    entries.push({ title: '盘点执行', note: '按当前作业范围查看', route: '/wms/count' })
  }
  return entries
})

function openRoute(route: string) {
  router.push(route).catch(() => undefined)
}
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="任务" /></template>

    <div class="space-y-5 p-4">
      <section v-if="myTasks.enabled.value">
        <div class="mb-2 flex items-center justify-between">
          <h1 class="text-sm font-semibold text-foreground">我的生产任务</h1>
          <span class="text-xs text-muted-foreground">
            进行中 {{ myTasks.inProgressCount.value }} · 待开工 {{ myTasks.queuedCount.value }}
          </span>
        </div>
        <NvCellGroup
          v-if="personalTasks.length"
          class="overflow-hidden rounded-xl border border-border"
        >
          <NvCell
            v-for="task in personalTasks"
            :key="task.operationTaskId"
            :title="task.workOrderNo || task.workOrderId || '生产任务'"
            :note="task.operationCode ? `工序 ${task.operationCode}` : undefined"
            arrow
            @click="openRoute('/mes/operation')"
          >
            <template #icon><Factory /></template>
            <template #value>
              <NvMobileTag size="sm">{{ operationTaskStatusLabel(task.status) }}</NvMobileTag>
            </template>
          </NvCell>
        </NvCellGroup>
        <div
          v-else-if="!myTasks.pending.value"
          class="rounded-xl border border-dashed border-border bg-card p-5 text-center text-sm text-muted-foreground"
        >
          暂无派给我的生产任务
        </div>
      </section>

      <section v-if="canSeeQualitySelfTasks">
        <h2 class="mb-2 text-sm font-semibold text-foreground">质量任务</h2>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <NvCell
            data-testid="quality-self-tasks"
            title="我的质检任务"
            note="服务端按当前主体 Self 范围返回"
            arrow
            @click="openRoute('/quality/tasks')"
          >
            <template #icon><ClipboardCheck /></template>
          </NvCell>
        </NvCellGroup>
      </section>

      <section v-if="warehouseEntrances.length">
        <h2 class="mb-2 text-sm font-semibold text-foreground">仓储作业</h2>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <NvCell
            v-for="entry in warehouseEntrances"
            :key="entry.route"
            :title="entry.title"
            :note="entry.note"
            arrow
            @click="openRoute(entry.route)"
          >
            <template #icon><PackageOpen /></template>
          </NvCell>
        </NvCellGroup>
      </section>
    </div>
  </NvAppShellMobile>
</template>
