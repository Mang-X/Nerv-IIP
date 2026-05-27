<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesProductionReports } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '报工与完工' } })

const { filters, productionReports, productionReportsError, productionReportsPending, refreshProductionReports } = useMesProductionReports()
const errorMessage = computed(() => productionReportsError.value instanceof Error ? productionReportsError.value.message : productionReportsError.value ? '请求失败。' : '')
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="MES" title="报工与完工" summary="查看合格数、不良数、完工标记和后续入库请求依据。">
        <template #actions><Button size="sm" variant="outline" :disabled="productionReportsPending" @click="refreshProductionReports"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="reports-status">状态</FieldLabel><Input id="reports-status" v-model="filters.status" placeholder="可选" /></Field>
          <Field><FieldLabel for="reports-take">数量上限</FieldLabel><Input id="reports-take" v-model.number="filters.take" type="number" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>报工单</TableHead><TableHead>工单</TableHead><TableHead>工序任务</TableHead><TableHead>合格数</TableHead><TableHead>不良数</TableHead><TableHead>返工数</TableHead><TableHead>报工时间</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in productionReports" :key="row.productionReportId">
              <TableCell class="font-medium">{{ row.productionReportId }}</TableCell>
              <TableCell>{{ row.workOrderId }}</TableCell>
              <TableCell>{{ row.operationTaskId }}</TableCell>
              <TableCell>{{ row.goodQuantity ?? 0 }}</TableCell>
              <TableCell>{{ row.scrapQuantity ?? 0 }}</TableCell>
              <TableCell>{{ row.reworkQuantity ?? 0 }}</TableCell>
              <TableCell>{{ row.reportedAtUtc ?? '未指定' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="productionReportsPending" :colspan="7">正在加载报工记录...</TableEmpty>
            <TableEmpty v-if="!productionReports.length && !productionReportsPending" :colspan="7">暂无报工记录。</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </section>
  </BusinessLayout>
</template>
