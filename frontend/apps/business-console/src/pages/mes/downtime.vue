<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessMetricCell from '@/components/business/BusinessMetricCell.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesDowntimeEvents } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, DataTablePagination, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '设备与停机' } })

const { downtimeEvents, downtimeEventsError, downtimeEventsPending, downtimeEventsTotal, filters, refreshDowntimeEvents } = useMesDowntimeEvents()
const errorMessage = computed(() => downtimeEventsError.value instanceof Error ? downtimeEventsError.value.message : downtimeEventsError.value ? '请求失败。' : '')
const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)

watch(() => filters.status, () => {
  page.value = 1
})

watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="生产执行" title="设备与停机" summary="记录和查看影响生产执行的设备停机、恢复与未结异常。">
        <template #actions><Button size="sm" variant="outline" :disabled="downtimeEventsPending" @click="refreshDowntimeEvents"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="downtime-status">状态</FieldLabel><Input id="downtime-status" v-model="filters.status" placeholder="可选" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="grid gap-3 md:grid-cols-3">
        <BusinessMetricCell label="停机事件" :value="downtimeEventsTotal" detail="后端筛选总数" />
        <BusinessMetricCell label="未恢复" :value="downtimeEvents.filter((x) => x.status === 'Open').length" detail="需处理" />
        <BusinessMetricCell label="已恢复" :value="downtimeEvents.filter((x) => x.status !== 'Open').length" detail="已关闭" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>停机事件</TableHead><TableHead>工单</TableHead><TableHead>工序任务</TableHead><TableHead>设备</TableHead><TableHead>状态</TableHead><TableHead>开始</TableHead><TableHead>恢复</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in downtimeEvents" :key="row.downtimeEventId">
              <TableCell class="font-medium">{{ row.downtimeEventId }}</TableCell>
              <TableCell>{{ row.workOrderId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.operationTaskId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.deviceAssetId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.status ?? '未知' }}</TableCell>
              <TableCell>{{ row.startedAtUtc ?? '未指定' }}</TableCell>
              <TableCell>{{ row.recoveredAtUtc ?? '未恢复' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="downtimeEventsPending" :colspan="7">正在加载停机事件...</TableEmpty>
            <TableEmpty v-if="!downtimeEvents.length && !downtimeEventsPending" :colspan="7">暂无停机事件。</TableEmpty>
          </TableBody>
        </Table>
      </div>
      <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="downtimeEventsTotal" />
    </section>
  </BusinessLayout>
</template>
