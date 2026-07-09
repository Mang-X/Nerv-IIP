<script setup lang="ts">
import type { DataTableColumn } from '@nerv-iip/ui'
import DefaultLayout from '@/layouts/DefaultLayout.vue'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  NvDataTable,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  NvStatusBadge,
} from '@nerv-iip/ui'

definePage({
  meta: {
    requiresAuth: true,
    title: 'routes.business',
  },
})

interface BusinessService {
  name: string
  scope: string
}

// 已交付的业务后端服务快照（平台视角）。仅展示服务与能力范围，不暴露工程内部追踪号。
const businessServices: BusinessService[] = [
  { name: 'BusinessMasterData', scope: '基础主数据（Layer 0）' },
  { name: 'BusinessProductEngineering', scope: '工程文档、物料、BOM、工艺路线与变更' },
  { name: 'BusinessInventory', scope: '库位、台账、移动与盘点' },
  { name: 'BusinessQuality', scope: '检验计划、记录与不良事实' },
  { name: 'BusinessMES', scope: '工单、工序任务与执行报工' },
  { name: 'BusinessDemandPlanning', scope: '需求来源、MPS、MRP 与挂账' },
  { name: 'BarcodeLabel', scope: '条码规则、标签模板、打印批次与扫描' },
  { name: 'BusinessApproval', scope: '业务审批模板、审批链、步骤与决策' },
  { name: 'WMS', scope: '入库、出库、仓库任务与 WCS 边界' },
  { name: 'BusinessIndustrialTelemetry', scope: '遥测点位、设备快照、告警与汇总' },
  { name: 'BusinessMaintenance', scope: '工单、PM 计划、点检、停机与备件' },
  { name: 'BusinessERP', scope: '采购、销售与财务 MVP 事实' },
]

const columns: DataTableColumn<BusinessService>[] = [
  { key: 'name', header: '服务', cellClass: 'font-medium' },
  { key: 'scope', header: '能力范围', cellClass: 'text-muted-foreground' },
  { key: 'status', header: '状态', align: 'end', width: 'w-24' },
]
</script>

<template>
  <DefaultLayout>
    <section class="grid gap-6">
      <NvPageHeader
        title="业务平台状态"
        :breadcrumbs="[{ label: '平台' }]"
        :count="`${businessServices.length} 项服务`"
      />

      <NvSectionCards :columns="3">
        <NvSectionCard
          description="已交付服务"
          :value="businessServices.length"
          hint="业务后端能力层"
        />
        <NvSectionCard description="就绪状态" value="已交付" hint="后端能力可用" />
        <NvSectionCard description="覆盖范围" value="主线 MVP" hint="跨服务全链路验收推进中" />
      </NvSectionCards>

      <div class="grid gap-4 xl:grid-cols-[minmax(0,1fr)_22rem]">
        <div class="grid min-w-0 content-start gap-2">
          <p class="text-sm text-muted-foreground">
            业务平台 MVP 已交付的后端能力与当前范围边界快照。
          </p>
          <NvDataTable
            :pagination="false"
            :searchable="false"
            :column-settings="false"
            :columns="columns"
            :rows="businessServices"
            row-key="name"
            empty-message="暂无已交付的业务服务。"
          >
            <template #cell-status>
              <NvStatusBadge label="已交付" tone="success" />
            </template>
          </NvDataTable>
        </div>

        <Card class="content-start">
          <CardHeader>
            <CardTitle>范围说明</CardTitle>
            <CardDescription>业务平台在本控制台的能力边界。</CardDescription>
          </CardHeader>
          <CardContent class="grid gap-2 text-sm text-muted-foreground">
            <p>
              下一步聚焦打通各业务服务的全链路验收路径，将已交付的 MVP 事实串成可验证的跨服务链路。
            </p>
            <p>
              甘特图 / RFC 等排程编辑能力不在本控制台 MVP 范围，此处不引入时间线编辑器或排程工作流。
            </p>
          </CardContent>
        </Card>
      </div>
    </section>
  </DefaultLayout>
</template>
