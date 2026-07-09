<script setup lang="ts">
import { computed } from 'vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_DOMAINS, DOMAIN_SIDE_NAV, permittedBy } from '@/navigation'
import { BUSINESS_DOMAIN_PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { useBusinessWorkbenchSummary } from '@/composables/useBusinessWorkbench'
import { NvBadge } from '@nerv-iip/ui'
import type { SideNav } from '@nerv-iip/app-shell'
import type {
  BusinessConsoleWorkbenchAlertItem,
  BusinessConsoleWorkbenchKpiItem,
  BusinessConsoleWorkbenchMessageItem,
  BusinessConsoleWorkbenchSourceStatus,
  BusinessConsoleWorkbenchTodoItem,
} from '@nerv-iip/api-client'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务工作台',
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench],
  },
})

interface ShortcutGroup {
  title: string
  items: Array<{
    path: string
    title: string
  }>
}

const auth = useAuthStore()
const {
  alertItems,
  availableKpis,
  messageItems,
  sourceStatuses,
  summary,
  summaryError,
  summaryPending,
  todoItems,
} = useBusinessWorkbenchSummary()

const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const sourceStatusList = computed(() => sourceStatuses.value.map(describeSourceStatus))
const visibleShortcutGroups = computed<ShortcutGroup[]>(() => {
  const domainTitles = new Map(BUSINESS_DOMAINS.map((domain) => [domain.id, domain.title]))

  return Object.entries(DOMAIN_SIDE_NAV)
    .filter(([domainId]) => domainId !== 'workbench')
    .map(([domainId, groups]) => {
      const items = permittedShortcutItems(groups, permissionCodes.value)
      return {
        title: domainTitles.get(domainId) ?? '业务入口',
        items,
      }
    })
    .filter((group) => group.items.length > 0)
})

function permittedShortcutItems(groups: SideNav, codes: string[] | undefined) {
  return groups.flatMap((group) =>
    permittedBy(group.items, codes).map((item) => ({
      path: typeof item.to === 'string' ? item.to : (item.to.path ?? '/'),
      title: item.title,
    })),
  )
}

function normalize(value: string | null | undefined) {
  return value?.trim() ?? ''
}

function sourceLabel(source: string | null | undefined) {
  const labels: Record<string, string> = {
    BusinessApproval: '审批',
    BusinessInventory: '库存管理',
    BusinessMES: '制造执行',
    BusinessQuality: '质量管理',
    IndustrialTelemetry: '设备预警',
    Notification: '消息通知',
  }

  const key = normalize(source)
  return labels[key] ?? '业务来源'
}

function statusLabel(status: string | null | undefined) {
  const labels: Record<string, string> = {
    available: '已接入',
    forbidden: '无权限',
    unavailable: '暂不可用',
    unsupported: '未接入',
  }

  return labels[normalize(status).toLowerCase()] ?? '待确认'
}

function statusVariant(status: string | null | undefined): 'success' | 'warning' | 'neutral' {
  const value = normalize(status).toLowerCase()
  if (value === 'available') return 'success'
  if (value === 'forbidden' || value === 'unavailable') return 'warning'
  return 'neutral'
}

function describeSourceStatus(status: BusinessConsoleWorkbenchSourceStatus) {
  return {
    label: sourceLabel(status.source),
    source: normalize(status.source),
    status: normalize(status.status),
    statusLabel: statusLabel(status.status),
    variant: statusVariant(status.status),
  }
}

function kpiLabel(kpi: BusinessConsoleWorkbenchKpiItem) {
  const labels: Record<string, string> = {
    openNcrs: '未关闭质量异常',
    releasedWorkOrders: '已下达工单',
  }

  return labels[normalize(kpi.key)] ?? (normalize(kpi.label) || '业务指标')
}

function kpiSource(kpi: BusinessConsoleWorkbenchKpiItem) {
  return sourceLabel(kpi.source)
}

function todoLabel(item: BusinessConsoleWorkbenchTodoItem) {
  const source = sourceLabel(item.source)
  const typeLabels: Record<string, string> = {
    'inventory-count': '盘点任务',
    'purchase-order': '采购单据',
    quality: '质量处置',
  }
  const type = typeLabels[normalize(item.itemType)] ?? '待办事项'
  return `${source} · ${type}`
}

function todoMeta(item: BusinessConsoleWorkbenchTodoItem) {
  const parts = [statusLabel(item.status)]
  if (item.dueAtUtc) {
    parts.push(`到期 ${formatDateTime(item.dueAtUtc)}`)
  }
  if (item.referenceId) {
    parts.push(item.referenceId)
  }
  return parts.join(' · ')
}

function messageLabel(item: BusinessConsoleWorkbenchMessageItem) {
  const severity = severityLabel(item.severity)
  return `${severity}消息`
}

function messageMeta(item: BusinessConsoleWorkbenchMessageItem) {
  const parts = [statusLabel(item.status)]
  if (item.createdAtUtc) {
    parts.push(formatDateTime(item.createdAtUtc))
  }
  return parts.join(' · ')
}

function alertLabel(item: BusinessConsoleWorkbenchAlertItem) {
  const device = normalize(item.deviceAssetId) || '设备'
  const code = normalize(item.alarmCode) || '报警'
  return `${device} · ${code}`
}

function alertMeta(item: BusinessConsoleWorkbenchAlertItem) {
  const parts = [severityLabel(item.severity)]
  if (item.raisedAtUtc) {
    parts.push(formatDateTime(item.raisedAtUtc))
  }
  return parts.join(' · ')
}

function severityLabel(severity: string | null | undefined) {
  const labels: Record<string, string> = {
    critical: '紧急',
    error: '严重',
    info: '提示',
    warning: '预警',
  }

  return labels[normalize(severity).toLowerCase()] ?? '业务'
}

function formatDateTime(value: string) {
  const date = new Date(value)
  if (Number.isNaN(date.getTime())) {
    return ''
  }

  return new Intl.DateTimeFormat('zh-CN', {
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    month: '2-digit',
  }).format(date)
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3 border-b pb-4">
        <div>
          <p class="text-xs font-bold uppercase text-primary">业务控制台</p>
          <h1 class="text-xl font-semibold text-foreground">业务工作台</h1>
          <p class="mt-1 max-w-3xl text-sm text-muted-foreground">
            面向计划、车间、质量、库存和设备角色的 PC
            入口；按当前权限汇总待办、消息、预警和可进入页面。
          </p>
        </div>
        <NvBadge variant="neutral">PC 工作台</NvBadge>
      </div>

      <section v-if="summaryPending" class="rounded-lg border bg-background p-3">
        <p class="text-sm text-muted-foreground">正在刷新工作台摘要。</p>
      </section>

      <section class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <div
          v-for="kpi in availableKpis"
          :key="`${kpi.source}-${kpi.key}`"
          class="rounded-lg border bg-background p-4"
        >
          <p class="text-sm text-muted-foreground">{{ kpiSource(kpi) }}</p>
          <p class="mt-2 text-2xl font-semibold text-foreground">{{ kpi.value ?? 0 }}</p>
          <p class="mt-1 text-sm font-medium text-foreground">{{ kpiLabel(kpi) }}</p>
        </div>
        <div
          v-if="!summaryPending && availableKpis.length === 0"
          class="rounded-lg border bg-background p-4 md:col-span-2 xl:col-span-4"
        >
          <p class="text-sm font-medium text-foreground">暂无可显示指标</p>
          <p class="mt-1 text-sm text-muted-foreground">
            当前角色没有可汇总的跨域指标，或来源暂不可用。
          </p>
        </div>
      </section>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1.35fr)_minmax(360px,0.65fr)]">
        <section class="grid gap-4 lg:grid-cols-3">
          <article class="rounded-lg border bg-background">
            <div class="border-b px-4 py-3">
              <div class="flex items-center justify-between gap-3">
                <h2 class="text-sm font-semibold text-foreground">待办</h2>
                <NvBadge variant="neutral">待办 {{ summary?.todos?.total ?? 0 }}</NvBadge>
              </div>
              <p class="mt-1 text-sm text-muted-foreground">审批和通知任务按当前用户过滤。</p>
            </div>
            <div class="divide-y">
              <div
                v-for="item in todoItems"
                :key="`${item.source}-${item.itemId}`"
                class="px-4 py-3"
              >
                <p class="text-sm font-medium text-foreground">{{ todoLabel(item) }}</p>
                <p class="mt-0.5 text-sm text-muted-foreground">{{ todoMeta(item) }}</p>
              </div>
              <div
                v-if="!summaryPending && todoItems.length === 0"
                class="px-4 py-6 text-sm text-muted-foreground"
              >
                暂无待处理事项
              </div>
            </div>
          </article>

          <article class="rounded-lg border bg-background">
            <div class="border-b px-4 py-3">
              <div class="flex items-center justify-between gap-3">
                <h2 class="text-sm font-semibold text-foreground">消息</h2>
                <NvBadge variant="neutral">消息 {{ summary?.messages?.total ?? 0 }}</NvBadge>
              </div>
              <p class="mt-1 text-sm text-muted-foreground">只展示消息状态，不展开消息标题。</p>
            </div>
            <div class="divide-y">
              <div v-for="item in messageItems" :key="item.messageId" class="px-4 py-3">
                <p class="text-sm font-medium text-foreground">{{ messageLabel(item) }}</p>
                <p class="mt-0.5 text-sm text-muted-foreground">{{ messageMeta(item) }}</p>
              </div>
              <div
                v-if="!summaryPending && messageItems.length === 0"
                class="px-4 py-6 text-sm text-muted-foreground"
              >
                暂无未读消息
              </div>
            </div>
          </article>

          <article class="rounded-lg border bg-background">
            <div class="border-b px-4 py-3">
              <div class="flex items-center justify-between gap-3">
                <h2 class="text-sm font-semibold text-foreground">设备预警</h2>
                <NvBadge variant="neutral">设备预警 {{ summary?.alerts?.total ?? 0 }}</NvBadge>
              </div>
              <p class="mt-1 text-sm text-muted-foreground">来自设备运行事实的当前报警。</p>
            </div>
            <div class="divide-y">
              <RouterLink
                v-for="item in alertItems"
                :key="item.alarmEventId"
                class="block px-4 py-3 transition-colors hover:bg-accent"
                to="/equipment/alarms"
              >
                <span class="block text-sm font-medium text-foreground">{{
                  alertLabel(item)
                }}</span>
                <span class="mt-0.5 block text-sm text-muted-foreground">{{
                  alertMeta(item)
                }}</span>
              </RouterLink>
              <div
                v-if="!summaryPending && alertItems.length === 0"
                class="px-4 py-6 text-sm text-muted-foreground"
              >
                暂无当前预警
              </div>
            </div>
          </article>
        </section>

        <section class="rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">来源状态</h2>
            <p class="mt-1 text-sm text-muted-foreground">区分已接入、无权限、未接入和暂不可用。</p>
          </div>
          <div class="grid gap-2 p-3">
            <div
              v-for="source in sourceStatusList"
              :key="source.source || source.label"
              class="flex items-center justify-between gap-3 rounded-md border px-3 py-2"
              :data-source="source.source || source.label"
            >
              <span class="text-sm font-medium text-foreground">{{ source.label }}</span>
              <NvBadge :variant="source.variant">{{ source.statusLabel }}</NvBadge>
            </div>
            <div
              v-if="!summaryPending && sourceStatusList.length === 0"
              class="px-1 py-3 text-sm text-muted-foreground"
            >
              正在等待来源状态。
            </div>
          </div>
        </section>
      </div>

      <section class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">快捷入口</h2>
          <p class="mt-1 text-sm text-muted-foreground">仅展示当前角色可进入的页面。</p>
        </div>
        <div class="grid gap-3 p-3 lg:grid-cols-3">
          <div
            v-for="group in visibleShortcutGroups"
            :key="group.title"
            class="grid gap-2 rounded-md border p-3"
          >
            <h3 class="text-sm font-semibold text-foreground">{{ group.title }}</h3>
            <RouterLink
              v-for="item in group.items"
              :key="item.path"
              class="rounded-md px-3 py-2 text-sm font-medium text-foreground transition-colors hover:bg-accent"
              :to="item.path"
            >
              {{ item.title }}
            </RouterLink>
          </div>
          <div
            v-if="visibleShortcutGroups.length === 0"
            class="rounded-md border p-3 text-sm text-muted-foreground lg:col-span-3"
          >
            当前角色没有可进入页面。
          </div>
        </div>
      </section>

      <section v-if="summaryError" class="rounded-lg border bg-background p-3">
        <p class="text-sm text-muted-foreground">工作台摘要暂不可用，请稍后刷新。</p>
      </section>
    </section>
  </BusinessLayout>
</template>
