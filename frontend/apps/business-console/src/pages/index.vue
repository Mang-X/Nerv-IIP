<script setup lang="ts">
import type { Component } from 'vue'
import { computed } from 'vue'
import {
  ActivityIcon,
  BellRingIcon,
  FactoryIcon,
  ListChecksIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
} from '@lucide/vue'
import {
  NvButton,
  NvCard,
  NvDonutChart,
  NvMetricCard,
  NvPageHeader,
  NvSectionCards,
} from '@nerv-iip/ui'
import type { DonutSlice, NvMetricTone } from '@nerv-iip/ui'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import WorkbenchDomainTiles from '@/components/workbench/WorkbenchDomainTiles.vue'
import type { WorkbenchDomainTile } from '@/components/workbench/WorkbenchDomainTiles.vue'
import WorkbenchFocusCard from '@/components/workbench/WorkbenchFocusCard.vue'
import type { WorkbenchFocusItem } from '@/components/workbench/WorkbenchFocusCard.vue'
import { BUSINESS_DOMAINS, DOMAIN_SIDE_NAV, WORKBENCH_DOMAIN_ID, permittedBy } from '@/navigation'
import { BUSINESS_DOMAIN_PERMISSIONS } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { useBusinessWorkbenchSummary } from '@/composables/useBusinessWorkbench'
import type {
  BusinessConsoleWorkbenchAlertItem,
  BusinessConsoleWorkbenchMessageItem,
  BusinessConsoleWorkbenchTodoItem,
} from '@nerv-iip/api-client'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务工作台',
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench],
  },
})

/**
 * 业务工作台 = 跨域经营驾驶舱首屏，不是入口目录页。三段式：
 *
 * ① 英雄区（左 2×2 指标 + 右「今日待处理构成」环图）——只放 facade 真实返回的跨域读数，
 *    某个来源 status 不是 available 就整张不出现，宁缺毋假，不用 0 冒充"已接入且为零"。
 * ② 行动区——待办 / 消息 / 设备预警三张行动卡，带最近 3 条真实条目与出口。
 * ③ 业务域磁贴——收纳到域一级，页面级导航交给进入该域后的左侧导航。
 *
 * 「来源状态」（已接入 / 未接入 / 无权限 / 暂不可用）是实施与运维视角，业务角色的第一屏
 * 不承载；需要排障视图时应另建运维页面，而不是塞回工作台。
 */

const auth = useAuthStore()
const {
  alertItems,
  availableKpis,
  messageItems,
  refreshWorkbenchSummary,
  summary,
  summaryError,
  summaryPending,
  todoItems,
} = useBusinessWorkbenchSummary()

interface HeroMetric {
  key: string
  label: string
  value: number
  unit: string
  icon: Component
  tone: NvMetricTone
}

const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])

const todosAvailable = computed(() => isAvailable(summary.value?.todos?.status))
const messagesAvailable = computed(() => isAvailable(summary.value?.messages?.status))
const alertsAvailable = computed(() => isAvailable(summary.value?.alerts?.status))

const todoTotal = computed(() => (todosAvailable.value ? (summary.value?.todos?.total ?? 0) : 0))
const messageTotal = computed(() =>
  messagesAvailable.value ? (summary.value?.messages?.total ?? 0) : 0,
)
const messageUnread = computed(() =>
  messagesAvailable.value ? (summary.value?.messages?.unread ?? 0) : 0,
)
const alertTotal = computed(() => (alertsAvailable.value ? (summary.value?.alerts?.total ?? 0) : 0))
const alertCritical = computed(() =>
  alertsAvailable.value ? (summary.value?.alerts?.critical ?? 0) : 0,
)

/** 首屏总量：三路待处理之和，供页头一眼给出"今天有多少事"。 */
const pendingTotal = computed(() => todoTotal.value + messageUnread.value + alertTotal.value)

/**
 * 英雄区指标：facade KPI（当前口径为已下达工单 / 未关闭质量异常）+ 待办 + 设备预警。
 * 未读消息不占指标位——它的价值在行动卡的条目里，指标位留给能驱动决策的量。
 */
const heroMetrics = computed<HeroMetric[]>(() => {
  const metrics: HeroMetric[] = availableKpis.value.map((kpi) => {
    const value = kpi.value ?? 0
    const preset = KPI_PRESENTATION[normalize(kpi.key)]
    return {
      key: `kpi-${normalize(kpi.source)}-${normalize(kpi.key)}`,
      label: preset?.label ?? (normalize(kpi.label) || '业务指标'),
      value,
      unit: preset?.unit ?? '',
      icon: preset?.icon ?? ListChecksIcon,
      tone: preset ? preset.tone(value) : 'neutral',
    }
  })

  if (todosAvailable.value) {
    metrics.push({
      key: 'todos',
      label: '待办事项',
      value: todoTotal.value,
      unit: '项',
      icon: ListChecksIcon,
      tone: todoTotal.value > 0 ? 'warning' : 'success',
    })
  }

  if (alertsAvailable.value) {
    metrics.push({
      key: 'alerts',
      label: '未解除设备预警',
      value: alertTotal.value,
      unit: '条',
      icon: ActivityIcon,
      tone: alertCritical.value > 0 ? 'danger' : alertTotal.value > 0 ? 'warning' : 'success',
    })
  }

  return metrics
})

/**
 * 今日待处理构成：三路各自的**权威总量**（不是抽样条目），所以份额是真的。
 * 三路都为零时整块换成"已清空"读数，不画一个空环。
 */
const workloadSlices = computed<DonutSlice[]>(() =>
  [
    { label: '待办', value: todoTotal.value, color: 'var(--nv-warning)' },
    { label: '未读消息', value: messageUnread.value, color: 'var(--nv-brand)' },
    { label: '设备预警', value: alertTotal.value, color: 'var(--destructive)' },
  ].filter((slice) => slice.value > 0),
)

const todoFocusItems = computed<WorkbenchFocusItem[]>(() =>
  todoItems.value.map((item) => ({
    key: `${normalize(item.source)}-${normalize(item.itemId)}`,
    primary: normalize(item.referenceId) || todoLabel(item),
    secondary: todoMeta(item),
  })),
)

const messageFocusItems = computed<WorkbenchFocusItem[]>(() =>
  messageItems.value.map((item) => ({
    key: normalize(item.messageId),
    primary: normalize(item.resourceId) || messageLabel(item),
    secondary: messageMeta(item),
  })),
)

const alertFocusItems = computed<WorkbenchFocusItem[]>(() =>
  alertItems.value.map((item) => ({
    key: normalize(item.alarmEventId),
    primary: alertLabel(item),
    secondary: alertMeta(item),
  })),
)

/**
 * 业务域磁贴：落点取该域中当前角色**第一个有权限**的页面；一个可进入页面都没有的域
 * 直接不出现（前端可见性与网关授权保持一致，不留点进去 403 的入口）。
 */
const domainTiles = computed<WorkbenchDomainTile[]>(() =>
  BUSINESS_DOMAINS.filter((domain) => domain.id !== WORKBENCH_DOMAIN_ID)
    .map((domain) => {
      const items = (DOMAIN_SIDE_NAV[domain.id] ?? []).flatMap((group) =>
        permittedBy(group.items, permissionCodes.value),
      )
      const first = items[0]
      const to = first ? (typeof first.to === 'string' ? first.to : (first.to.path ?? '')) : ''

      return {
        id: domain.id,
        title: domain.title,
        icon: domain.icon,
        moduleCount: items.length,
        to,
      }
    })
    .filter((tile) => tile.moduleCount > 0 && tile.to.length > 0),
)

/** facade KPI key → 业务展示口径。未登记的 key 按 facade 标签原样展示，不硬编码兜底。 */
const KPI_PRESENTATION: Record<
  string,
  { label: string; unit: string; icon: Component; tone: (value: number) => NvMetricTone }
> = {
  openNcrs: {
    label: '未关闭质量异常',
    unit: '项',
    icon: ShieldAlertIcon,
    tone: (value) => (value > 0 ? 'danger' : 'success'),
  },
  releasedWorkOrders: {
    label: '已下达工单',
    unit: '张',
    icon: FactoryIcon,
    tone: () => 'brand',
  },
}

function isAvailable(status: string | null | undefined) {
  return normalize(status).toLowerCase() === 'available'
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
  return parts.filter(Boolean).join(' · ')
}

function messageLabel(item: BusinessConsoleWorkbenchMessageItem) {
  return `${severityLabel(item.severity)}消息`
}

function messageMeta(item: BusinessConsoleWorkbenchMessageItem) {
  const parts = [severityLabel(item.severity), statusLabel(item.status)]
  if (item.createdAtUtc) {
    parts.push(formatDateTime(item.createdAtUtc))
  }
  return parts.filter(Boolean).join(' · ')
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
  return parts.filter(Boolean).join(' · ')
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
    <NvPageHeader
      title="业务工作台"
      :breadcrumbs="[{ label: '数字化工作台' }]"
      :count="`${pendingTotal} 项待处理`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="summaryPending"
          @click="refreshWorkbenchSummary"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="summaryPending" class="text-sm text-muted-foreground" role="status">
      正在刷新工作台摘要。
    </p>
    <p v-else-if="summaryError" class="text-sm text-destructive" role="alert">
      工作台摘要暂不可用，请稍后刷新。
    </p>

    <!-- ① 英雄区：左侧跨域指标 2×2，右侧今日待处理构成 -->
    <section class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_minmax(340px,24rem)]">
      <!-- auto-rows-fr：指标块与右侧环图卡等高，英雄区不出现半截留白 -->
      <NvSectionCards v-if="heroMetrics.length > 0" class="h-full auto-rows-fr" :columns="2">
        <NvMetricCard
          v-for="metric in heroMetrics"
          :key="metric.key"
          class="flex flex-col justify-center bg-gradient-to-t from-primary/5 to-card"
          variant="icon"
          :label="metric.label"
          :value="metric.value"
          :unit="metric.unit"
          :icon="metric.icon"
          :tone="metric.tone"
        />
      </NvSectionCards>
      <NvCard
        v-else-if="!summaryPending"
        class="grid place-items-center bg-gradient-to-t from-primary/5 to-card p-6 text-center"
      >
        <div>
          <p class="text-sm font-medium text-foreground">暂无可显示指标</p>
          <p class="mt-1 text-sm text-muted-foreground">
            当前角色没有可汇总的跨域指标，或来源暂不可用。
          </p>
        </div>
      </NvCard>

      <NvCard class="flex flex-col overflow-hidden bg-gradient-to-t from-primary/5 to-card p-0">
        <div class="border-b px-5 py-3">
          <h2 class="text-sm font-semibold text-foreground">今日待处理构成</h2>
        </div>
        <div class="flex flex-1 items-center px-5 py-4">
          <NvDonutChart
            v-if="workloadSlices.length > 0"
            class="w-full"
            :data="workloadSlices"
            :height="144"
            :central-label="String(pendingTotal)"
            central-sub-label="项待处理"
          />
          <div v-else-if="!summaryPending" class="w-full text-center">
            <p class="text-sm font-medium text-foreground">今天没有待处理事项</p>
            <p class="mt-1 text-sm leading-6 text-muted-foreground">
              待办、消息与设备预警都已清空，新的事项到达后会自动汇总到这里。
            </p>
          </div>
        </div>
      </NvCard>
    </section>

    <!-- ② 行动区：三条处理路径，各带最近条目与出口 -->
    <section class="grid gap-4 lg:grid-cols-3">
      <WorkbenchFocusCard
        title="待办"
        description="先清掉到期的审批与任务，别让单据停在你这一环。"
        :icon="ListChecksIcon"
        tone="warning"
        :count="todoTotal"
        unit="项"
        :items="todoFocusItems"
        :pending="summaryPending"
        empty-title="待办已清空"
        empty-hint="新的审批与任务到达后会自动出现在这里。"
        to="/approval"
        action-label="去审批中心"
      />

      <WorkbenchFocusCard
        title="消息"
        description="与你相关的通知集中在这里，不用逐个业务页面翻。"
        :icon="BellRingIcon"
        tone="brand"
        :count="messageUnread"
        unit="条"
        :badge="messageTotal > 0 ? `共 ${messageTotal} 条` : undefined"
        :items="messageFocusItems"
        :pending="summaryPending"
        empty-title="暂无未读消息"
        empty-hint="有新的业务通知时会第一时间出现在这里。"
      />

      <WorkbenchFocusCard
        title="设备预警"
        description="先看紧急报警，确认现场是否已经影响生产节拍。"
        :icon="ShieldAlertIcon"
        :tone="alertCritical > 0 ? 'danger' : 'neutral'"
        :count="alertTotal"
        unit="条"
        :badge="alertCritical > 0 ? `紧急 ${alertCritical}` : undefined"
        :items="alertFocusItems"
        :pending="summaryPending"
        empty-title="设备当前运行正常"
        empty-hint="一旦有设备报出异常就会立刻出现在这里。"
        to="/equipment/alarms"
        action-label="查看设备报警"
      />
    </section>

    <!-- ③ 业务域磁贴：收纳到域一级，页面级导航交给域内左侧导航 -->
    <WorkbenchDomainTiles :tiles="domainTiles" />
  </BusinessLayout>
</template>
