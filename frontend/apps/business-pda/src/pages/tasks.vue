<script setup lang="ts">
import { ClipboardCheck, Factory, PackageOpen } from '@lucide/vue'
import { NvAppShellMobile, NvCell, NvCellGroup, NvNavBar } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

import { HOME_PERMISSIONS, usePdaIdentity } from '@/composables/useWorkbenchHome'

definePage({ meta: { requiresAuth: true, title: '任务' } })

const router = useRouter()
const identity = usePdaIdentity()
const canSeeMesOperations = computed(() => identity.can(HOME_PERMISSIONS.mesOperations))
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
      <section v-if="canSeeMesOperations">
        <h1 class="mb-2 text-sm font-semibold text-foreground">生产作业</h1>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <NvCell
            title="生产作业"
            note="服务端按当前主体与授权作业范围过滤"
            arrow
            @click="openRoute('/mes/operation')"
          >
            <template #icon><Factory /></template>
          </NvCell>
        </NvCellGroup>
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
