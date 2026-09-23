<script setup lang="ts">
import type { BusinessConsoleErpWorkCenterCostRateItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn, NvMetricStripCell } from '@nerv-iip/ui'
import {
  NvButton,
  NvDataTable,
  NvEntityPicker,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvMetricStrip,
  NvPageHeader,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetFooter,
  NvSheetHeader,
  NvSheetTitle,
  NvStatusBadge,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'
import { PlusIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { useErpWorkCenterCostRates } from '@/composables/useBusinessErp'
import { useEquipmentWorkCenterCatalog } from '@/composables/useEquipmentPickerCatalog'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { today } from '@/utils/format'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { UNAVAILABLE_TEXT, formatAmount, formatDateTime } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '工作中心费率',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

type RateRow = BusinessConsoleErpWorkCenterCostRateItem

const auth = useAuthStore()
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.erpFinanceManage),
)

const { workCenterOptions, workCentersPending } = useEquipmentWorkCenterCatalog()

const costs = useErpWorkCenterCostRates()
const items = computed(() => costs.rates.value?.items ?? [])
const current = computed(() => items.value.find((row) => row.isCurrentEffectiveRevision))
// 同一工作中心的币种在首个修订后固定；列表按版本倒序，取任一行即可。
const fixedCurrency = computed(() => items.value[0]?.currencyCode)

function hourly(row: RateRow) {
  return `${formatAmount(row.hourlyRate, row.currencyCode)} / 小时`
}
function period(row: RateRow) {
  return `${formatDateTime(row.effectiveFromUtc)} 起${
    row.effectiveToUtc ? ` 至 ${formatDateTime(row.effectiveToUtc)}` : ''
  }`
}

const currentCells = computed<NvMetricStripCell[]>(() => {
  const row = current.value
  if (!costs.rates.value) {
    return [{ key: 'rate', label: '当前有效费率', value: UNAVAILABLE_TEXT }]
  }
  if (!row) {
    return [
      {
        key: 'rate',
        label: '当前有效费率',
        value: '暂无生效费率',
        meta: '在有修订生效前，报工无法计算人工成本，完工入库会停在待入库。',
      },
    ]
  }
  return [
    { key: 'rate', label: '当前有效费率', value: hourly(row) },
    { key: 'period', label: '生效期间', value: period(row) },
    { key: 'revision', label: '修订版本', value: `第 ${row.revision} 版`, meta: row.reason },
  ]
})

type StatusView = { value: string; label: string; tone?: 'neutral' }
function statusOf(row: RateRow): StatusView {
  if (row.isCurrentEffectiveRevision) return { value: 'active', label: '当前生效' }
  if (row.effectiveStatus === 'future') return { value: 'scheduled', label: '未到生效时间' }
  if (row.effectiveStatus === 'expired')
    return { value: 'expired', label: '已过期', tone: 'neutral' }
  return { value: 'superseded', label: '已被新修订取代' }
}

const columns: NvDataTableColumn<RateRow>[] = [
  { key: 'revision', header: '版本', width: 'w-20', accessor: (row) => `第 ${row.revision} 版` },
  { key: 'hourlyRate', header: '每小时费率', align: 'end', accessor: hourly },
  { key: 'period', header: '生效期间', accessor: period },
  { key: 'status', header: '状态', width: 'w-36' },
  { key: 'reason', header: '修订原因', accessor: (row) => row.reason ?? '-' },
  { key: 'changedAtUtc', header: '修订时间', accessor: (row) => formatDateTime(row.changedAtUtc) },
]

const open = shallowRef(false)
const form = reactive({ hourlyRate: '', currencyCode: 'CNY', effectiveFrom: '', reason: '' })
const showErrors = shallowRef(false)
const invalid = computed(() => ({
  hourlyRate: !(Number(form.hourlyRate) > 0),
  currencyCode: !/^[A-Za-z]{3}$/.test(form.currencyCode.trim()),
  effectiveFrom: !form.effectiveFrom,
  reason: !form.reason.trim(),
}))
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openSheet() {
  form.hourlyRate = current.value ? String(current.value.hourlyRate) : ''
  form.currencyCode = fixedCurrency.value ?? 'CNY'
  form.effectiveFrom = today()
  form.reason = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  try {
    await costs.addRevision({
      hourlyRate: Number(form.hourlyRate),
      currencyCode: form.currencyCode.trim().toUpperCase(),
      // 生效日期按本地零点起算。
      effectiveFromUtc: new Date(`${form.effectiveFrom}T00:00:00`).toISOString(),
      reason: form.reason.trim(),
    })
    open.value = false
    notifySuccess('费率修订已保存')
  } catch (error) {
    notifyOperationFailure(
      '保存费率修订失败',
      costs.addRevisionError.value ?? error,
      '保存费率修订失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="工作中心费率" :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]">
      <template v-if="canManage" #actions>
        <NvButton size="sm" type="button" :disabled="!costs.rates.value" @click="openSheet"
          ><PlusIcon aria-hidden="true" />新增修订</NvButton
        >
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="costs.workCenterId.value"
          class="w-80"
          :options="workCenterOptions"
          title="选择工作中心"
          placeholder="选择工作中心"
          empty-text="暂无工作中心，请先在「基础数据 · 工作中心」维护"
          :loading="workCentersPending"
          aria-label="工作中心"
        />
      </template>
    </NvToolbar>

    <NvMetricStrip v-if="costs.workCenterId.value" :cells="currentCells" />

    <NvDataTable
      :columns="columns"
      :rows="items"
      :row-key="(row: RateRow) => row.workCenterCostRateId ?? String(row.revision)"
      :loading="costs.pending.value"
      :error="Boolean(costs.error.value)"
      error-message="费率读取失败，请重试。"
      :searchable="false"
      :column-settings="false"
      :awaiting-scope="!costs.ready.value || !costs.workCenterId.value"
      awaiting-scope-message="请选择工作中心。"
      empty-message="该工作中心还没有费率修订。"
      @retry="costs.refresh"
    >
      <template #cell-status="{ row }">
        <NvStatusBadge v-bind="statusOf(row)" />
      </template>
    </NvDataTable>

    <NvSheet v-if="canManage" v-model:open="open">
      <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>新增费率修订</NvSheetTitle>
          <NvSheetDescription class="sr-only"
            >为所选工作中心追加一个人工费率修订。</NvSheetDescription
          >
        </NvSheetHeader>
        <form class="grid content-start gap-4 p-4" @submit.prevent="submit">
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请填写正数费率、三位字母币种、生效日期和修订原因。
          </p>
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel for="erp-wcr-rate">
                每小时费率 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-wcr-rate"
                v-model="form.hourlyRate"
                type="number"
                min="0"
                step="0.01"
                :data-invalid="showErrors && invalid.hourlyRate ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-wcr-currency">
                币种 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-wcr-currency"
                v-model="form.currencyCode"
                maxlength="3"
                :disabled="Boolean(fixedCurrency)"
                :data-invalid="showErrors && invalid.currencyCode ? '' : undefined"
              />
              <NvFieldDescription v-if="fixedCurrency">
                该工作中心的币种已固定为 {{ fixedCurrency }}。
              </NvFieldDescription>
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-wcr-from">
                生效日期 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-wcr-from"
                v-model="form.effectiveFrom"
                type="date"
                :data-invalid="showErrors && invalid.effectiveFrom ? '' : undefined"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-wcr-reason">
                修订原因 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-wcr-reason"
                v-model="form.reason"
                maxlength="500"
                :data-invalid="showErrors && invalid.reason ? '' : undefined"
              />
            </NvField>
          </NvFieldGroup>
          <NvSheetFooter>
            <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
            <NvButton type="submit" :disabled="costs.addRevisionPending.value">
              <Spinner v-if="costs.addRevisionPending.value" aria-hidden="true" />保存修订
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
