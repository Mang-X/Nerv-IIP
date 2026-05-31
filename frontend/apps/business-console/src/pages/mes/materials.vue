<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesMaterialIssueRequests } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '齐套与物料' } })

const { filters, materialIssueRequests, materialIssueRequestsError, materialIssueRequestsPending, refreshMaterialIssueRequests } = useMesMaterialIssueRequests()
const errorMessage = computed(() => materialIssueRequestsError.value instanceof Error ? materialIssueRequestsError.value.message : materialIssueRequestsError.value ? '请求失败。' : '')
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="生产执行" title="齐套与物料" summary="跟踪工单齐套、领料申请和线边收料状态。">
        <template #actions><Button size="sm" variant="outline" :disabled="materialIssueRequestsPending" @click="refreshMaterialIssueRequests"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="materials-status">状态</FieldLabel><Input id="materials-status" v-model="filters.status" placeholder="可选" /></Field>
          <Field><FieldLabel for="materials-take">数量上限</FieldLabel><Input id="materials-take" v-model.number="filters.take" type="number" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="领料申请" :value="materialIssueRequests.length" detail="当前筛选结果" />
        <BusinessMetricCell label="待处理" :value="materialIssueRequests.filter((x) => x.status !== 'Closed').length" detail="未关闭申请" />
        <BusinessMetricCell label="已关闭" :value="materialIssueRequests.filter((x) => x.status === 'Closed').length" detail="已完成收料" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>申请号</TableHead><TableHead>工单</TableHead><TableHead>状态</TableHead><TableHead>WMS 单号</TableHead><TableHead>申请时间</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in materialIssueRequests" :key="row.requestId">
              <TableCell class="font-medium">{{ row.requestId }}</TableCell>
              <TableCell>{{ row.workOrderId }}</TableCell>
              <TableCell>{{ row.status ?? '未知' }}</TableCell>
              <TableCell>{{ row.wmsRequestId ?? '未下发' }}</TableCell>
              <TableCell>{{ row.requestedAtUtc ?? '未指定' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="materialIssueRequestsPending" :colspan="5">正在加载领料申请...</TableEmpty>
            <TableEmpty v-if="!materialIssueRequests.length && !materialIssueRequestsPending" :colspan="5">暂无领料申请。</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </section>
  </BusinessLayout>
</template>
