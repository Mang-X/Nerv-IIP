<script setup lang="ts">
import { PDA_TASK_KINDS } from '@nerv-iip/business-core'
import { AppShellMobile, ScanBar } from '@nerv-iip/ui-mobile'
import { useRouter } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '工作台',
  },
})

const router = useRouter()

function onScan(value: string) {
  router.push({ path: '/scan', query: { code: value } }).catch(() => {})
}

function openTask(route: string, ready: boolean) {
  if (!ready) return
  router.push(route).catch(() => {})
}
</script>

<template>
  <AppShellMobile>
    <template #header>
      <div class="px-4 py-3">
        <h1 class="text-lg font-semibold text-foreground">工作台</h1>
      </div>
    </template>

    <div class="space-y-6 p-4">
      <ScanBar placeholder="扫描工单 / 库位 / 物料 / 设备" @scan="onScan" />

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
            class="min-h-touch flex flex-col items-center justify-center gap-1 rounded-xl border border-border bg-card p-3 text-center text-sm text-foreground disabled:opacity-40"
            @click="openTask(kind.route, kind.routeReady)"
          >
            <span>{{ kind.label }}</span>
          </button>
        </div>
      </section>
    </div>
  </AppShellMobile>
</template>
