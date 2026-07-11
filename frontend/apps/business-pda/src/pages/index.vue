<script setup lang="ts">
import { PDA_TASK_KINDS } from '@nerv-iip/business-core'
import { useBusinessEquipmentAlarms } from '@/composables/useBusinessEquipmentAlarms'
import { NvAppShellMobile, NvMobileBadge, NvScanBar } from '@nerv-iip/ui-mobile'
import { ref } from 'vue'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工作台',
  },
})

const router = useRouter()

// 工作台报警角标：未确认报警数与「查看报警」入口联动（确认/搁置后经查询失效自动回落）。
const { unacknowledgedCount } = useBusinessEquipmentAlarms()

const lastScan = ref('')

function onScan(value: string) {
  // TODO(M5): 扫码直达（/scan 路由 + 扫码解析端点）落地后改为按解析结果导航。
  // 现阶段 /scan 尚不存在，只做诚实的页内反馈，不做假跳转。
  lastScan.value = value
}

function openTask(route: string, ready: boolean) {
  if (!ready) return
  router.push(route).catch(() => {})
}
</script>

<template>
  <NvAppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">工作台</h1>
      </div>
    </template>

    <div class="space-y-6 p-4">
      <NvScanBar placeholder="扫描工单 / 库位 / 物料 / 设备" @scan="onScan" />

      <p v-if="lastScan" data-testid="last-scan" class="-mt-3 text-sm text-foreground">
        已扫码：{{ lastScan }}
        <span class="block text-xs text-muted-foreground">
          扫码直达将在后续里程碑（M5）落地，当前仅回显扫码内容。
        </span>
      </p>

      <section>
        <h2 class="mb-2 text-sm font-medium text-muted-foreground">我的任务</h2>
        <div
          class="rounded-lg border border-dashed border-border bg-card px-4 py-8 text-center text-sm text-muted-foreground"
        >
          暂无分配给你的任务
        </div>
      </section>

      <section>
        <h2 class="mb-2 text-sm font-medium text-muted-foreground">快捷应用</h2>
        <div class="grid grid-cols-3 gap-3">
          <button
            v-for="kind in PDA_TASK_KINDS"
            :key="kind.id"
            type="button"
            :disabled="!kind.routeReady"
            class="min-h-touch relative flex flex-col items-center justify-center gap-1 rounded-xl border border-border bg-card p-3 text-center text-sm text-foreground disabled:opacity-40"
            @click="openTask(kind.route, kind.routeReady)"
          >
            <NvMobileBadge
              v-if="kind.id === 'equipment.alarms' && unacknowledgedCount > 0"
              data-testid="alarm-badge"
              :count="unacknowledgedCount"
              class="absolute right-2 top-2"
            />
            <span>{{ kind.label }}</span>
          </button>
        </div>
      </section>
    </div>
  </NvAppShellMobile>
</template>
