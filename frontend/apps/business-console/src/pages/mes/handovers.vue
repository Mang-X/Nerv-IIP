<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesShiftHandovers } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, DataTablePagination, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '班次交接' } })

const { filters, handovers, handoversError, handoversPending, handoversTotal, refreshHandovers } = useMesShiftHandovers()
const errorMessage = computed(() => handoversError.value instanceof Error ? handoversError.value.message : handoversError.value ? '请求失败。' : '')
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="生产执行" title="班次交接" summary="承接未完成工单、物料、质量、设备和入库事项。">
        <template #actions><Button size="sm" variant="outline" :disabled="handoversPending" @click="refreshHandovers"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="handover-status">状态</FieldLabel><Input id="handover-status" v-model="filters.status" placeholder="可选" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>交接单</TableHead><TableHead>班次</TableHead><TableHead>班组</TableHead><TableHead>状态</TableHead><TableHead>未结事项</TableHead><TableHead>创建时间</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in handovers" :key="row.handoverId">
              <TableCell class="font-medium">{{ row.handoverId }}</TableCell>
              <TableCell>{{ row.shiftId }}</TableCell>
              <TableCell>{{ row.teamId }}</TableCell>
              <TableCell>{{ row.handoverStatus ?? '未知' }}</TableCell>
              <TableCell>{{ row.openIssueCount ?? 0 }}</TableCell>
              <TableCell>{{ row.createdAtUtc ?? '未指定' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="handoversPending" :colspan="6">正在加载班次交接...</TableEmpty>
            <TableEmpty v-if="!handovers.length && !handoversPending" :colspan="6">暂无班次交接。</TableEmpty>
          </TableBody>
        </Table>
      </div>
      <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="handoversTotal" />
    </section>
  </BusinessLayout>
</template>
