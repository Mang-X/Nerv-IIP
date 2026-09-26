<script setup lang="ts">
import type {
  BusinessConsoleErpMachineOverheadApplicability,
  BusinessConsoleErpWorkCenterMachineOverheadRateItem,
} from '@nerv-iip/api-client'
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
  NvRadioGroup,
  NvRadioGroupItem,
  NvSearchSelect,
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
import { useErpWorkCenterMachineOverheadRates } from '@/composables/useErpCostAccounting'
import { useEquipmentWorkCenterCatalog } from '@/composables/useEquipmentPickerCatalog'
import { currencyOptionsIncluding } from '@/data/currencyReference'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { today } from '@/utils/format'
import { notifyOperationFailure, notifySuccess } from '@/utils/notify'
import { machineAmount, machineHours } from '../machineOverhead'
import { UNAVAILABLE_TEXT, formatDateTime } from '../shared'

definePage({
  meta: {
    requiresAuth: true,
    title: '机器制造费用率',
    requiredPermissions: ['business.erp.finance.read'],
  },
})

type RateRow = BusinessConsoleErpWorkCenterMachineOverheadRateItem

const auth = useAuthStore()
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.erpFinanceManage),
)

const { workCenterOptions, workCentersPending } = useEquipmentWorkCenterCatalog()

const rates = useErpWorkCenterMachineOverheadRates()
// 会计期间编码默认取本月（如 2026-09），与财务开立期间的惯例一致；可改为其它期间。
rates.accountingPeriodCode.value = today().slice(0, 7)
const periodDraft = shallowRef(rates.accountingPeriodCode.value)
function commitPeriod() {
  rates.accountingPeriodCode.value = periodDraft.value.trim()
}

const items = computed(() => rates.rates.value?.items ?? [])
const current = computed(() =>
  items.value.find((row) => row.revision === rates.rates.value?.currentRevision),
)
// 同一工作中心的币种在首个修订后固定；列表按版本倒序，取任一行即可。
const fixedCurrency = computed(() => items.value[0]?.currencyCode)

const applicable = (row: RateRow) => row.applicability === 'applicable'
const perHour = (value: number, row: RateRow) =>
  `${machineAmount(value, row.currencyCode)} / 机器小时`

const currentCells = computed<NvMetricStripCell[]>(() => {
  const row = current.value
  if (!rates.rates.value) {
    return [{ key: 'rate', label: '本期费率', value: UNAVAILABLE_TEXT }]
  }
  if (!row) {
    return [
      {
        key: 'rate',
        label: '本期费率',
        value: '未配置',
        meta: '补录前，该工作中心在本期带设备的工序完工无法结算机器费用。',
      },
    ]
  }
  const revision = {
    key: 'revision',
    label: '修订版本',
    value: `第 ${row.revision} 版`,
    meta: row.reason,
  }
  if (!applicable(row)) {
    return [
      { key: 'rate', label: '本期费率', value: '不适用', meta: '本期不计机器制造费用。' },
      revision,
    ]
  }
  return [
    {
      key: 'rate',
      label: '本期费率',
      value: perHour(row.totalHourlyRate, row),
      meta: `固定 ${machineAmount(row.fixedHourlyRate, row.currencyCode)} · 变动 ${machineAmount(row.variableHourlyRate, row.currencyCode)}`,
    },
    {
      key: 'basis',
      label: '正常产能',
      value: machineHours(row.normalCapacityMachineHours),
      meta: `固定预算 ${machineAmount(row.fixedOverheadBudget, row.currencyCode)} · 变动预算 ${machineAmount(row.variableOverheadBudget, row.currencyCode)}`,
    },
    revision,
  ]
})

const columns: NvDataTableColumn<RateRow>[] = [
  { key: 'revision', header: '版本', width: 'w-20', accessor: (row) => `第 ${row.revision} 版` },
  { key: 'rate', header: '每机器小时费率（固定 / 变动 / 合计）' },
  { key: 'basis', header: '预算与正常产能' },
  { key: 'status', header: '状态', width: 'w-36' },
  { key: 'reason', header: '修订原因', accessor: (row) => row.reason },
  { key: 'changedAtUtc', header: '修订时间', accessor: (row) => formatDateTime(row.changedAtUtc) },
]

const open = shallowRef(false)
const form = reactive({
  applicability: 'applicable' as BusinessConsoleErpMachineOverheadApplicability,
  fixedOverheadBudget: '',
  variableOverheadBudget: '',
  normalCapacityMachineHours: '',
  currencyCode: 'CNY',
  reason: '',
})
const showErrors = shallowRef(false)
const isApplicable = computed(() => form.applicability === 'applicable')
const invalid = computed(() => {
  const fixed = Number(form.fixedOverheadBudget || 0)
  const variable = Number(form.variableOverheadBudget || 0)
  return {
    budget: isApplicable.value && !(fixed >= 0 && variable >= 0 && (fixed > 0 || variable > 0)),
    capacity: isApplicable.value && !(Number(form.normalCapacityMachineHours) > 0),
    reason: !form.reason.trim(),
  }
})
const canSubmit = computed(() => !Object.values(invalid.value).some(Boolean))

function openSheet() {
  const row = current.value
  const basis = row && applicable(row) ? row : undefined
  form.applicability = row?.applicability ?? 'applicable'
  form.fixedOverheadBudget = basis ? String(basis.fixedOverheadBudget) : ''
  form.variableOverheadBudget = basis ? String(basis.variableOverheadBudget) : ''
  form.normalCapacityMachineHours = basis ? String(basis.normalCapacityMachineHours) : ''
  form.currencyCode = fixedCurrency.value ?? 'CNY'
  form.reason = ''
  showErrors.value = false
  open.value = true
}

async function submit() {
  showErrors.value = true
  if (!canSubmit.value) return
  // 不适用时预算与产能按零提交。
  const amount = (value: string) => (isApplicable.value ? Number(value || 0) : 0)
  try {
    await rates.addRevision({
      applicability: form.applicability,
      fixedOverheadBudget: amount(form.fixedOverheadBudget),
      variableOverheadBudget: amount(form.variableOverheadBudget),
      normalCapacityMachineHours: amount(form.normalCapacityMachineHours),
      currencyCode: form.currencyCode,
      reason: form.reason.trim(),
    })
    open.value = false
    notifySuccess('机器制造费用率修订已保存')
  } catch (error) {
    notifyOperationFailure(
      '保存机器制造费用率失败',
      rates.addRevisionError.value ?? error,
      '保存机器制造费用率失败，请稍后重试。',
    )
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="机器制造费用率" :breadcrumbs="[{ label: '经营管理' }, { label: '财务' }]">
      <template v-if="canManage" #actions>
        <NvButton size="sm" type="button" :disabled="!rates.rates.value" @click="openSheet"
          ><PlusIcon aria-hidden="true" />新增修订</NvButton
        >
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvEntityPicker
          v-model="rates.workCenterId.value"
          class="w-80"
          :options="workCenterOptions"
          title="选择工作中心"
          placeholder="选择工作中心"
          empty-text="暂无工作中心，请先在「基础数据 · 工作中心」维护"
          :loading="workCentersPending"
          aria-label="工作中心"
        />
        <NvInput
          v-model="periodDraft"
          class="w-40"
          aria-label="会计期间"
          placeholder="会计期间，如 2026-09"
          @change="commitPeriod"
        />
      </template>
    </NvToolbar>

    <NvMetricStrip
      v-if="rates.workCenterId.value && rates.accountingPeriodCode.value"
      :cells="currentCells"
    />

    <NvDataTable
      :columns="columns"
      :rows="items"
      :row-key="(row: RateRow) => row.workCenterMachineOverheadRateId"
      :loading="rates.pending.value"
      :error="Boolean(rates.error.value)"
      error-message="费率读取失败，请重试。"
      :searchable="false"
      :column-settings="false"
      :awaiting-scope="
        !rates.ready.value || !rates.workCenterId.value || !rates.accountingPeriodCode.value
      "
      awaiting-scope-message="请选择工作中心并填写会计期间。"
      @retry="rates.refresh"
    >
      <template #empty>
        <p class="text-sm font-medium">该工作中心在本期还没有机器制造费用率</p>
        <p class="max-w-md text-sm text-muted-foreground">
          {{
            canManage
              ? '录入前，本期带设备的工序完工无法结算机器费用；不产生机器费用的工作中心也要录入一条“不适用”。'
              : '录入前，本期带设备的工序完工无法结算机器费用，请联系财务维护人员录入。'
          }}
        </p>
        <NvButton v-if="canManage" size="sm" type="button" @click="openSheet"
          ><PlusIcon aria-hidden="true" />新增修订</NvButton
        >
      </template>
      <template #cell-rate="{ row }">
        <div v-if="applicable(row)" class="space-y-1 tabular-nums">
          <p>固定 {{ machineAmount(row.fixedHourlyRate, row.currencyCode) }}</p>
          <p>变动 {{ machineAmount(row.variableHourlyRate, row.currencyCode) }}</p>
          <p class="font-medium">合计 {{ machineAmount(row.totalHourlyRate, row.currencyCode) }}</p>
        </div>
        <p v-else>不适用</p>
      </template>
      <template #cell-basis="{ row }">
        <div v-if="applicable(row)" class="space-y-1 tabular-nums">
          <p>固定 {{ machineAmount(row.fixedOverheadBudget, row.currencyCode) }}</p>
          <p>变动 {{ machineAmount(row.variableOverheadBudget, row.currencyCode) }}</p>
          <p class="text-xs text-muted-foreground">
            正常产能 {{ machineHours(row.normalCapacityMachineHours) }}
          </p>
        </div>
        <p v-else>—</p>
      </template>
      <template #cell-status="{ row }">
        <NvStatusBadge
          v-bind="
            row.revision === rates.rates.value?.currentRevision
              ? { value: 'active', label: '当前生效' }
              : { value: 'superseded', label: '已被新修订取代' }
          "
        />
      </template>
    </NvDataTable>

    <NvSheet v-if="canManage" v-model:open="open">
      <NvSheetContent class="w-full overflow-y-auto sm:max-w-xl">
        <NvSheetHeader>
          <NvSheetTitle>新增机器制造费用率修订</NvSheetTitle>
          <NvSheetDescription>
            会计期间 {{ rates.accountingPeriodCode.value }}。保存后作为本期当前费率，历史修订保留。
          </NvSheetDescription>
        </NvSheetHeader>
        <form class="grid content-start gap-4 p-4" @submit.prevent="submit">
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            {{
              isApplicable
                ? '请填写至少一项正数预算、正数正常产能机器小时、三位字母币种和修订原因。'
                : '请填写三位字母币种和修订原因。'
            }}
          </p>
          <NvFieldGroup>
            <NvField>
              <NvFieldLabel>适用性 <span class="text-destructive">*</span></NvFieldLabel>
              <NvRadioGroup
                v-model="form.applicability"
                class="grid grid-cols-2 gap-2.5"
                aria-label="适用性"
              >
                <NvRadioGroupItem value="applicable">适用</NvRadioGroupItem>
                <NvRadioGroupItem value="notApplicable">不适用</NvRadioGroupItem>
              </NvRadioGroup>
              <NvFieldDescription v-if="!isApplicable">
                该工作中心本期不产生机器制造费用，结算时不计机器费用。
              </NvFieldDescription>
            </NvField>
            <template v-if="isApplicable">
              <NvField>
                <NvFieldLabel for="erp-mor-fixed">固定费用预算</NvFieldLabel>
                <NvInput
                  id="erp-mor-fixed"
                  v-model="form.fixedOverheadBudget"
                  type="number"
                  min="0"
                  step="0.01"
                  :invalid="showErrors && invalid.budget"
                />
              </NvField>
              <NvField>
                <NvFieldLabel for="erp-mor-variable">变动费用预算</NvFieldLabel>
                <NvInput
                  id="erp-mor-variable"
                  v-model="form.variableOverheadBudget"
                  type="number"
                  min="0"
                  step="0.01"
                  :invalid="showErrors && invalid.budget"
                />
                <NvFieldDescription>固定、变动至少填写一项正数。</NvFieldDescription>
              </NvField>
              <NvField>
                <NvFieldLabel for="erp-mor-capacity">
                  正常产能机器小时 <span class="text-destructive">*</span>
                </NvFieldLabel>
                <NvInput
                  id="erp-mor-capacity"
                  v-model="form.normalCapacityMachineHours"
                  type="number"
                  min="0"
                  step="0.01"
                  :invalid="showErrors && invalid.capacity"
                />
                <NvFieldDescription>每机器小时费率 = 预算 ÷ 正常产能机器小时。</NvFieldDescription>
              </NvField>
            </template>
            <NvField>
              <NvFieldLabel for="erp-mor-currency">
                币种 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvSearchSelect
                id="erp-mor-currency"
                v-model="form.currencyCode"
                :options="currencyOptionsIncluding(form.currencyCode)"
                search-placeholder="搜索币种代码或名称"
                aria-label="币种"
                :disabled="Boolean(fixedCurrency)"
              />
              <NvFieldDescription v-if="fixedCurrency">
                该工作中心的币种已固定为 {{ fixedCurrency }}。
              </NvFieldDescription>
            </NvField>
            <NvField>
              <NvFieldLabel for="erp-mor-reason">
                修订原因 <span class="text-destructive">*</span>
              </NvFieldLabel>
              <NvInput
                id="erp-mor-reason"
                v-model="form.reason"
                maxlength="500"
                :invalid="showErrors && invalid.reason"
              />
            </NvField>
          </NvFieldGroup>
          <NvSheetFooter>
            <NvButton type="button" variant="outline" @click="open = false">取消</NvButton>
            <NvButton type="submit" :disabled="rates.addRevisionPending.value">
              <Spinner v-if="rates.addRevisionPending.value" aria-hidden="true" />保存修订
            </NvButton>
          </NvSheetFooter>
        </form>
      </NvSheetContent>
    </NvSheet>
  </BusinessLayout>
</template>
