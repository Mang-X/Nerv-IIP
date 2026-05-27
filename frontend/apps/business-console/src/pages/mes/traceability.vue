<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesTraceability } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, Field, FieldGroup, FieldLabel, Input, Select, SelectContent, SelectItem, SelectTrigger, SelectValue, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '追溯查询' } })

const { filters, refreshTraceability, traceability, traceabilityError, traceabilityPending } = useMesTraceability()
const errorMessage = computed(() => traceabilityError.value instanceof Error ? traceabilityError.value.message : traceabilityError.value ? '请求失败。' : '')
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="MES" title="追溯查询" summary="从工单、批次/序列号或物料批查询执行证据链。">
        <template #actions><Button size="sm" variant="outline" :disabled="traceabilityPending" @click="refreshTraceability"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-5">
          <Field>
            <FieldLabel for="trace-mode">查询类型</FieldLabel>
            <Select v-model="filters.mode"><SelectTrigger id="trace-mode"><SelectValue /></SelectTrigger><SelectContent><SelectItem value="work-order">工单</SelectItem><SelectItem value="batch">批次/序列号</SelectItem><SelectItem value="material-lot">物料批</SelectItem></SelectContent></Select>
          </Field>
          <Field><FieldLabel for="trace-wo">工单</FieldLabel><Input id="trace-wo" v-model="filters.workOrderId" /></Field>
          <Field><FieldLabel for="trace-batch">批次/物料批</FieldLabel><Input id="trace-batch" v-model="filters.batchOrSerial" @update:model-value="filters.materialLotId = String($event ?? '')" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="grid gap-3 md:grid-cols-2">
        <BusinessMetricCell label="节点" :value="traceability?.nodes?.length ?? 0" detail="执行证据对象" />
        <BusinessMetricCell label="关系" :value="traceability?.edges?.length ?? 0" detail="上下游关联" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>节点</TableHead><TableHead>类型</TableHead><TableHead>名称</TableHead><TableHead>状态</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="node in traceability?.nodes ?? []" :key="node.nodeId">
              <TableCell class="font-medium">{{ node.nodeId }}</TableCell>
              <TableCell>{{ node.nodeType }}</TableCell>
              <TableCell>{{ node.displayName }}</TableCell>
              <TableCell>{{ node.status }}</TableCell>
            </TableRow>
            <TableEmpty v-if="traceabilityPending" :colspan="4">正在加载追溯数据...</TableEmpty>
            <TableEmpty v-if="!(traceability?.nodes?.length) && !traceabilityPending" :colspan="4">暂无追溯数据。</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </section>
  </BusinessLayout>
</template>
