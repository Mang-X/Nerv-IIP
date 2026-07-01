<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_DOMAIN_PERMISSIONS } from '@/permissions'
import { BadgePro } from '@nerv-iip/ui'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务工作台',
    requiredPermissions: [...BUSINESS_DOMAIN_PERMISSIONS.workbench],
  },
})

const attentionItems = [
  { path: '/mes/capacity', label: '设备停机影响', status: '需关注', detail: '查看停机、恢复和维护事件对本班产能的影响。' },
  { path: '/inventory/counts', label: '盘点差异确认', status: '待处理', detail: '进入盘点任务，核对差异并完成调整确认。' },
  { path: '/quality/ncrs', label: '不合格品处置', status: '待跟进', detail: '跟进待处置、待关闭的质量异常和返工去向。' },
  { path: '/mes/wip', label: '在制阻塞跟踪', status: '待排查', detail: '查看在制状态、阻塞原因和对应工单。' },
] as const

const actionGroups = [
  {
    title: '基础与工程',
    items: [
      { path: '/master-data/skus', label: '维护物料与产品', summary: '补齐成品、半成品、原材料和包材。' },
      { path: '/master-data/facilities', label: '维护工厂与产线', summary: '建档工厂、产线和工作中心。' },
      { path: '/master-data/devices', label: '维护设备台账', summary: '登记产线与工作中心下的设备资产。' },
      { path: '/engineering', label: '查看发布版本', summary: '核对生产版本、路线和物料清单。' },
    ],
  },
  {
    title: '计划与生产',
    items: [
      { path: '/planning', label: '需求与物料计划', summary: '查看需求、物料计划和建议清单。' },
      { path: '/mes/plans', label: '计划转工单', summary: '从生产计划进入工单下达前检查。' },
      { path: '/mes/work-orders', label: '工单与派工', summary: '处理急单、派工和工单详情。' },
    ],
  },
  {
    title: '质量与库存',
    items: [
      { path: '/quality/inspections', label: '检验任务与记录', summary: '从方案、工单或收货信息提交检验记录。' },
      { path: '/inventory/availability', label: '查询可用库存', summary: '按物料、工厂和批次确认可用量。' },
      { path: '/inventory/movements', label: '查看库存移动', summary: '核对入库、调拨和调整记录。' },
    ],
  },
] as const

const chainItems = [
  { path: '/erp', title: '采购到料跟进', detail: '查看采购订单、供应商交期、未到数量和部分收货状态。' },
  { path: '/mes/materials', title: '齐套到派工', detail: '从齐套状态进入工单派工，减少车间等待。' },
  { path: '/mes/operation-tasks', title: '任务到报工', detail: '从工序任务进入执行队列，再提交报工和完工。' },
  { path: '/mes/receipts', title: '完工到入库', detail: '查看完工入库请求，衔接库存入账。' },
  { path: '/mes/downtime', title: '停机到产能', detail: '查看设备停机记录，再评估产能影响。' },
  { path: '/mes/traceability', title: '批次到追溯', detail: '按工单、批次和物料追踪生产过程。' },
] as const

const supportItems = [
  { path: '/mes/schedules', label: '规则排程', detail: '查看当前规则排程结果；高级排程能力接入前不在首页执行长期排程承诺。' },
  { path: '/mes/foundation', label: '生产准备检查', detail: '用于放行前排查基础数据、工程资料、库存、质量和设备准备情况。' },
] as const
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <div class="flex flex-wrap items-end justify-between gap-3 border-b pb-4">
        <div>
          <p class="text-xs font-bold uppercase text-primary">业务控制台</p>
          <h1 class="text-xl font-semibold text-foreground">业务工作台</h1>
          <p class="mt-1 max-w-3xl text-sm text-muted-foreground">
            面向计划、车间、质量和库存的 PC 入口；从待关注事项进入队列，从业务动作进入已落地页面。
          </p>
        </div>
        <BadgePro variant="neutral">PC 工作台</BadgePro>
      </div>

      <div class="grid gap-4 lg:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
        <section class="rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">待关注事项</h2>
            <p class="mt-1 text-sm text-muted-foreground">先处理会影响放行、派工、报工和入库的事项。</p>
          </div>
          <div class="divide-y">
            <RouterLink
              v-for="item in attentionItems"
              :key="item.path"
              class="grid grid-cols-[1fr_auto] gap-3 px-4 py-3 transition-colors hover:bg-accent"
              :to="item.path"
            >
              <span>
                <span class="block text-sm font-medium text-foreground">{{ item.label }}</span>
                <span class="mt-0.5 block text-sm text-muted-foreground">{{ item.detail }}</span>
              </span>
              <BadgePro variant="neutral">{{ item.status }}</BadgePro>
            </RouterLink>
          </div>
        </section>

        <section class="rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">关键链路入口</h2>
            <p class="mt-1 text-sm text-muted-foreground">按业务链路跳转，不要求用户回到菜单逐层查找。</p>
          </div>
          <div class="grid gap-2 p-3 sm:grid-cols-2">
            <RouterLink
              v-for="item in chainItems"
              :key="item.path"
              class="rounded-md border px-3 py-2 transition-colors hover:border-primary/50 hover:bg-accent"
              :to="item.path"
            >
              <span class="block text-sm font-medium text-foreground">{{ item.title }}</span>
              <span class="mt-0.5 block text-sm text-muted-foreground">{{ item.detail }}</span>
            </RouterLink>
          </div>
        </section>
      </div>

      <section class="rounded-lg border bg-background">
        <div class="border-b px-4 py-3">
          <h2 class="text-sm font-semibold text-foreground">业务动作组</h2>
          <p class="mt-1 text-sm text-muted-foreground">只放已存在页面入口；真正提交动作仍在列表、详情或抽屉中完成。</p>
        </div>
        <div class="grid gap-3 p-3 lg:grid-cols-3">
          <div v-for="group in actionGroups" :key="group.title" class="grid gap-2 rounded-md border p-3">
            <h3 class="text-sm font-semibold text-foreground">{{ group.title }}</h3>
            <RouterLink
              v-for="item in group.items"
              :key="item.path"
              class="rounded-md px-3 py-2 transition-colors hover:bg-accent"
              :to="item.path"
            >
              <span class="block text-sm font-medium text-foreground">{{ item.label }}</span>
              <span class="mt-0.5 block text-sm text-muted-foreground">{{ item.summary }}</span>
            </RouterLink>
          </div>
        </div>
      </section>

      <section class="grid gap-2 rounded-lg border bg-background p-3 md:grid-cols-2">
        <RouterLink
          v-for="item in supportItems"
          :key="item.path"
          class="rounded-md border px-3 py-2 transition-colors hover:border-primary/50 hover:bg-accent"
          :to="item.path"
        >
          <span class="block text-sm font-medium text-foreground">{{ item.label }}</span>
          <span class="mt-0.5 block text-sm text-muted-foreground">{{ item.detail }}</span>
        </RouterLink>
      </section>
    </section>
  </BusinessLayout>
</template>
