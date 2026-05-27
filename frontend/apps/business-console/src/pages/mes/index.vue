<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessEmptyState from '@/components/business/BusinessEmptyState.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesOverview } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Badge,
  Button,
  Spinner,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import {
  ArrowRightIcon,
  ClipboardCheckIcon,
  FactoryIcon,
  PackageCheckIcon,
  RefreshCwIcon,
  ShieldAlertIcon,
  WrenchIcon,
} from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '生产驾驶舱',
  },
})

const {
  blockers,
  counts,
  overviewError,
  overviewPending,
  pendingWork,
  refreshOverview,
} = useMesOverview()

const errorMessage = computed(() => formatError(overviewError.value))
const workOrderCount = computed(() => countValue('WorkOrders'))
const operationTaskCount = computed(() => countValue('OperationTasks'))
const blockerCount = computed(() => blockers.value.reduce((total, item) => total + (item.count ?? 0), 0))
const pendingWorkCount = computed(() => pendingWork.value.reduce((total, item) => total + (item.count ?? 0), 0))

const commandCards = computed(() => [
  {
    title: '先处理阻塞',
    description: blockerCount.value > 0 ? '物料、质量、设备或准备项存在阻塞，先排除再放行。' : '当前没有汇总阻塞，可以进入工单与派工继续推进。',
    value: blockerCount.value,
    route: blockerCount.value > 0 ? '/mes/foundation' : '/mes/work-orders',
    action: blockerCount.value > 0 ? '查看准备检查' : '进入工单与派工',
    icon: ShieldAlertIcon,
    tone: blockerCount.value > 0 ? 'border-destructive/30 bg-destructive/5' : 'border-emerald-500/20 bg-emerald-500/5',
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
    tone: 'border-blue-500/20 bg-blue-500/5',
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
    count: blockers.value.filter((item) => (item.areaCode ?? '').toLowerCase().includes('material')).length,
  },
  {
    role: '质检/设备',
    focus: '质量阻塞、停机、产能影响',
    route: '/mes/capacity',
    count: blockers.value.filter((item) => ['quality', 'equipment', 'capacity'].some((key) => (item.areaCode ?? '').toLowerCase().includes(key))).length,
  },
])

function countValue(key: string) {
  return counts.value.find((item) => item.key === key)?.count ?? 0
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败。' : ''
}
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader
        domain="MES"
        title="生产驾驶舱"
        kicker="班组长 / 调度员首屏"
        summary="先看阻塞，再进工单和工序。这里把计划、物料、质量、设备和班次待办压缩成可行动的现场指挥视图。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="overviewPending" @click="refreshOverview">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <BusinessFormStatus :error="errorMessage" />

      <div class="grid gap-3 xl:grid-cols-3">
        <RouterLink
          v-for="card in commandCards"
          :key="card.title"
          class="group grid gap-4 rounded-lg border p-4 transition-colors hover:border-primary/40"
          :class="card.tone"
          :to="{ path: card.route }"
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
              <ArrowRightIcon class="size-4 transition-transform group-hover:translate-x-0.5" aria-hidden="true" />
            </span>
          </div>
        </RouterLink>
      </div>

      <div class="grid gap-3 md:grid-cols-4">
        <BusinessMetricCell label="工单" :value="workOrderCount" detail="当前可见工单数" />
        <BusinessMetricCell label="工序任务" :value="operationTaskCount" detail="当前可见任务数" />
        <BusinessMetricCell label="阻塞项" :value="blockerCount" detail="需处理的问题数量" />
        <BusinessMetricCell label="待办" :value="pendingWorkCount" detail="按角色汇总" />
      </div>

      <div class="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
        <div class="overflow-hidden rounded-lg border bg-background">
          <div class="flex items-center justify-between border-b px-4 py-3">
            <div>
              <h2 class="text-sm font-semibold text-foreground">现场阻塞</h2>
              <p class="mt-1 text-xs text-muted-foreground">按来源聚合，先处理会阻断开工或完工的事项。</p>
            </div>
            <RouterLink class="text-sm font-medium text-primary hover:underline" :to="{ path: '/mes/foundation' }">
              准备检查
            </RouterLink>
          </div>
          <div v-if="!blockers.length && !overviewPending">
            <BusinessEmptyState
              title="当前没有生产阻塞"
              description="可以进入工单与派工继续安排今日任务；如果现场仍无法开工，请检查工厂、产线、物料、质量和设备基础准备。"
              action="建议从工单队列选择下一张可执行工单。"
            />
          </div>
          <div v-else class="overflow-x-auto">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>区域</TableHead>
                  <TableHead>代码</TableHead>
                  <TableHead>说明</TableHead>
                  <TableHead class="text-right">数量</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="item in blockers" :key="`${item.areaCode}-${item.code}`">
                  <TableCell>
                    <Badge variant="secondary">{{ item.areaCode ?? '未知' }}</Badge>
                  </TableCell>
                  <TableCell class="font-medium">{{ item.code ?? '未知' }}</TableCell>
                  <TableCell>{{ item.message ?? '无说明' }}</TableCell>
                  <TableCell class="text-right tabular-nums">{{ item.count ?? 0 }}</TableCell>
                </TableRow>
                <TableEmpty v-if="overviewPending" :colspan="4">正在加载总览…</TableEmpty>
              </TableBody>
            </Table>
          </div>
        </div>

        <div class="grid gap-4">
          <div class="overflow-hidden rounded-lg border bg-background">
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
          </div>

          <div class="grid gap-3 rounded-lg border bg-background p-4">
            <div class="flex items-center gap-2">
              <PackageCheckIcon class="size-4 text-primary" aria-hidden="true" />
              <h2 class="text-sm font-semibold text-foreground">下一步建议</h2>
            </div>
            <div class="grid gap-2 text-sm text-muted-foreground">
              <p>1. 有阻塞时先进入生产准备检查，明确是物料、质量还是设备问题。</p>
              <p>2. 没有阻塞时进入工单与派工，选择工单后再做报工、齐套或异常处理。</p>
              <p>3. 班中执行以工序执行为主，不要求一线人员跨模块手工拼接编号。</p>
            </div>
            <div class="flex flex-wrap gap-2 pt-1">
              <Button size="sm" type="button" as-child>
                <RouterLink :to="{ path: '/mes/work-orders' }">
                  <FactoryIcon data-icon="inline-start" />
                  工单与派工
                </RouterLink>
              </Button>
              <Button size="sm" type="button" variant="outline" as-child>
                <RouterLink :to="{ path: '/mes/operation-tasks' }">
                  <ClipboardCheckIcon data-icon="inline-start" />
                  工序执行
                </RouterLink>
              </Button>
              <Button size="sm" type="button" variant="outline" as-child>
                <RouterLink :to="{ path: '/mes/capacity' }">
                  <WrenchIcon data-icon="inline-start" />
                  异常与产能
                </RouterLink>
              </Button>
            </div>
          </div>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
