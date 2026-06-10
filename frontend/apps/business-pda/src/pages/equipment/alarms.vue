<script setup lang="ts">
import { useBusinessEquipmentAlarms } from '@/composables/useBusinessEquipmentAlarms'
import { alarmSeverityLabel } from '@nerv-iip/business-core'
import { AppShellMobile, ListRow, ScanBar } from '@nerv-iip/ui-mobile'
import { computed } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '查看报警',
  },
})

const router = useRouter()

const {
  filters,
  alarms,
  pending,
  error,
} = useBusinessEquipmentAlarms()

// 当前是否按设备过滤（用于展示/清除过滤）。
const filteredDevice = computed(() => filters.deviceAssetId)

// ScanBar 扫设备码 → 服务端按 deviceAssetId 过滤。
function onScan(value: string) {
  filters.deviceAssetId = value
}

function clearFilter() {
  filters.deviceAssetId = undefined
}

// 行标题：设备 · 报警码（均为业务码，可显示）。
function alarmTitle(item: { deviceAssetId?: string, alarmCode?: string }) {
  const device = item.deviceAssetId ?? '未知设备'
  const code = item.alarmCode ?? '—'
  return `${device} · 报警码 ${code}`
}

// 行副标题：级别中文 + 发生时间（alarmEventId/externalAlarmId 仅作 key / 透传，不外显）。
function alarmSubtitle(item: { severity?: string, raisedAtUtc?: string }) {
  const parts = [alarmSeverityLabel(item.severity)]
  if (item.raisedAtUtc) {
    parts.push(new Date(item.raisedAtUtc).toLocaleString('zh-CN'))
  }
  return parts.join(' · ')
}

// 去报修：把设备 + 来源报警事件 ID 作为上下文带入报修页（report.vue 消费 query 预填）。
function goRepair(item: { deviceAssetId?: string, alarmEventId?: string }) {
  void router.push({
    path: '/equipment/repair',
    query: {
      deviceAssetId: item.deviceAssetId,
      sourceAlarmId: item.alarmEventId,
    },
  })
}
</script>

<template>
  <AppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">查看报警</h1>
      </div>
    </template>

    <div class="space-y-4 p-4">
      <!-- 按设备过滤 -->
      <section class="space-y-2">
        <ScanBar placeholder="扫描设备码筛选报警" @scan="onScan" />
        <div
          v-if="filteredDevice"
          class="flex items-center justify-between rounded-lg border border-border bg-card px-4 py-2 text-sm"
        >
          <span class="truncate text-foreground">仅显示设备 {{ filteredDevice }}</span>
          <button
            data-testid="clear-filter"
            type="button"
            class="ml-3 shrink-0 rounded-md border border-border px-3 py-1 text-sm text-foreground"
            @click="clearFilter"
          >
            清除筛选
          </button>
        </div>
      </section>

      <!-- 报警列表 -->
      <section class="space-y-2">
        <h2 class="text-sm font-medium text-muted-foreground">设备报警</h2>

        <p
          v-if="error"
          data-testid="alarms-error"
          class="rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm text-destructive"
        >
          报警加载失败，请稍后重试。
        </p>

        <div v-else-if="pending" class="px-4 py-6 text-center text-sm text-muted-foreground">
          加载中…
        </div>

        <div
          v-else-if="alarms.length === 0"
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          暂无设备报警
        </div>

        <div v-else class="overflow-hidden rounded-lg border border-border">
          <ListRow
            v-for="item in alarms"
            :key="item.alarmEventId"
            :title="alarmTitle(item)"
            :subtitle="alarmSubtitle(item)"
            :interactive="false"
          >
            <template #trailing>
              <button
                :data-testid="`repair-${item.alarmEventId}`"
                type="button"
                class="shrink-0 rounded-md bg-primary px-3 py-1.5 text-sm font-medium text-primary-foreground"
                @click="goRepair(item)"
              >
                去报修
              </button>
            </template>
          </ListRow>
        </div>
      </section>
    </div>
  </AppShellMobile>
</template>
