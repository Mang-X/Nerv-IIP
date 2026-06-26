<script setup lang="ts">
import type { DataTableProColumn } from '@nerv-iip/ui'
import { useMesTraceability } from '@/composables/useBusinessMes'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  ButtonPro,
  DataTablePro,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Toolbar,
} from '@nerv-iip/ui'
import { RefreshCwIcon } from 'lucide-vue-next'
import { computed } from 'vue'

definePage({ meta: { requiresAuth: true, title: '追溯查询' } })

const { filters, refreshTraceability, traceability, traceabilityError, traceabilityPending } = useMesTraceability()

const nodes = computed(() => traceability.value?.nodes ?? [])
const errorMessage = computed(() => formatError(traceabilityError.value))
const batchModel = computed({
  get: () => filters.batchOrSerial ?? '',
  set: (value: string) => { filters.batchOrSerial = value; filters.materialLotId = value },
})

type NodeRow = (typeof nodes)['value'][number]
const columns: DataTableProColumn<NodeRow>[] = [
  { key: 'nodeId', header: '节点', cellClass: 'font-medium' },
  { key: 'nodeType', header: '类型', width: 'w-32' },
  { key: 'displayName', header: '名称' },
  { key: 'status', header: '状态', width: 'w-28' },
]

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="追溯查询" :breadcrumbs="[{ label: '制造执行' }]">
      <template #actions>
        <ButtonPro size="sm" type="button" variant="outline" :disabled="traceabilityPending" @click="refreshTraceability">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>

    <SectionCards :columns="2">
      <SectionCard description="节点" :value="nodes.length" hint="执行证据对象" />
      <SectionCard description="关系" :value="traceability?.edges?.length ?? 0" hint="上下游关联" />
    </SectionCards>

    <Toolbar :show-search="false">
      <template #filters>
        <SelectPro v-model="filters.mode">
          <SelectProTrigger class="h-9 w-36" aria-label="查询类型"><SelectProValue /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="work-order">工单</SelectProItem>
            <SelectProItem value="batch">批次/序列号</SelectProItem>
            <SelectProItem value="material-lot">物料批</SelectProItem>
          </SelectProContent>
        </SelectPro>
        <InputPro v-model="filters.workOrderId" class="h-9 w-40" placeholder="工单号" aria-label="工单号" />
        <InputPro v-model="batchModel" class="h-9 w-44" placeholder="批次/序列号/物料批" aria-label="批次或物料批" />
      </template>
    </Toolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="nodes"
      row-key="nodeId"
      :loading="traceabilityPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无追溯数据。输入工单、批次/序列号或物料批后查询执行证据链。"
    />
  </BusinessLayout>
</template>
