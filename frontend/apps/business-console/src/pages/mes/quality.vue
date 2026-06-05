<script setup lang="ts">
import BusinessFormStatus from '@/components/business/BusinessFormStatus.vue'
import BusinessPageHeader from '@/components/business/BusinessPageHeader.vue'
import { useMesRelatedQualityItems } from '@/composables/useBusinessMes'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { Button, DataTablePagination, Field, FieldGroup, FieldLabel, Input, Table, TableBody, TableCell, TableEmpty, TableHead, TableHeader, TableRow } from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '质量与不良' } })

const { filters, qualityItems, qualityItemsError, qualityItemsPending, qualityItemsTotal, refreshQualityItems } = useMesRelatedQualityItems()
const errorMessage = computed(() => qualityItemsError.value instanceof Error ? qualityItemsError.value.message : qualityItemsError.value ? '请求失败。' : '')
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.status] })
</script>

<template>
  <BusinessLayout>
    <section class="grid gap-4">
      <BusinessPageHeader domain="生产执行" title="质量与不良" summary="查看工单和工序关联的不良、检验、NCR 和质量阻塞。">
        <template #actions><Button size="sm" variant="outline" :disabled="qualityItemsPending" @click="refreshQualityItems"><RefreshCwIcon data-icon="inline-start" />刷新</Button></template>
      </BusinessPageHeader>
      <div class="grid gap-3 rounded-lg border bg-background p-4">
        <FieldGroup class="grid gap-3 md:grid-cols-4">
          <Field><FieldLabel for="quality-status">状态</FieldLabel><Input id="quality-status" v-model="filters.status" placeholder="可选" /></Field>
        </FieldGroup>
        <BusinessFormStatus :error="errorMessage" />
      </div>
      <div class="overflow-hidden rounded-lg border bg-background">
        <Table>
          <TableHeader><TableRow><TableHead>质量项</TableHead><TableHead>来源类型</TableHead><TableHead>来源单据</TableHead><TableHead>状态</TableHead><TableHead>缺陷代码</TableHead><TableHead>NCR</TableHead></TableRow></TableHeader>
          <TableBody>
            <TableRow v-for="row in qualityItems" :key="row.qualityItemId">
              <TableCell class="font-medium">{{ row.qualityItemId }}</TableCell>
              <TableCell>{{ row.sourceType ?? '未指定' }}</TableCell>
              <TableCell>{{ row.sourceDocumentId ?? '未指定' }}</TableCell>
              <TableCell>{{ row.status ?? '未知' }}</TableCell>
              <TableCell>{{ row.defectCode ?? '无' }}</TableCell>
              <TableCell>{{ row.ncrId ?? '无' }}</TableCell>
            </TableRow>
            <TableEmpty v-if="qualityItemsPending" :colspan="6">正在加载质量信息...</TableEmpty>
            <TableEmpty v-if="!qualityItems.length && !qualityItemsPending" :colspan="6">暂无质量或不良记录。</TableEmpty>
          </TableBody>
        </Table>
      </div>
      <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="qualityItemsTotal" />
    </section>
  </BusinessLayout>
</template>
