<script setup lang="ts">
import type { BusinessConsoleTelemetryAlarmRuleItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import {
  useBusinessTelemetryAlarmRules,
  type SaveTelemetryAlarmRuleInput,
} from '@/composables/useBusinessTelemetry'
import {
  useEquipmentDeviceCatalog,
  useEquipmentUomCatalog,
  useTelemetryTagCatalog,
} from '@/composables/useEquipmentPickerCatalog'
import { useMasterDataDisplayNames } from '@/composables/useMasterDataDisplayNames'
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
  NvEntityPicker,
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
} from '@nerv-iip/ui'
import { EditIcon, LineChartIcon, PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, shallowRef, watch } from 'vue'
import { RouterLink } from 'vue-router'
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import { notifyError, notifySuccess } from '@/utils/notify'

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
  saveAlarmRulePending,
} = useBusinessTelemetryAlarmRules()
const { page, pageSize } = usePagedList(filters, { resetOn: [() => filters.deviceAssetId] })
const { deviceOptions, devicesPending } = useEquipmentDeviceCatalog()
const { uomOptions, uomsPending } = useEquipmentUomCatalog()

const formOpen = shallowRef(false)
const formEditing = shallowRef(false)
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

// 弹窗里的采集标签跟随所选设备：换设备就只列那台设备的测点，旧测点同时清空。
const { tagOptions, tagsPending, unitByTagKey } = useTelemetryTagCatalog(() => form.deviceAssetId)
watch(
  () => form.deviceAssetId,
  () => {
    if (!formEditing.value) form.tagKey = ''
  },
)
// 选完测点自动带出它标注的单位，省掉一次手输；已填单位不覆盖。
watch(
  () => form.tagKey,
  (tagKey) => {
    const unitCode = unitByTagKey.value.get(tagKey.trim())
    if (unitCode && !form.unitCode?.trim()) form.unitCode = unitCode
  },
)

// 测点标注的单位不一定登记在计量单位主数据里；已生效的值照样显示出来，不让选择器看着是空的。
const unitPickerOptions = computed(() => {
  const unitCode = form.unitCode?.trim() ?? ''
  if (!unitCode || uomOptions.value.some((option) => option.value === unitCode)) {
    return uomOptions.value
  }
  return [...uomOptions.value, { value: unitCode, label: unitCode, hint: '来自采集标签标注' }]
})

const errorMessage = computed(() => formatError(alarmRulesError.value))
// 服务端错误走 toast；这里只留点提交后的字段级校验汇总。
const formErrorMessage = computed(() => formError.value)
// 编辑态：规则身份（设备 / 采集标签 / 规则编号 / 报警编号）由所选行带出，只读呈现。
const ruleContextItems = computed(() => [
  { label: '设备', value: form.deviceAssetId },
  { label: '采集标签', value: form.tagKey },
  { label: '规则编号', value: form.ruleCode },
  { label: '报警编号', value: form.alarmCode },
])
const formEnabledValue = computed({
  get: () => (form.isEnabled ? 'enabled' : 'disabled'),
  set: (value: string) => {
    form.isEnabled = value === 'enabled'
  },
})

// 规则读面只回设备编号（DEV-CNC-01），设备名在主数据里，按编号 join 出中文名。
const { resolveDevice } = useMasterDataDisplayNames({ devices: true })
/** 设备展示串：名称优先，名录查不到就只显编号，不编名字。 */
function deviceLabel(code?: string | null, fallback = '无设备') {
  if (!code) return fallback
  return resolveDevice(code) ?? code
}

const columns: NvDataTableColumn<BusinessConsoleTelemetryAlarmRuleItem>[] = [
  {
    key: 'ruleCode',
    header: '规则',
    cellClass: 'font-medium',
    accessor: (r) => r.ruleCode ?? '无规则',
  },
  {
    key: 'deviceAssetId',
    header: '设备',
    accessor: (r) =>
      resolveDevice(r.deviceAssetId)
        ? `${resolveDevice(r.deviceAssetId)} ${r.deviceAssetId}`
        : (r.deviceAssetId ?? '无设备'),
  },
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
  formEditing.value = false
  formError.value = ''
  formOpen.value = true
}
function openEdit(row: BusinessConsoleTelemetryAlarmRuleItem) {
  formEditing.value = true
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
    notifySuccess('报警规则已保存')
  } catch (error) {
    notifyError(error, '报警规则保存失败，请稍后重试。')
  }
}
function severityLabel(value?: string | null) {
  // 选项里没有的级别就说「未知级别」，绝不把后端英文码回吐到界面上。
  return value ? (severityOptions.find((o) => o.value === value)?.label ?? '未知级别') : '未知'
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
        <NvEntityPicker
          v-model="filters.deviceAssetId"
          class="w-64"
          :options="deviceOptions"
          title="选择设备"
          placeholder="全部设备"
          source-text="数据来自基础数据设备资产"
          empty-text="暂无设备资产，请先在基础数据登记设备"
          :loading="devicesPending"
          clearable
          aria-label="设备"
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
          class="grid leading-tight text-brand underline-offset-4 hover:underline"
        >
          <span>{{ deviceLabel(row.deviceAssetId) }}</span>
          <span v-if="resolveDevice(row.deviceAssetId)" class="text-xs text-muted-foreground">{{
            row.deviceAssetId
          }}</span>
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
          <NvDialogTitle>{{ formEditing ? '编辑报警规则' : '新建报警规则' }}</NvDialogTitle>
          <NvDialogDescription class="sr-only">
            {{ formEditing ? `编辑报警规则 ${form.ruleCode}。` : '为设备采集标签配置报警阈值。' }}
          </NvDialogDescription>
        </NvDialogHeader>
        <form class="grid gap-4" @submit.prevent="submitRule">
          <!-- 编辑态：规则身份由所选行带出，只读呈现，不做成看起来还能改的输入位。 -->
          <CarriedContextSummary v-if="formEditing" label="报警规则" :items="ruleContextItems" />

          <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
            <NvField v-if="!formEditing">
              <NvFieldLabel for="rule-device">设备</NvFieldLabel>
              <NvEntityPicker
                id="rule-device"
                v-model="form.deviceAssetId"
                :options="deviceOptions"
                title="选择设备"
                placeholder="选择设备"
                source-text="数据来自基础数据设备资产"
                empty-text="暂无设备资产，请先在基础数据登记设备"
                :loading="devicesPending"
                aria-label="设备"
              />
            </NvField>
            <NvField v-if="!formEditing">
              <NvFieldLabel for="rule-tag">采集标签</NvFieldLabel>
              <NvEntityPicker
                id="rule-tag"
                v-model="form.tagKey"
                :options="tagOptions"
                title="选择采集标签"
                :placeholder="form.deviceAssetId ? '选择采集标签' : '请先选择设备'"
                :disabled="!form.deviceAssetId"
                source-text="数据来自该设备已配置的采集标签"
                empty-text="该设备还没有配置采集标签，请先完成采集映射"
                :loading="tagsPending"
                aria-label="采集标签"
              />
            </NvField>
            <NvField v-if="!formEditing">
              <NvFieldLabel for="rule-code">规则编号</NvFieldLabel>
              <NvInput
                id="rule-code"
                v-model="form.ruleCode"
                autocomplete="off"
                placeholder="如 TEMP_HIGH"
              />
            </NvField>
            <NvField v-if="!formEditing">
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
              <NvEntityPicker
                id="rule-unit"
                v-model="form.unitCode"
                :options="unitPickerOptions"
                title="选择单位"
                placeholder="选择单位"
                source-text="数据来自基础数据计量单位"
                empty-text="暂无计量单位，请先在基础数据维护单位"
                :loading="uomsPending"
                aria-label="单位"
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
