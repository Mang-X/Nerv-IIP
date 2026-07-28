<script setup lang="ts">
import type { BusinessConsoleErpOpportunityItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import { useErpOpportunities } from '@/composables/useBusinessErp'
import { useErpPartnerCatalog } from '@/composables/useErpPickerCatalog'
import { useBusinessPartnerNames } from '@/composables/useBusinessPartnerNames'
import { usePagedList } from '@/composables/usePagedList'
import PartnerNameCell from '@/components/erp/PartnerNameCell.vue'
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
  NvEntityPicker,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'
import { erpReadState, formatDateTime, pickerInvalidClass, readCount } from '../shared'

definePage({
  meta: { requiresAuth: true, title: '销售机会', requiredPermissions: ['business.erp.sales.read'] },
})

const opportunities = useErpOpportunities()
// 客户从业务伙伴主数据里选，机会一开立就挂在真实客户上。
const { customerOptions, partnersPending } = useErpPartnerCatalog()
// 列表侧另需 code→name 反查（目录只给下拉选项，不做反查）；底层同一份查询，不会重复请求。
const { resolvePartner } = useBusinessPartnerNames()
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
  {
    key: 'customerCode',
    header: '客户',
    accessor: (r) => resolvePartner(r.customerCode) ?? r.customerCode ?? '-',
  },
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
// 机会页的两个数字是同一句话的两半（还在谈几单 / 覆盖几个客户），通栏一条即可，
// 不必占满两张大卡把表格挤到首屏之外。
const readState = computed(() =>
  erpReadState({
    noun: '销售机会',
    unit: '个',
    ready: opportunities.ready.value,
    pending: opportunities.pending.value,
    error: opportunities.error.value,
    total: opportunities.total.value,
    filtered: Boolean(opportunities.filters.keyword || opportunities.filters.status),
    emptyHint: '还没有销售机会。先登记客户意向，再推进报价和销售订单。',
  }),
)

const opportunityCells = computed<NvMetricStripCell[]>(() => [
  {
    key: 'active',
    label: '跟进中机会',
    value: readCount(readState.value, activeCount.value),
    unit: readState.value.trustworthy ? '个' : '',
    meta: readState.value.trustworthy ? undefined : readState.value.emptyMessage,
  },
  {
    key: 'customers',
    label: '涉及客户',
    value: readCount(readState.value, customerCount.value),
    unit: readState.value.trustworthy ? '家' : '',
    meta: readState.value.trustworthy ? undefined : readState.value.emptyMessage,
  },
])

const open = shallowRef(false)
const form = reactive({ customerCode: '', topic: '' })
// 点提交才标红；结果一律 toast，弹窗不留常驻结果条。
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  customerCode: !form.customerCode.trim(),
  topic: !form.topic.trim(),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openDialog() {
  form.customerCode = ''
  form.topic = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await opportunities.openOpportunity({
      customerCode: form.customerCode.trim(),
      topic: form.topic.trim(),
    })
    open.value = false
    notifySuccess('销售机会已开立')
  } catch (error) {
    notifyError(opportunities.openOpportunityError.value ?? error, '开立机会失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="销售机会"
      :breadcrumbs="[{ label: '经营管理' }, { label: '销售' }]"
      :count="readState.count"
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

    <NvMetricStrip :cells="opportunityCells" />

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
      :empty-message="readState.emptyMessage"
      :error="readState.error"
      :error-message="readState.errorMessage"
      :awaiting-scope="readState.awaitingScope"
      :awaiting-scope-message="readState.awaitingScopeMessage"
      @retry="opportunities.refresh"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
    >
      <template #cell-customerCode="{ row }">
        <PartnerNameCell :code="row.customerCode" />
      </template>
      <template #cell-status="{ row }"><NvStatusBadge :value="row.status ?? '-'" /></template>
    </NvDataTable>

    <NvDialog v-model:open="open">
      <NvDialogContent>
        <NvDialogHeader>
          <NvDialogTitle>开立销售机会</NvDialogTitle>
          <NvDialogDescription class="sr-only">登记客户与机会主题。</NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submit">
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="erp-opp-customer">
                客户 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvEntityPicker
                id="erp-opp-customer"
                v-model="form.customerCode"
                :options="customerOptions"
                title="选择客户"
                placeholder="选择客户"
                source-text="数据来自基础数据业务伙伴（客户角色）"
                empty-text="暂无客户，请先在「基础数据 · 业务伙伴」维护"
                :loading="partnersPending"
                aria-label="客户"
                :class="pickerInvalidClass(showErrors && invalid.customerCode)"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-opp-topic">
                机会主题 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-opp-topic"
                v-model="form.topic"
                autocomplete="off"
                :data-invalid="showErrors && invalid.topic ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请选择客户并填写机会主题。
          </p>
          <NvDialogFooter>
            <NvDialogClose as-child
              ><NvButton type="button" variant="outline">取消</NvButton></NvDialogClose
            >
            <NvButton type="submit" :disabled="opportunities.openOpportunityPending.value">
              <Spinner v-if="opportunities.openOpportunityPending.value" aria-hidden="true" />
              开立机会
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
