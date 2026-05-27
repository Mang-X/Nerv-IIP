<script setup lang="ts">
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Badge } from '@nerv-iip/ui'

definePage({
  meta: {
    requiresAuth: true,
    title: '业务工作台',
  },
})

const workbenchItems = [
  { path: '/master-data/skus', domain: '主数据', label: '物料与产品', summary: '减振器成品、半成品、原材料和包材建档。' },
  { path: '/master-data/resources', domain: '主数据', label: '工厂资源', summary: '维护工厂、产线、工作中心、设备和班次。' },
  { path: '/erp', domain: 'ERP', label: '业务协同', summary: '查看销售、采购、财务与生产的协同进度。' },
  { path: '/mes/plans', domain: 'MES', label: '生产计划', summary: '处理正常订单、备货、安全库存和预测需求转工单。' },
  { path: '/mes/work-orders', domain: 'MES', label: '工单与派工', summary: '处理计划工单、急单插单、派工和工单详情。' },
  { path: '/mes/operation-tasks', domain: 'MES', label: '工序执行', summary: '查看待开工、执行中和阻塞任务。' },
  { path: '/quality/ncrs', domain: '质量', label: '不合格品处理', summary: '跟进待处置、待关闭的质量异常。' },
  { path: '/inventory/availability', domain: '库存', label: '库存可用量', summary: '按物料、工厂、批次快速确认库存。' },
] as const

const exceptionItems = [
  { path: '/mes/foundation', label: '生产准备检查', status: '辅助', detail: '开工、释放、派工前的齐套和主数据检查。' },
  { path: '/mes/capacity', label: '异常与产能', status: '关注', detail: '设备停机、恢复和维护对产能的影响。' },
  { path: '/inventory/counts', label: '库存盘点', status: '待办', detail: '创建盘点任务并从任务信息确认差异。' },
  { path: '/quality/inspections', label: '检验任务与记录', status: '待办', detail: '从方案、工单或收货信息提交检验记录。' },
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
            面向生产、库存和质量的一线待办入口；基础能力收在业务动作背后，不再按系统边界罗列。
          </p>
        </div>
        <Badge variant="secondary">PC 工作台</Badge>
      </div>

      <div class="grid gap-3 md:grid-cols-2 xl:grid-cols-4">
        <RouterLink
          v-for="item in workbenchItems"
          :key="item.path"
          class="grid gap-2 rounded-lg border bg-background p-4 text-sm transition-colors hover:border-primary/50 hover:bg-accent focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-ring"
          :to="item.path"
        >
          <span class="text-xs font-bold uppercase text-muted-foreground">{{ item.domain }}</span>
          <span class="text-base font-semibold text-foreground">{{ item.label }}</span>
          <span class="text-sm text-muted-foreground">{{ item.summary }}</span>
        </RouterLink>
      </div>

      <div class="grid gap-4 lg:grid-cols-[minmax(0,1.2fr)_minmax(320px,0.8fr)]">
        <section class="rounded-lg border bg-background">
          <div class="border-b px-4 py-3">
            <h2 class="text-sm font-semibold text-foreground">今日待处理</h2>
            <p class="mt-1 text-sm text-muted-foreground">后续接入角色待办前，先按业务入口安排操作。</p>
          </div>
          <div class="divide-y">
            <RouterLink
              v-for="item in exceptionItems"
              :key="item.path"
              class="grid grid-cols-[1fr_auto] gap-3 px-4 py-3 transition-colors hover:bg-accent"
              :to="item.path"
            >
              <span>
                <span class="block text-sm font-medium text-foreground">{{ item.label }}</span>
                <span class="mt-0.5 block text-sm text-muted-foreground">{{ item.detail }}</span>
              </span>
              <Badge variant="outline">{{ item.status }}</Badge>
            </RouterLink>
          </div>
        </section>

        <section class="rounded-lg border bg-background p-4">
          <h2 class="text-sm font-semibold text-foreground">工作台原则</h2>
          <div class="mt-3 grid gap-3 text-sm text-muted-foreground">
            <p>高频查询留在主页面，创建、报工、入库、排程等动作进入抽屉或对象详情。</p>
            <p>工厂、产线和班次逐步收敛为默认生产范围，减少手输技术字段。</p>
            <p>生产准备检查只作为开工/释放前的辅助判断，不作为一线主流程入口。</p>
          </div>
        </section>
      </div>
    </section>
  </BusinessLayout>
</template>
