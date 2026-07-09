<script setup lang="ts">
import type { BusinessConsoleErpOpportunityItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useErpOpportunities } from '@/composables/useBusinessErp'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSectionCard,
  NvSectionCards,
  Spinner,
  NvStatusBadge,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, shallowRef } from 'vue'
import { formatDateTime, formatError } from './shared'

definePage({
  meta: { requiresAuth: true, title: '销售机会', requiredPermissions: ['business.erp.sales.read'] },
})

const opportunities = useErpOpportunities()
const { page, pageSize } = usePagedList(opportunities.filters, {
  resetOn: [() => opportunities.filters.keyword],
})

const columns: NvDataTableColumn<BusinessConsoleErpOpportunityItem>[] = [
  {
    key: 'opportunityNo',
    header: '商机编号',
    cellClass: 'font-medium',
    accessor: (r) => r.opportunityNo ?? '-',
  },
  { key: 'customerCode', header: '客户', accessor: (r) => r.customerCode ?? '-' },
  { key: 'topic', header: '主题', accessor: (r) => r.topic ?? '-' },
  { key: 'status', header: '阶段', width: 'w-28' },
  {
    key: 'openedAtUtc',
    header: '创建时间',
    width: 'w-40',
    accessor: (r) => formatDateTime(r.openedAtUtc),
  },
]

const activeCount = computed(
  () => opportunities.items.value.filter((o) => (o.status ?? '').toLowerCase() === 'open').length,
)
const customerCount = computed(
  () => new Set(opportunities.items.value.map((o) => o.customerCode).filter(Boolean)).size,
)

const open = shallowRef(false)
const form = reactive({ customerCode: '', topic: '' })
const formError = shallowRef('')

function openDialog() {
  form.customerCode = ''
  form.topic = ''
  formError.value = ''
  open.value = true
}

async function submit() {
  if (!form.customerCode.trim() || !form.topic.trim()) {
    formError.value = '请填写客户与商机主题。'
    return
  }
  try {
    await opportunities.openOpportunity({
      customerCode: form.customerCode.trim(),
      topic: form.topic.trim(),
    })
    open.value = false
    toast.success('销售机会已开立')
  } catch {
    formError.value =
      formatError(opportunities.openOpportunityError.value) || '开立失败，请稍后重试。'
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售机会"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="`${opportunities.total.value} 个机会`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="opportunities.pending.value"
          @click="opportunities.refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openDialog">
          <PlusIcon aria-hidden="true" />
          开立机会
        </NvButton>
      </template>
    </NvPageHeader>

    <NvSectionCards :columns="2">
      <NvSectionCard description="跟进中机会" :value="activeCount" hint="可转报价的客户机会" />
      <NvSectionCard description="涉及客户" :value="customerCount" hint="本页机会覆盖客户数" />
    </NvSectionCards>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="opportunities.filters.keyword"
          class="h-9 w-64"
          placeholder="商机编号 / 客户 / 主题"
          aria-label="销售机会关键字"
        />
      </template>
    </NvToolbar>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="opportunities.total.value"
      :columns="columns"
      :rows="opportunities.items.value"
      :row-key="(r: BusinessConsoleErpOpportunityItem) => r.opportunityNo ?? '销售机会'"
      :loading="opportunities.pending.value"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无销售机会。先登记客户意向，再推进报价和销售订单。"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>开立销售机会</NvDialogTitle>
          <NvDialogDescription
            >登记客户线索与机会主题，作为报价前的销售上下文。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup>
            <NvField
              ><NvFieldLabel for="erp-opp-customer">客户</NvFieldLabel
              ><NvInput id="erp-opp-customer" v-model="form.customerCode" autocomplete="off"
            /></NvField>
            <NvField
              ><NvFieldLabel for="erp-opp-topic">机会主题</NvFieldLabel
              ><NvInput id="erp-opp-topic" v-model="form.topic" autocomplete="off"
            /></NvField>
          </NvFieldGroup>
          <NvFieldError v-if="formError" :errors="[formError]" />
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="opportunities.openOpportunityPending.value">
              <Spinner v-if="opportunities.openOpportunityPending.value" aria-hidden="true" />
              开立
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
