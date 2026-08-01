<script setup lang="ts">
import { ClipboardCheck, Factory, PackageOpen, Wrench } from '@lucide/vue'
import { NvAppShellMobile, NvCellGroup, NvNavBar } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'

import PdaNavigationCell from '@/components/navigation/PdaNavigationCell.vue'
import { HOME_PERMISSIONS, usePdaIdentity } from '@/composables/useWorkbenchHome'
import { canAccessMaintenanceWorkOrderReadModel } from '@/permissions/maintenanceReadModelAccess'

definePage({ meta: { requiresAuth: true, title: '任务' } })

const identity = usePdaIdentity()
const canSeeMesOperations = computed(() => identity.can(HOME_PERMISSIONS.mesOperations))
const canSeeQualitySelfTasks = computed(() => identity.can(HOME_PERMISSIONS.quality))
const canSeeMaintenanceSelfQueue = computed(() =>
  canAccessMaintenanceWorkOrderReadModel(identity.permissionCodes.value),
)
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
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="任务" /></template>

    <div class="space-y-5 p-4">
      <section v-if="canSeeMesOperations">
        <h1 class="mb-2 text-sm font-semibold text-foreground">生产作业</h1>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <PdaNavigationCell
            to="/mes/operation"
            title="生产作业"
            note="查看当前账号可执行的生产作业"
            accessible-name="生产作业，查看当前账号可执行的生产作业"
          >
            <template #icon><Factory /></template>
          </PdaNavigationCell>
        </NvCellGroup>
      </section>

      <section v-if="canSeeQualitySelfTasks">
        <h2 class="mb-2 text-sm font-semibold text-foreground">质量任务</h2>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <PdaNavigationCell
            data-testid="quality-self-tasks"
            to="/quality/tasks"
            title="我的质检任务"
            note="查看分派给当前账号的质检任务"
          >
            <template #icon><ClipboardCheck /></template>
          </PdaNavigationCell>
        </NvCellGroup>
      </section>

      <section v-if="canSeeMaintenanceSelfQueue">
        <h2 class="mb-2 text-sm font-semibold text-foreground">维修任务</h2>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <PdaNavigationCell
            data-testid="maintenance-self-work-orders"
            to="/equipment/work-orders"
            title="维修工单"
            note="查看分派给当前维修人员的工单与设备位置"
          >
            <template #icon><Wrench /></template>
          </PdaNavigationCell>
        </NvCellGroup>
      </section>

      <section v-if="warehouseEntrances.length">
        <h2 class="mb-2 text-sm font-semibold text-foreground">仓储作业</h2>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <PdaNavigationCell
            v-for="entry in warehouseEntrances"
            :key="entry.route"
            :to="entry.route"
            :title="entry.title"
            :note="entry.note"
            :accessible-name="`${entry.title}，${entry.note}`"
          >
            <template #icon><PackageOpen /></template>
          </PdaNavigationCell>
        </NvCellGroup>
      </section>
    </div>
  </NvAppShellMobile>
</template>
