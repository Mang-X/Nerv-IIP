<script setup lang="ts">
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useMesOverview } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvCard,
  NvCardContent,
  NvDataTable,
  NvMetricStrip,
  NvPageHeader,
  cn,
} from '@nerv-iip/ui'
import {
  ArrowRightIcon,
  ClipboardCheckIcon,
  FactoryIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
} from '@lucide/vue'
import { computed } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '生产驾驶舱',
    requiredPermissions: ['business.mes.overview.read'],
  },
})

const { blockers, counts, overviewError, overviewPending, pendingWork, refreshOverview } =
  useMesOverview()

const errorMessage = computed(() => formatError(overviewError.value))
// facade 回的 count key 是 kebab-case（work-orders / operation-tasks）。
// 曾按 PascalCase 取值，于是两个总量恒为 0、整个驾驶舱看着像没数据——
// 这里按「去分隔符 + 小写」归一化匹配，两种写法都认。
const workOrderCount = computed(() => countValue('work-orders'))
const operationTaskCount = computed(() => countValue('operation-tasks'))
const blockerCount = computed(() =>
  blockers.value.reduce((total, item) => total + (item.count ?? 0), 0),
)
const pendingWorkCount = computed(() =>
  pendingWork.value.reduce((total, item) => total + (item.count ?? 0), 0),
)

// 指挥卡已经承担「先看什么、去哪里」；这一条只压缩成一行现场总量，不再重复副标题。
const overviewCells = computed<NvMetricStripCell[]>(() => [
  { key: 'work-orders', label: '在制工单', value: workOrderCount.value, unit: '张' },
  { key: 'operation-tasks', label: '工序任务', value: operationTaskCount.value, unit: '个' },
  {
    key: 'blockers',
    label: '阻塞项',
    value: blockerCount.value,
    unit: '项',
    valueTone: blockerCount.value > 0 ? 'danger' : undefined,
  },
  { key: 'pending', label: '待办', value: pendingWorkCount.value, unit: '项' },
])

const commandCards = computed(() => [
  {
    title: '先处理阻塞',
    description:
      blockerCount.value > 0
        ? '物料、质量、设备或产能存在阻塞，先排除再放行。'
        : '当前没有汇总阻塞，可进入工单与派工继续推进。',
    value: blockerCount.value,
    route: blockerCount.value > 0 ? '/mes/capacity' : '/mes/work-orders',
    action: blockerCount.value > 0 ? '查看异常与产能' : '进入工单与派工',
    icon: ShieldAlertIcon,
    tone:
      blockerCount.value > 0
        ? 'border-destructive/30 bg-destructive/5'
        : 'border-success/30 bg-success/5',
  },
  {
    title: '安排今日工单',
    description: '查看待下达、待派工和急单影响，围绕工单推进生产节奏。',
    value: workOrderCount.value,
    route: '/mes/work-orders',
    action: '打开工单队列',
    icon: FactoryIcon,
    tone: 'border-primary/20 bg-primary/5',
  },
  {
    title: '盯紧工序现场',
    description: '从工序任务进入报工、质检和异常记录，减少跨页面手工查找。',
    value: operationTaskCount.value,
    route: '/mes/operation-tasks',
    action: '查看工序执行',
    icon: ClipboardCheckIcon,
    tone: 'border-brand/30 bg-brand/5',
  },
])
const roleLanes = computed(() => [
  {
    role: '调度员',
    focus: '工单释放、插单影响、派工顺序',
    route: '/mes/work-orders',
    count: workOrderCount.value,
  },
  {
    role: '班组长',
    focus: '可开工任务、报工进度、班次遗留',
    route: '/mes/operation-tasks',
    count: operationTaskCount.value,
  },
  {
    role: '物料员',
    focus: '齐套、领料、补料和退料线索',
    route: '/mes/materials',
    count: blockers.value.filter((i) => (i.areaCode ?? '').toLowerCase().includes('material'))
      .length,
  },
  {
    role: '质检/设备',
    focus: '质量阻塞、停机、产能影响',
    route: '/mes/capacity',
    count: blockers.value.filter((i) =>
      ['quality', 'equipment', 'capacity'].some((k) =>
        (i.areaCode ?? '').toLowerCase().includes(k),
      ),
    ).length,
  },
])

// 角色码 → 一线称呼；facade 未来加新角色码时按原样显示，不吞掉。
const PENDING_ROLE_LABELS: Record<string, string> = {
  dispatcher: '调度员',
  planner: '计划员',
  supervisor: '班组长',
  operator: '操作员',
  material: '物料员',
  quality: '质检员',
  maintenance: '设备员',
}
// facade 的 routeHint 是相对路径提示；只接受站内 /mes 路径，其余回落到工序执行。
function resolvePendingRoute(routeHint?: string | null) {
  const hint = (routeHint ?? '').trim()
  return hint.startsWith('/mes') ? hint : '/mes/operation-tasks'
}
const pendingWorkItems = computed(() =>
  pendingWork.value.map((item, index) => ({
    key: `${item.roleCode ?? 'role'}-${item.workType ?? 'work'}-${index}`,
    role: PENDING_ROLE_LABELS[(item.roleCode ?? '').toLowerCase()] ?? (item.roleCode ?? '未指定角色'),
    workType: item.workType ?? '待办',
    count: item.count ?? 0,
    route: resolvePendingRoute(item.routeHint),
  })),
)

type BlockerRow = (typeof blockers)['value'][number]
const blockerColumns: NvDataTableColumn<BlockerRow>[] = [
  { key: 'areaCode', header: '区域', width: 'w-28', accessor: (r) => r.areaCode ?? '未知' },
  { key: 'code', header: '代码', cellClass: 'font-medium', accessor: (r) => r.code ?? '未知' },
  { key: 'message', header: '说明', accessor: (r) => r.message ?? '无说明' },
  { key: 'count', header: '数量', align: 'end', width: 'w-20' },
]

function normalizeCountKey(key?: string | null) {
  return (key ?? '').replace(/[-_\s]/g, '').toLowerCase()
}
function countValue(key: string) {
  const wanted = normalizeCountKey(key)
  return counts.value.find((item) => normalizeCountKey(item.key) === wanted)?.count ?? 0
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="生产驾驶舱" :breadcrumbs="[{ label: '制造执行' }]">
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="overviewPending"
          @click="refreshOverview"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div class="grid gap-4 xl:grid-cols-3">
      <RouterLink
        v-for="card in commandCards"
        :key="card.title"
        :to="{ path: card.route }"
        :class="
          cn(
            'group grid gap-4 rounded-lg border p-4 transition-colors hover:border-primary/40',
            card.tone,
          )
        "
      >
        <div class="flex items-start justify-between gap-3">
          <div class="grid gap-1">
            <p class="text-sm font-semibold text-foreground">{{ card.title }}</p>
            <p class="text-sm leading-6 text-muted-foreground">{{ card.description }}</p>
          </div>
          <component :is="card.icon" class="size-5 shrink-0 text-primary" aria-hidden="true" />
        </div>
        <div class="flex items-end justify-between gap-3">
          <span class="text-3xl font-semibold tabular-nums text-foreground">{{ card.value }}</span>
          <span class="inline-flex items-center gap-1 text-sm font-medium text-primary">
            {{ card.action }}
            <ArrowRightIcon
              class="size-4 transition-transform group-hover:translate-x-0.5"
              aria-hidden="true"
            />
          </span>
        </div>
      </RouterLink>
    </div>

    <NvMetricStrip :cells="overviewCells" />

    <!-- 两栏各自按内容定高：默认 stretch 会把矮的一栏拉到高的一栏那么高，
         留出整片空边框（阻塞表为空时尤其明显）。 -->
    <div class="grid items-start gap-4 xl:grid-cols-[1.15fr_0.85fr]">
      <div class="grid min-w-0 content-start gap-2">
        <div class="flex items-center justify-between">
          <span class="text-sm font-semibold text-foreground">现场阻塞</span>
          <RouterLink
            class="text-sm font-medium text-brand hover:underline"
            :to="{ path: '/mes/capacity' }"
            >异常与产能</RouterLink
          >
        </div>
        <NvDataTable
          :columns="blockerColumns"
          :rows="blockers"
          :row-key="(r) => `${r.areaCode}-${r.code}`"
          :loading="overviewPending"
          :searchable="false"
          :column-settings="false"
          max-body-height="20rem"
          empty-message="暂无阻塞记录。物料、质量、设备或产能出现卡点时会汇总到这里。"
        >
          <template #cell-count="{ row }"
            ><span class="tabular-nums">{{ row.count ?? 0 }}</span></template
          >
        </NvDataTable>
      </div>

      <div class="grid gap-4">
        <NvCard>
          <NvCardContent class="p-0">
            <div class="border-b px-4 py-3">
              <h2 class="text-sm font-semibold text-foreground">角色工作台</h2>
              <p class="mt-1 text-xs text-muted-foreground">把同一批生产事实按一线角色重组入口。</p>
            </div>
            <div class="divide-y">
              <RouterLink
                v-for="lane in roleLanes"
                :key="lane.role"
                class="flex items-center justify-between gap-3 p-4 transition-colors hover:bg-muted/50"
                :to="{ path: lane.route }"
              >
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-foreground">{{ lane.role }}</p>
                  <p class="mt-1 truncate text-sm text-muted-foreground">{{ lane.focus }}</p>
                </div>
                <div class="flex items-center gap-3">
                  <span class="text-lg font-semibold tabular-nums">{{ lane.count }}</span>
                  <ArrowRightIcon class="size-4 text-muted-foreground" aria-hidden="true" />
                </div>
              </RouterLink>
            </div>
          </NvCardContent>
        </NvCard>

        <NvCard>
          <NvCardContent class="p-0">
            <div class="flex items-center gap-2 border-b px-4 py-3">
              <PackageCheckIcon class="size-4 text-primary" aria-hidden="true" />
              <h2 class="text-sm font-semibold text-foreground">待办工作</h2>
            </div>
            <div v-if="pendingWorkItems.length" class="divide-y">
              <RouterLink
                v-for="item in pendingWorkItems"
                :key="item.key"
                class="flex items-center justify-between gap-3 p-4 transition-colors hover:bg-muted/50"
                :to="{ path: item.route }"
              >
                <div class="min-w-0">
                  <p class="text-sm font-semibold text-foreground">{{ item.workType }}</p>
                  <p class="mt-1 truncate text-sm text-muted-foreground">{{ item.role }}</p>
                </div>
                <div class="flex items-center gap-3">
                  <span class="text-lg font-semibold tabular-nums">{{ item.count }}</span>
                  <ArrowRightIcon class="size-4 text-muted-foreground" aria-hidden="true" />
                </div>
              </RouterLink>
            </div>
            <p v-else class="px-4 py-6 text-sm text-muted-foreground">
              暂无按角色汇总的待办。各角色可从上方工作台直接进入自己的队列。
            </p>
          </NvCardContent>
        </NvCard>
      </div>
    </div>
  </BusinessLayout>
</template>
