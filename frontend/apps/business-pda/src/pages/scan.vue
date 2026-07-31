<script setup lang="ts">
import { ClipboardCheck, FilePen, PackageCheck, Wrench } from '@lucide/vue'
import { NvAppShellMobile, NvCellGroup, NvNavBar, NvScanBar } from '@nerv-iip/ui-mobile'
import { computed, shallowRef } from 'vue'

import PdaNavigationCell from '@/components/navigation/PdaNavigationCell.vue'
import { usePdaIdentity } from '@/composables/useWorkbenchHome'

definePage({ meta: { requiresAuth: true, title: '扫码' } })

const identity = usePdaIdentity()
const scannedCode = shallowRef('')
const navigationFocused = shallowRef(false)

const workEntrances = computed(() =>
  [
    {
      title: '生产报工',
      route: '/mes/report',
      permission: 'business.mes.reporting.read',
      icon: FilePen,
    },
    {
      title: '收货入库',
      route: '/wms/inbound',
      permission: 'business.wms.receipts.read',
      icon: PackageCheck,
    },
    {
      title: '质检任务',
      route: '/quality/tasks',
      permission: 'business.quality.inspection-records.read',
      icon: ClipboardCheck,
    },
    {
      title: '设备报修',
      route: '/equipment/repair',
      permission: 'business.maintenance.work-orders.read',
      icon: Wrench,
    },
  ].filter((entry) => identity.can(entry.permission)),
)
</script>

<template>
  <NvAppShellMobile>
    <template #header><NvNavBar title="扫码" /></template>

    <div class="space-y-5 p-4">
      <NvScanBar
        placeholder="扫描工单 / 库位 / 物料 / 设备"
        :active="!navigationFocused"
        @scan="scannedCode = $event"
      />

      <div
        v-if="scannedCode"
        data-testid="scan-result"
        class="rounded-xl border border-brand/30 bg-brand-subtle p-4"
      >
        <div class="text-xs font-medium text-brand">已读取</div>
        <div class="mt-1 break-all font-mono text-base font-semibold text-foreground">
          {{ scannedCode }}
        </div>
      </div>

      <section v-if="workEntrances.length">
        <h1 class="mb-2 text-sm font-semibold text-foreground">选择作业</h1>
        <NvCellGroup class="overflow-hidden rounded-xl border border-border">
          <PdaNavigationCell
            v-for="entry in workEntrances"
            :key="entry.route"
            :to="entry.route"
            :title="entry.title"
            @focusin="navigationFocused = true"
            @focusout="navigationFocused = false"
          >
            <template #icon><component :is="entry.icon" /></template>
          </PdaNavigationCell>
        </NvCellGroup>
      </section>
    </div>
  </NvAppShellMobile>
</template>
