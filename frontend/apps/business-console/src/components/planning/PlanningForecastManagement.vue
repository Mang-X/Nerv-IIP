<script setup lang="ts">
import type { BusinessConsoleForecastInputItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { CalendarRangeIcon, PencilIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import {
  useBusinessMasterDataResources,
  useBusinessSkus,
} from '@/composables/useBusinessMasterData'
import { useBusinessForecasts, type ForecastForm } from '@/composables/useBusinessForecasts'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvDataTable,
  NvDialog,
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
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvToolbar,
  Spinner,
} from '@nerv-iip/ui'

const {
  filters,
  forecasts,
  forecastsError,
  forecastsPending,
  refreshForecasts,
  saveForecast,
  saveForecastPending,
} = useBusinessForecasts()
const auth = useAuthStore()
const canManage = computed(() =>
  (auth.principal?.permissionCodes ?? []).includes(P.planningDemandsManage),
)

const { skus } = useBusinessSkus()
const { resources: sites } = useBusinessMasterDataResources('site')
const { resources: units } = useBusinessMasterDataResources('unit-of-measure')
const skuOptions = computed(() =>
  skus.value
    .filter((item) => item.code)
    .map((item) => ({
      value: item.code as string,
      label: `${item.displayName ?? item.code} · ${item.code}`,
    })),
)
const siteOptions = computed(() =>
  sites.value
    .filter((item) => item.code)
    .map((item) => ({
      value: item.code as string,
      label: `${item.displayName ?? item.code} · ${item.code}`,
    })),
)
const uomOptions = computed(() =>
  units.value
    .filter((item) => item.code)
    .map((item) => ({
      value: item.code as string,
      label: `${item.displayName ?? item.code} · ${item.code}`,
    })),
)
const skuNameByCode = computed(
  () => new Map(skus.value.map((item) => [item.code ?? '', item.displayName ?? item.code ?? ''])),
)
const siteNameByCode = computed(
  () => new Map(sites.value.map((item) => [item.code ?? '', item.displayName ?? item.code ?? ''])),
)

const keyword = shallowRef('')
const visibleForecasts = computed(() => {
  const value = keyword.value.trim().toLowerCase()
  if (!value) return forecasts.value
  return forecasts.value.filter((forecast) =>
    [forecast.forecastReference, forecast.skuCode, forecast.siteCode].some((candidate) =>
      candidate?.toLowerCase().includes(value),
    ),
  )
})

const columns: NvDataTableColumn<BusinessConsoleForecastInputItem>[] = [
  { key: 'forecastReference', header: '预测编号', cellClass: 'font-medium' },
  { key: 'skuCode', header: 'SKU' },
  { key: 'siteCode', header: '工厂' },
  { key: 'period', header: '预测期间', width: 'w-56' },
  { key: 'quantity', header: '预测数量', align: 'end', width: 'w-32' },
  { key: 'consumptionWindow', header: '订单冲减窗口', width: 'w-40' },
  { key: 'actions', header: '', align: 'end', width: 'w-24' },
]

function defaultForm(): ForecastForm {
  const periodStartDate = new Date().toISOString().slice(0, 10)
  const periodEndDate = new Date(Date.now() + 30 * 24 * 60 * 60 * 1000).toISOString().slice(0, 10)
  return {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    forecastReference: '',
    skuCode: '',
    uomCode: '',
    siteCode: '',
    periodStartDate,
    periodEndDate,
    quantity: 0,
    backwardConsumptionDays: 0,
    forwardConsumptionDays: 0,
  }
}

const dialogOpen = shallowRef(false)
const editMode = shallowRef(false)
const submitted = shallowRef(false)
const form = reactive<ForecastForm>(defaultForm())

const validationErrors = computed(() => {
  const errors: string[] = []
  if (!form.forecastReference.trim()) errors.push('请填写预测编号。')
  if (!form.skuCode.trim()) errors.push('请选择 SKU。')
  if (!form.siteCode.trim()) errors.push('请选择工厂。')
  if (!form.uomCode.trim()) errors.push('请选择单位。')
  if (!form.periodStartDate || !form.periodEndDate) errors.push('请选择完整预测期间。')
  if (form.periodStartDate && form.periodEndDate && form.periodEndDate < form.periodStartDate) {
    errors.push('预测结束日期不能早于开始日期。')
  }
  if ((form.quantity ?? 0) <= 0) errors.push('预测数量必须大于 0。')
  if (!Number.isInteger(form.backwardConsumptionDays) || (form.backwardConsumptionDays ?? -1) < 0) {
    errors.push('向前冲减天数必须是大于等于 0 的整数。')
  }
  if (!Number.isInteger(form.forwardConsumptionDays) || (form.forwardConsumptionDays ?? -1) < 0) {
    errors.push('向后冲减天数必须是大于等于 0 的整数。')
  }
  return errors
})

function replaceForm(next: ForecastForm) {
  Object.assign(form, next)
  submitted.value = false
}

function openCreate() {
  editMode.value = false
  replaceForm(defaultForm())
  dialogOpen.value = true
}

function openEdit(row: BusinessConsoleForecastInputItem) {
  if (!row.forecastReference) return
  editMode.value = true
  replaceForm({
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    forecastReference: row.forecastReference,
    skuCode: row.skuCode ?? '',
    uomCode: row.uomCode ?? '',
    siteCode: row.siteCode ?? '',
    periodStartDate: row.periodStartDate ?? '',
    periodEndDate: row.periodEndDate ?? '',
    quantity: row.quantity ?? 0,
    backwardConsumptionDays: row.backwardConsumptionDays ?? 0,
    forwardConsumptionDays: row.forwardConsumptionDays ?? 0,
  })
  dialogOpen.value = true
}

async function submitForecast() {
  submitted.value = true
  if (validationErrors.value.length > 0) return
  try {
    await saveForecast({
      ...form,
      forecastReference: form.forecastReference.trim(),
      skuCode: form.skuCode.trim(),
      uomCode: form.uomCode.trim(),
      siteCode: form.siteCode.trim(),
    })
    dialogOpen.value = false
    notifySuccess(editMode.value ? '预测已更新。' : '预测已创建。')
  } catch (error) {
    notifyOperationFailure('保存预测失败', error, '保存预测失败，请稍后重试。')
  }
}

function formatDate(value?: string | null) {
  return value ? value.slice(0, 10) : '—'
}

function formatQuantity(row: BusinessConsoleForecastInputItem) {
  return `${row.quantity ?? 0} ${row.uomCode ?? ''}`.trim()
}
</script>

<template>
  <div class="grid gap-3">
    <NvToolbar
      v-model:search="keyword"
      search-placeholder="搜预测编号 / 物料 / 工厂"
      search-label="预测关键字"
    >
      <template #filters>
        <NvSelect v-model="filters.skuCode">
          <NvSelectTrigger class="h-9 w-48" aria-label="预测 SKU 筛选">
            <NvSelectValue placeholder="全部 SKU" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部 SKU</NvSelectItem>
            <NvSelectItem v-for="option in skuOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
        <NvSelect v-model="filters.siteCode">
          <NvSelectTrigger class="h-9 w-48" aria-label="预测工厂筛选">
            <NvSelectValue placeholder="全部工厂" />
          </NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部工厂</NvSelectItem>
            <NvSelectItem v-for="option in siteOptions" :key="option.value" :value="option.value">
              {{ option.label }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
      <template #actions>
        <NvButton
          type="button"
          size="sm"
          variant="outline"
          :disabled="forecastsPending"
          @click="refreshForecasts"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新预测
        </NvButton>
        <NvButton v-if="canManage" type="button" size="sm" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建预测
        </NvButton>
      </template>
    </NvToolbar>

    <p v-if="forecastsError" class="text-sm text-destructive" role="alert">
      {{ inlineErrorMessage(forecastsError) }}
    </p>

    <NvDataTable
      :columns="columns"
      :rows="visibleForecasts"
      row-key="forecastInputId"
      :loading="forecastsPending"
      :searchable="false"
      :column-settings="false"
      empty-message="当前范围没有预测。计划员可新建预测，保存后会进入后续 MRP 输入准备。"
    >
      <template #cell-skuCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ skuNameByCode.get(row.skuCode ?? '') || row.skuCode || '—' }}</span>
          <span v-if="row.skuCode" class="text-xs text-muted-foreground">{{ row.skuCode }}</span>
        </div>
      </template>
      <template #cell-siteCode="{ row }">
        <div class="flex flex-col gap-0.5">
          <span>{{ siteNameByCode.get(row.siteCode ?? '') || row.siteCode || '—' }}</span>
          <span v-if="row.siteCode" class="text-xs text-muted-foreground">{{ row.siteCode }}</span>
        </div>
      </template>
      <template #cell-period="{ row }">
        <span class="inline-flex items-center gap-1.5">
          <CalendarRangeIcon aria-hidden="true" class="size-4 text-muted-foreground" />
          {{ formatDate(row.periodStartDate) }} ~ {{ formatDate(row.periodEndDate) }}
        </span>
      </template>
      <template #cell-quantity="{ row }">
        <span class="tabular-nums">{{ formatQuantity(row) }}</span>
      </template>
      <template #cell-consumptionWindow="{ row }">
        前 {{ row.backwardConsumptionDays ?? 0 }} 天 / 后 {{ row.forwardConsumptionDays ?? 0 }} 天
      </template>
      <template #cell-actions="{ row }">
        <NvButton
          v-if="canManage"
          type="button"
          size="sm"
          variant="ghost"
          :aria-label="`编辑预测 ${row.forecastReference}`"
          @click="openEdit(row)"
        >
          <PencilIcon aria-hidden="true" />
          编辑
        </NvButton>
      </template>
    </NvDataTable>

    <NvDialog v-if="canManage" v-model:open="dialogOpen">
      <NvDialogContent class="sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>{{ editMode ? '编辑预测' : '新建预测' }}</NvDialogTitle>
          <NvDialogDescription>
            销售订单会在设置的前后窗口内冲减预测，只有剩余预测进入 MRP。
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" novalidate @submit.prevent="submitForecast">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="forecast-reference">预测编号</NvFieldLabel>
              <NvInput
                id="forecast-reference"
                v-model="form.forecastReference"
                :disabled="editMode"
                maxlength="128"
                placeholder="FC-2026-09-SKU-FG-1000"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-sku">SKU</NvFieldLabel>
              <NvSelect v-model="form.skuCode">
                <NvSelectTrigger id="forecast-sku"
                  ><NvSelectValue placeholder="选择 SKU"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in skuOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-site">工厂</NvFieldLabel>
              <NvSelect v-model="form.siteCode">
                <NvSelectTrigger id="forecast-site"
                  ><NvSelectValue placeholder="选择工厂"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in siteOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-uom">单位</NvFieldLabel>
              <NvSelect v-model="form.uomCode">
                <NvSelectTrigger id="forecast-uom"
                  ><NvSelectValue placeholder="选择单位"
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in uomOptions"
                    :key="option.value"
                    :value="option.value"
                  >
                    {{ option.label }}
                  </NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-start">开始日期</NvFieldLabel>
              <NvInput id="forecast-start" v-model="form.periodStartDate" type="date" />
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-end">结束日期</NvFieldLabel>
              <NvInput id="forecast-end" v-model="form.periodEndDate" type="date" />
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-quantity">预测数量</NvFieldLabel>
              <NvInput
                id="forecast-quantity"
                v-model.number="form.quantity"
                min="0.0001"
                step="0.0001"
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-backward-days">向前冲减天数</NvFieldLabel>
              <NvInput
                id="forecast-backward-days"
                v-model.number="form.backwardConsumptionDays"
                min="0"
                step="1"
                type="number"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="forecast-forward-days">向后冲减天数</NvFieldLabel>
              <NvInput
                id="forecast-forward-days"
                v-model.number="form.forwardConsumptionDays"
                min="0"
                step="1"
                type="number"
              />
            </NvField>
          </NvFieldGroup>
          <NvFieldError v-if="submitted && validationErrors.length" :errors="validationErrors" />
          <NvDialogFooter>
            <NvButton type="button" variant="outline" @click="dialogOpen = false">取消</NvButton>
            <NvButton type="submit" :disabled="saveForecastPending">
              <Spinner v-if="saveForecastPending" aria-hidden="true" />
              {{ editMode ? '保存修改' : '创建预测' }}
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </div>
</template>
