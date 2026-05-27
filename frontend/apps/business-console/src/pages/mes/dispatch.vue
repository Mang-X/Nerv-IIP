<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesDispatchTasks } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '派工看板' } })

const { dispatchTasks, dispatchTasksError, dispatchTasksPending, filters, refreshDispatchTasks } = useMesDispatchTasks()
const errorMessage = computed(() => dispatchTasksError.value instanceof Error ? dispatchTasksError.value.message : dispatchTasksError.value ? '请求失败。' : '')
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="MES" title="派工看板" summary="按工作中心、人员、设备和班次查看待派工工序。">
        <template #actions><Button size="sm" variant="outline" :disabled="dispatchTasksPending" @click="refreshDispatchTasks"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="dispatch-status">状态</FieldLabel><Input id="dispatch-status" v-model="filters.status" placeholder="可选" /></Field>
          <Field><FieldLabel for="dispatch-take">数量上限</FieldLabel><Input id="dispatch-take" v-model.number="filters.take" type="number" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="派工任务" :value="dispatchTasks.length" detail="当前筛选结果" />
        <BusinessMetricCell label="有阻塞" :value="dispatchTasks.filter((x) => x.blockingReasons?.length).length" detail="需处理" />
        <BusinessMetricCell label="可派工" :value="dispatchTasks.filter((x) => !x.blockingReasons?.length).length" detail="无阻塞" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>工序任务</TableHead><TableHead>工单</TableHead><TableHead>状态</TableHead><TableHead>工作中心</TableHead><TableHead>设备</TableHead><TableHead>班次</TableHead><TableHead>计划开始</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in dispatchTasks" :key="row.operationTaskId">
              <TableCell class="font-medium">{{ row.operationTaskId }}</TableCell>
              <TableCell>{{ row.workOrderId }}</TableCell>
              <TableCell>{{ row.status ?? '未知' }}</TableCell>
              <TableCell>{{ row.workCenterId }}</TableCell>
              <TableCell>{{ row.deviceAssetId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.shiftId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.plannedStartUtc ?? '未指定' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="dispatchTasksPending" :colspan="7">正在加载派工任务...</TableEmpty>
            <TableEmpty v-if="!dispatchTasks.length && !dispatchTasksPending" :colspan="7">暂无派工任务。</TableEmpty>
          </TableBody>
        </Table>
      </div>
    </section>
  </BusinessLayout>
</template>
