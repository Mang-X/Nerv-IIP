<script setup lang="ts">
import type { BusinessConsoleTelemetryAlarmRuleItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  useBusinessTelemetryAlarmRules,
  type SaveTelemetryAlarmRuleInput,
} from '@/composables/useBusinessTelemetry'
import { usePagedList } from '@/composables/usePagedList'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvBadge,
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogClose,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDropdownMenuItem,
  NvField,
  NvFieldError,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvRowActions,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvToolbar,
  toast,
} from '@nerv-iip/ui'
import { EditIcon, LineChartIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef } from 'vue'
import { RouterLink } from 'vue-router'

definePage({
  meta: {
    requiresAuth: true,
    title: '报警规则',
    requiredPermissions: ['business.iiot.alarms.read', 'business.iiot.alarm-rules.manage'],
  },
})

const {
  alarmRules,
  alarmRulesError,
  alarmRulesPending,
  alarmRulesTotal,
  filters,
  refreshAlarmRules,
  saveAlarmRule,
  saveAlarmRuleError,
  saveAlarmRulePending,
} = useBusinessTelemetryAlarmRules()
const { page, pageSize } = usePagedList(filters)

const formOpen = shallowRef(false)
const formError = shallowRef('')
const form = reactive<SaveTelemetryAlarmRuleInput>({
  deviceAssetId: '',
  ruleCode: '',
  alarmCode: '',
  severity: 'warning',
  tagKey: '',
  comparisonOperator: '>',
  thresholdValue: undefined,
  unitCode: '',
  isEnabled: true,
})

const errorMessage = computed(() => formatError(alarmRulesError.value))
const formErrorMessage = computed(() => formError.value || formatError(saveAlarmRuleError.value))
const formEnabledValue = computed({
  get: () => (form.isEnabled ? 'enabled' : 'disabled'),
  set: (value: string) => {
    form.isEnabled = value === 'enabled'
  },
})

const columns: NvDataTableColumn<BusinessConsoleTelemetryAlarmRuleItem>[] = [
  {
    key: 'ruleCode',
    header: '规则',
    cellClass: 'font-medium',
    accessor: (r) => r.ruleCode ?? '无规则',
  },
  { key: 'deviceAssetId', header: '设备', accessor: (r) => r.deviceAssetId ?? '无设备' },
  { key: 'tagKey', header: '采集标签', accessor: (r) => r.tagKey ?? '无标签' },
  { key: 'condition', header: '触发条件', accessor: (r) => conditionLabel(r) },
  { key: 'severity', header: '级别', width: 'w-24' },
  { key: 'isEnabled', header: '状态', width: 'w-24' },
  { key: 'updatedAtUtc', header: '更新时间', width: 'w-44' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-12' },
]

const severityOptions = [
  { label: '信息', value: 'info' },
  { label: '预警', value: 'warning' },
  { label: '阻塞', value: 'blocked' },
  { label: '严重', value: 'critical' },
]
const operatorOptions = [
  { label: '大于', value: '>' },
  { label: '大于等于', value: '>=' },
  { label: '小于', value: '<' },
  { label: '小于等于', value: '<=' },
  { label: '等于', value: '==' },
  { label: '不等于', value: '!=' },
]

function openCreate() {
  Object.assign(form, {
    deviceAssetId: '',
    ruleCode: '',
    alarmCode: '',
    severity: 'warning',
    tagKey: '',
    comparisonOperator: '>',
    thresholdValue: undefined,
    unitCode: '',
    isEnabled: true,
  })
  formError.value = ''
  formOpen.value = true
}
function openEdit(row: BusinessConsoleTelemetryAlarmRuleItem) {
  Object.assign(form, {
    deviceAssetId: row.deviceAssetId ?? '',
    ruleCode: row.ruleCode ?? '',
    alarmCode: row.alarmCode ?? '',
    severity: row.severity ?? 'warning',
    tagKey: row.tagKey ?? '',
    comparisonOperator: row.comparisonOperator ?? '>',
    thresholdValue: row.thresholdValue,
    unitCode: row.unitCode ?? '',
    isEnabled: row.isEnabled ?? true,
  })
  formError.value = ''
  formOpen.value = true
}
async function submitRule() {
  const thresholdValue = parseThresholdValue(form.thresholdValue)
  if (
    !form.deviceAssetId.trim() ||
    !form.ruleCode.trim() ||
    !form.alarmCode.trim() ||
    !form.tagKey.trim() ||
    thresholdValue === undefined ||
    !form.unitCode.trim()
  ) {
    formError.value = '请填写设备、规则、报警、采集标签、阈值和单位。'
    return
  }

  try {
    await saveAlarmRule({
      ...form,
      deviceAssetId: form.deviceAssetId.trim(),
      ruleCode: form.ruleCode.trim(),
      alarmCode: form.alarmCode.trim(),
      tagKey: form.tagKey.trim(),
      unitCode: form.unitCode.trim(),
      thresholdValue,
    })
    formOpen.value = false
    toast.success('报警规则已保存')
  } catch {
    // 错误由表单错误区域展示。
  }
}
function severityLabel(value?: string | null) {
  return severityOptions.find((o) => o.value === value)?.label ?? value ?? '未知'
}
function severityVariant(value?: string | null) {
  const severity = value?.toLowerCase()
  if (severity === 'critical' || severity === 'blocked') return 'danger'
  if (severity === 'warning') return 'warning'
  return 'neutral'
}
function conditionLabel(row: BusinessConsoleTelemetryAlarmRuleItem) {
  return `${row.comparisonOperator ?? '?'} ${row.thresholdValue ?? '无阈值'} ${row.unitCode ?? ''}`.trim()
}
function parseThresholdValue(value: SaveTelemetryAlarmRuleInput['thresholdValue'] | string) {
  if (value === null || value === undefined) return undefined
  const raw = typeof value === 'string' ? value.trim() : value
  if (raw === '') return undefined
  const numericValue = Number(raw)
  return Number.isFinite(numericValue) ? numericValue : undefined
}
function formatDateTime(value?: string | null) {
  if (!value) return '无'
  const date = new Date(value)
  return Number.isNaN(date.getTime()) ? value : date.toLocaleString()
}
function rowKey(row: BusinessConsoleTelemetryAlarmRuleItem) {
  return row.alarmRuleId ?? `${row.deviceAssetId}-${row.ruleCode}`
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="报警规则"
      :breadcrumbs="[{ label: '设备监控（IoT）' }]"
      :count="`${alarmRulesTotal} 条规则`"
    >
      <template #actions>
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="alarmRulesPending"
          @click="refreshAlarmRules"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreate">
          <PlusIcon aria-hidden="true" />
          新建报警规则
        </NvButton>
      </template>
    </NvPageHeader>

    <NvToolbar :show-search="false">
      <template #filters>
        <NvInput
          v-model="filters.deviceAssetId"
          class="h-9 w-64"
          placeholder="按设备编号筛选"
          aria-label="设备编号"
        />
        <NvSelect v-model="filters.isEnabled">
          <NvSelectTrigger class="h-9 w-36" aria-label="规则状态"
            ><NvSelectValue
          /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem value="all">全部状态</NvSelectItem>
            <NvSelectItem value="enabled">启用</NvSelectItem>
            <NvSelectItem value="disabled">停用</NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </template>
    </NvToolbar>

    <p v-if="errorMessage" class="text-sm text-destructive" role="alert">{{ errorMessage }}</p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="alarmRulesTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="alarmRules"
      :row-key="rowKey"
      :loading="alarmRulesPending"
      :searchable="false"
      :column-settings="false"
      empty-message="暂无报警规则。为关键设备的采集标签配置阈值后，系统会按真实采样事实触发报警。"
    >
      <template #cell-deviceAssetId="{ row }">
        <RouterLink
          :to="`/equipment/${row.deviceAssetId}`"
          class="text-brand underline-offset-4 hover:underline"
        >
          {{ row.deviceAssetId ?? '无设备' }}
        </RouterLink>
      </template>
      <template #cell-severity="{ row }">
        <NvBadge class="rounded-sm" :variant="severityVariant(row.severity)">{{
          severityLabel(row.severity)
        }}</NvBadge>
      </template>
      <template #cell-isEnabled="{ row }">
        <NvBadge class="rounded-sm" :variant="row.isEnabled ? 'success' : 'neutral'">{{
          row.isEnabled ? '启用' : '停用'
        }}</NvBadge>
      </template>
      <template #cell-updatedAtUtc="{ row }">{{ formatDateTime(row.updatedAtUtc) }}</template>
      <template #cell-actions="{ row }">
        <NvRowActions :label="`报警规则操作 ${row.ruleCode ?? ''}`">
          <NvDropdownMenuItem @click="openEdit(row)">
            <EditIcon aria-hidden="true" />
            维护规则
          </NvDropdownMenuItem>
          <NvDropdownMenuItem as-child>
            <RouterLink
              :to="{
                path: '/equipment/telemetry/history',
                query: { deviceAssetId: row.deviceAssetId, tagKey: row.tagKey },
              }"
            >
              <LineChartIcon aria-hidden="true" />
              查看趋势
            </RouterLink>
          </NvDropdownMenuItem>
        </NvRowActions>
      </template>
    </NvDataTable>

    <NvDialog v-model:open="formOpen">
      <NvDialogContent class="max-h-[85vh] overflow-y-auto sm:max-w-2xl">
        <NvDialogHeader>
          <NvDialogTitle>维护报警规则</NvDialogTitle>
          <NvDialogDescription
            >规则保存后由 IndustrialTelemetry
            按真实采样事实评估，不在前端模拟报警。</NvDialogDescription
          >
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRule">
          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField>
              <NvFieldLabel for="rule-device">设备</NvFieldLabel>
              <NvInput
                id="rule-device"
                v-model="form.deviceAssetId"
                autocomplete="off"
                placeholder="如 DEV-CNC-01"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-tag">采集标签</NvFieldLabel>
              <NvInput
                id="rule-tag"
                v-model="form.tagKey"
                autocomplete="off"
                placeholder="如 temperature"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-code">规则编号</NvFieldLabel>
              <NvInput
                id="rule-code"
                v-model="form.ruleCode"
                autocomplete="off"
                placeholder="如 TEMP_HIGH"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-alarm">报警编号</NvFieldLabel>
              <NvInput
                id="rule-alarm"
                v-model="form.alarmCode"
                autocomplete="off"
                placeholder="如 ALM-TEMP-HIGH"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-severity">级别</NvFieldLabel>
              <NvSelect v-model="form.severity">
                <NvSelectTrigger id="rule-severity" aria-label="报警级别"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in severityOptions"
                    :key="option.value"
                    :value="option.value"
                    >{{ option.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-operator">比较方式</NvFieldLabel>
              <NvSelect v-model="form.comparisonOperator">
                <NvSelectTrigger id="rule-operator" aria-label="比较方式"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem
                    v-for="option in operatorOptions"
                    :key="option.value"
                    :value="option.value"
                    >{{ option.label }}</NvSelectItem
                  >
                </NvSelectContent>
              </NvSelect>
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-threshold">阈值</NvFieldLabel>
              <NvInput
                id="rule-threshold"
                v-model="form.thresholdValue"
                type="number"
                step="0.001"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-unit">单位</NvFieldLabel>
              <NvInput
                id="rule-unit"
                v-model="form.unitCode"
                autocomplete="off"
                placeholder="如 CEL"
              />
            </NvField>
            <NvField>
              <NvFieldLabel for="rule-enabled">规则状态</NvFieldLabel>
              <NvSelect v-model="formEnabledValue">
                <NvSelectTrigger id="rule-enabled" aria-label="规则状态"
                  ><NvSelectValue
                /></NvSelectTrigger>
                <NvSelectContent>
                  <NvSelectItem value="enabled">启用</NvSelectItem>
                  <NvSelectItem value="disabled">停用</NvSelectItem>
                </NvSelectContent>
              </NvSelect>
            </NvField>
          </NvFieldGroup>

          <NvFieldError v-if="formErrorMessage" :errors="[formErrorMessage]" />

          <NvDialogFooter>
            <NvDialogClose as-child>
              <NvButton type="button" variant="outline">取消</NvButton>
            </NvDialogClose>
            <NvButton type="submit" :disabled="saveAlarmRulePending">
              <Spinner v-if="saveAlarmRulePending" aria-hidden="true" />
              保存规则
            </NvButton>
          </NvDialogFooter>
        </form>
      </NvDialogContent>
    </NvDialog>
  </BusinessLayout>
</template>
