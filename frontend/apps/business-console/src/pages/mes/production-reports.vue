<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesProductionReports } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Field,
  FieldGroup,
  FieldLabel,
  Input,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeader,
  TableRow,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({
  meta: {
    requiresAuth: true,
    title: '生产报工',
  },
})

const {
  filters,
  productionReports,
  productionReportsError,
  productionReportsPending,
  refreshProductionReports,
} = useMesProductionReports()

const errorMessage = computed(() => formatError(productionReportsError.value))
const goodQuantity = computed(() =>
  productionReports.value.reduce((total, item) => total + (item.goodQuantity ?? 0), 0),
)
const scrapQuantity = computed(() =>
  productionReports.value.reduce((total, item) => total + (item.scrapQuantity ?? 0), 0),
)

function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}

function formatQuantity(value?: number) {
  return new Intl.NumberFormat('zh-CN', { maximumFractionDigits: 3 }).format(value ?? 0)
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
        title="生产报工"
        summary="查看一线生产报工记录；新增报工仍从工单页选择工单和工序后提交。"
      >
        <template #actions>
          <Button size="sm" type="button" variant="outline" :disabled="productionReportsPending" @click="refreshProductionReports">
            <RefreshCwIcon data-icon="inline-start" />
            刷新
          </Button>
        </template>
      </BusinessPageHeader>

      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field>
            <FieldLabel for="report-list-org">组织</FieldLabel>
            <Input id="report-list-org" v-model="filters.organizationId" />
          </Field>
          <Field>
            <FieldLabel for="report-list-env">环境</FieldLabel>
            <Input id="report-list-env" v-model="filters.environmentId" />
          </Field>
          <Field>
            <FieldLabel for="report-list-status">状态</FieldLabel>
            <Input id="report-list-status" v-model="filters.status" placeholder="可选" />
          </Field>
          <Field>
            <FieldLabel for="report-list-take">数量</FieldLabel>
            <Input id="report-list-take" v-model.number="filters.take" inputmode="numeric" type="number" />
          </Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>

      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="报工记录" :value="productionReports.length" detail="当前筛选结果" />
        <BusinessMetricCell label="良品数" :value="formatQuantity(goodQuantity)" detail="累计良品" />
        <BusinessMetricCell label="报废数" :value="formatQuantity(scrapQuantity)" detail="累计报废" />
      </div>

      <div class="overflow-hidden rounded-lg border bg-background">
        <div class="overflow-x-auto">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>报工单</TableHead>
                <TableHead>工单</TableHead>
                <TableHead>工序任务</TableHead>
                <TableHead class="text-right">良品</TableHead>
                <TableHead class="text-right">报废</TableHead>
                <TableHead class="text-right">返工</TableHead>
                <TableHead>报工时间</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="row in productionReports" :key="row.productionReportId">
                <TableCell class="font-medium">{{ row.productionReportId ?? '无' }}</TableCell>
                <TableCell>{{ row.workOrderId ?? '无' }}</TableCell>
                <TableCell>{{ row.operationTaskId ?? '无' }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.goodQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.scrapQuantity) }}</TableCell>
                <TableCell class="text-right tabular-nums">{{ formatQuantity(row.reworkQuantity) }}</TableCell>
                <TableCell>{{ formatDateTime(row.reportedAtUtc) }}</TableCell>
              </TableRow>
              <TableEmpty v-if="!productionReports.length && !productionReportsPending" :colspan="7">
                暂无生产报工。
              </TableEmpty>
              <TableEmpty v-if="productionReportsPending" :colspan="7">正在加载生产报工...</TableEmpty>
            </TableBody>
          </Table>
        </div>
      </div>
    </section>
  </BusinessLayout>
</template>
