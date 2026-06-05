<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMesOverview } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Card,
  CardContent,
  DataTable,
  PageHeader,
  SectionCard,
  SectionCards,
  cn,
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
import { RouterLink } from 'vue-router'

definePage({ meta: { requiresAuth: true, title: '生产驾驶舱' } })

const { blockers, counts, overviewError, overviewPending, pendingWork, refreshOverview } = useMesOverview()

const errorMessage = computed(() => formatError(overviewError.value))
const workOrderCount = computed(() => countValue('WorkOrders'))
const operationTaskCount = computed(() => countValue('OperationTasks'))
const blockerCount = computed(() => blockers.value.reduce((total, item) => total + (item.count ?? 0), 0))
const pendingWorkCount = computed(() => pendingWork.value.reduce((total, item) => total + (item.count ?? 0), 0))

const commandCards = computed(() => [
  {
    title: '先处理阻塞',
    description: blockerCount.value > 0 ? '物料、质量、设备或产能存在阻塞，先排除再放行。' : '当前没有汇总阻塞，可进入工单与派工继续推进。',
    value: blockerCount.value,
    route: blockerCount.value > 0 ? '/mes/capacity' : '/mes/work-orders',
    action: blockerCount.value > 0 ? '查看异常与产能' : '进入工单与派工',
    icon: ShieldAlertIcon,
    tone: blockerCount.value > 0 ? 'border-destructive/30 bg-destructive/5' : 'border-success/30 bg-success/5',
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
  { role: '调度员', focus: '工单释放、插单影响、派工顺序', route: '/mes/work-orders', count: workOrderCount.value },
  { role: '班组长', focus: '可开工任务、报工进度、班次遗留', route: '/mes/operation-tasks', count: operationTaskCount.value },
  { role: '物料员', focus: '齐套、领料、补料和退料线索', route: '/mes/materials', count: blockers.value.filter((i) => (i.areaCode ?? '').toLowerCase().includes('material')).length },
  { role: '质检/设备', focus: '质量阻塞、停机、产能影响', route: '/mes/capacity', count: blockers.value.filter((i) => ['quality', 'equipment', 'capacity'].some((k) => (i.areaCode ?? '').toLowerCase().includes(k))).length },
])

type BlockerRow = (typeof blockers)['value'][number]
const blockerColumns: DataTableColumn<BlockerRow>[] = [
  { key: 'areaCode', header: '区域', width: 'w-28', accessor: (r) => r.areaCode ?? '未知' },
  { key: 'code', header: '代码', cellClass: 'font-medium', accessor: (r) => r.code ?? '未知' },
  { key: 'message', header: '说明', accessor: (r) => r.message ?? '无说明' },
  { key: 'count', header: '数量', align: 'end', width: 'w-20' },
]

function countValue(key: string) {
  return counts.value.find((item) => item.key === key)?.count ?? 0
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="生产驾驶舱" :breadcrumbs="[{ label: '制造执行' }]">
      <template #actions>
        <Button size="sm" type="button" variant="outline" :disabled="overviewPending" @click="refreshOverview">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <div class="grid gap-4 xl:grid-cols-3">
      <RouterLink
        v-for="card in commandCards"
        :key="card.title"
        :to="{ path: card.route }"
        :class="cn('group grid gap-4 rounded-lg border p-4 transition-colors hover:border-primary/40', card.tone)"
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

    <SectionCards :columns="4">
      <SectionCard description="工单" :value="workOrderCount" hint="当前可见工单数" />
      <SectionCard description="工序任务" :value="operationTaskCount" hint="当前可见任务数" />
      <SectionCard description="阻塞项" :value="blockerCount" hint="需处理的问题数量" />
      <SectionCard description="待办" :value="pendingWorkCount" hint="按角色汇总" />
    </SectionCards>

    <div class="grid gap-4 xl:grid-cols-[1.15fr_0.85fr]">
      <div class="grid gap-2">
        <div class="flex items-center justify-between">
          <span class="text-sm font-semibold text-foreground">现场阻塞</span>
          <RouterLink class="text-sm font-medium text-brand hover:underline" :to="{ path: '/mes/capacity' }">异常与产能</RouterLink>
        </div>
        <DataTable
          :columns="blockerColumns"
          :rows="blockers"
          :row-key="(r) => `${r.areaCode}-${r.code}`"
          :loading="overviewPending"
          empty-message="当前没有生产阻塞。可进入工单与派工继续安排今日任务。"
        >
          <template #cell-count="{ row }"><span class="tabular-nums">{{ row.count ?? 0 }}</span></template>
        </DataTable>
      </div>

      <div class="grid gap-4">
        <Card>
          <CardContent class="p-0">
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
          </CardContent>
        </Card>

        <Card>
          <CardContent class="grid gap-3">
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
                <RouterLink :to="{ path: '/mes/work-orders' }"><FactoryIcon aria-hidden="true" />工单与派工</RouterLink>
              </Button>
              <Button size="sm" type="button" variant="outline" as-child>
                <RouterLink :to="{ path: '/mes/operation-tasks' }"><ClipboardCheckIcon aria-hidden="true" />工序执行</RouterLink>
              </Button>
              <Button size="sm" type="button" variant="outline" as-child>
                <RouterLink :to="{ path: '/mes/capacity' }"><WrenchIcon aria-hidden="true" />异常与产能</RouterLink>
              </Button>
            </div>
          </CardContent>
        </Card>
      </div>
    </div>
  </BusinessLayout>
</template>
