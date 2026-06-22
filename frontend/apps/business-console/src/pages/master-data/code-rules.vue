<script setup lang="ts">
import type {
  BusinessConsoleCodeRuleItem,
  BusinessConsoleCodeRuleSegment,
  BusinessConsoleCreateCodeRuleVersionRequest,
} from '@nerv-iip/api-client'
import type { DataTableColumn, StatusTone } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useCodeRules } from '@/composables/useCodeRules'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  Checkbox,
  DataTable,
  DataTablePagination,
  DatePicker,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  Input,
  PageHeader,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '编码规则' } })

const {
  filters,
  rules,
  rulesError,
  rulesPending,
  refresh,
  fetchRuleDetail,
  previewCode,
  createRuleVersion,
  createPending,
} = useCodeRules()

// ── 中文映射 ────────────────────────────────────────────────────
type SegmentType = NonNullable<BusinessConsoleCodeRuleSegment['type']>
type ScopeDimension = NonNullable<BusinessConsoleCodeRuleItem['scope']>
type ResetPeriod = NonNullable<BusinessConsoleCodeRuleSegment['reset']>
type FieldTransform = NonNullable<BusinessConsoleCodeRuleSegment['transform']>

const SEGMENT_TYPE_LABEL: Record<SegmentType, string> = {
  constant: '常量',
  date: '日期',
  sequence: '流水号',
  field: '字段',
  checksum: '校验位',
}
const SCOPE_LABEL: Record<ScopeDimension, string> = {
  organization: '组织',
  environment: '环境',
  site: '站点',
}
const RESET_LABEL: Record<ResetPeriod, string> = {
  none: '不重置',
  day: '按天',
  month: '按月',
  year: '按年',
}
const TRANSFORM_LABEL: Record<FieldTransform, string> = {
  none: '原样',
  upper: '大写',
  lower: '小写',
}

const SEGMENT_TYPE_OPTIONS = (Object.keys(SEGMENT_TYPE_LABEL) as SegmentType[])
  .map((value) => ({ value, label: SEGMENT_TYPE_LABEL[value] }))
const SCOPE_OPTIONS = (Object.keys(SCOPE_LABEL) as ScopeDimension[])
  .map((value) => ({ value, label: SCOPE_LABEL[value] }))
const RESET_OPTIONS = (Object.keys(RESET_LABEL) as ResetPeriod[])
  .map((value) => ({ value, label: RESET_LABEL[value] }))
const TRANSFORM_OPTIONS = (Object.keys(TRANSFORM_LABEL) as FieldTransform[])
  .map((value) => ({ value, label: TRANSFORM_LABEL[value] }))

function segmentTypeLabel(type?: SegmentType | null) {
  return type ? SEGMENT_TYPE_LABEL[type] ?? type : '—'
}
function scopeLabel(scope?: ScopeDimension | null) {
  return scope ? SCOPE_LABEL[scope] ?? scope : '—'
}
function resetLabel(reset?: ResetPeriod | null) {
  return reset ? RESET_LABEL[reset] ?? reset : '不重置'
}
function transformLabel(transform?: FieldTransform | null) {
  return transform ? TRANSFORM_LABEL[transform] ?? transform : '原样'
}

// 单段拼成可读串，如「常量(SKU)」「日期(yyMM)」「流水(4)」。
function segmentSummary(segment: BusinessConsoleCodeRuleSegment) {
  const name = segmentTypeLabel(segment.type)
  switch (segment.type) {
    case 'constant':
      return segment.value ? `${name}(${segment.value})` : name
    case 'date':
      return segment.format ? `${name}(${segment.format})` : name
    case 'sequence':
      return segment.width != null ? `${name}(${segment.width})` : name
    case 'field':
      return segment.source ? `${name}(${segment.source})` : name
    case 'checksum':
      return segment.algorithm ? `${name}(${segment.algorithm})` : name
    default:
      return name
  }
}
function formatSegments(segments?: BusinessConsoleCodeRuleSegment[] | null) {
  if (!segments?.length) return '—'
  return segments.map(segmentSummary).join('-')
}

// 只读 Sheet 中每段的关键参数串。
function segmentParams(segment: BusinessConsoleCodeRuleSegment) {
  switch (segment.type) {
    case 'constant':
      return segment.value ?? '—'
    case 'date':
      return segment.format ?? '—'
    case 'sequence':
      return `宽${segment.width ?? 0} 起${segment.start ?? 0} 补'${segment.padChar ?? ''}' ${resetLabel(segment.reset)}`
    case 'field':
      return `源:${segment.source ?? '—'} ${transformLabel(segment.transform)} 上限:${segment.maxLength ?? '—'}`
    case 'checksum':
      return segment.algorithm ?? '—'
    default:
      return '—'
  }
}

function ruleStatusTone(rule: BusinessConsoleCodeRuleItem): StatusTone {
  return rule.isActive ? 'success' : 'neutral'
}
function ruleStatusLabel(rule: BusinessConsoleCodeRuleItem) {
  return rule.isActive ? '启用' : '停用'
}

function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
const listErrorMessage = computed(() => formatError(rulesError.value))

// ── 客户端分页 ──────────────────────────────────────────────────
const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
const pagedRules = computed(() => {
  const startIndex = (page.value - 1) * pageSizeNumber.value
  return rules.value.slice(startIndex, startIndex + pageSizeNumber.value)
})

const columns: DataTableColumn<BusinessConsoleCodeRuleItem>[] = [
  { key: 'ruleKey', header: '规则键', width: 'w-48' },
  { key: 'displayName', header: '名称', cellClass: 'font-medium' },
  { key: 'appliesTo', header: '适用对象' },
  { key: 'scope', header: '范围', width: 'w-24' },
  { key: 'format', header: '格式预览' },
  { key: 'version', header: '版本', width: 'w-20', align: 'end' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-32' },
]

// ── 查看 Sheet 抽屉 ─────────────────────────────────────────────
interface VersionRow {
  version?: number
  status?: string
  effectiveFromUtc?: string
  createdBy?: string
  changeReason?: string
  createdAtUtc?: string
}

const viewOpen = shallowRef(false)
const viewTarget = shallowRef<BusinessConsoleCodeRuleItem | null>(null)
const versions = ref<VersionRow[]>([])
const versionsPending = ref(false)
const versionsError = ref('')
const viewSegments = computed(() => viewTarget.value?.segments ?? [])

const viewPreview = ref('')
const viewPreviewPending = ref(false)

async function openView(row: BusinessConsoleCodeRuleItem) {
  viewTarget.value = row
  viewOpen.value = true
  versions.value = []
  versionsError.value = ''
  viewPreview.value = ''
  if (!row.ruleKey) return
  versionsPending.value = true
  try {
    const detail = await fetchRuleDetail(row.ruleKey)
    if (detail?.rule) viewTarget.value = detail.rule
    versions.value = detail?.versions ?? []
  }
  catch (error) {
    versionsError.value = formatError(error) || '加载版本历史失败，请稍后重试。'
  }
  finally {
    versionsPending.value = false
  }
}

async function previewViewSample() {
  const rule = viewTarget.value
  if (!rule?.ruleKey) return
  viewPreviewPending.value = true
  viewPreview.value = ''
  try {
    const sample = await previewCode(rule.ruleKey, rule.segments ?? [])
    viewPreview.value = sample ?? '（无返回）'
  }
  catch (error) {
    notifyError(error)
  }
  finally {
    viewPreviewPending.value = false
  }
}

// ── 新建版本 Dialog ─────────────────────────────────────────────
interface SegmentRow {
  type: SegmentType | ''
  value: string
  format: string
  width: string
  start: string
  padChar: string
  reset: ResetPeriod
  source: string
  transform: FieldTransform
  maxLength: string
  algorithm: string
  required: boolean
}

interface VersionForm {
  displayName: string
  appliesTo: string
  scope: ScopeDimension
  effectiveFromUtc: string | null
  changeReason: string
  createdBy: string
  segments: SegmentRow[]
}

function blankSegment(): SegmentRow {
  return {
    type: 'constant',
    value: '',
    format: '',
    width: '',
    start: '',
    padChar: '',
    reset: 'none',
    source: '',
    transform: 'none',
    maxLength: '',
    algorithm: '',
    required: false,
  }
}

function toSegmentRow(segment: BusinessConsoleCodeRuleSegment): SegmentRow {
  return {
    type: segment.type ?? 'constant',
    value: segment.value ?? '',
    format: segment.format ?? '',
    width: segment.width == null ? '' : String(segment.width),
    start: segment.start == null ? '' : String(segment.start),
    padChar: segment.padChar ?? '',
    reset: segment.reset ?? 'none',
    source: segment.source ?? '',
    transform: segment.transform ?? 'none',
    maxLength: segment.maxLength == null ? '' : String(segment.maxLength),
    algorithm: segment.algorithm ?? '',
    required: segment.required ?? false,
  }
}

function blankForm(): VersionForm {
  return {
    displayName: '',
    appliesTo: '',
    scope: 'organization',
    effectiveFromUtc: null,
    changeReason: '',
    createdBy: '',
    segments: [blankSegment()],
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
const editingRuleKey = shallowRef<string | null>(null)
const form = reactive<VersionForm>(blankForm())
const formPreview = ref('')
const formPreviewPending = ref(false)

function parseNumber(value: string): number | undefined {
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}

const displayNameValid = computed(() => form.displayName.trim().length > 0)
const createdByValid = computed(() => form.createdBy.trim().length > 0)
const segmentsValid = computed(() =>
  form.segments.length > 0 && form.segments.every((s) => s.type !== ''),
)
const canSubmit = computed(() =>
  displayNameValid.value && createdByValid.value && segmentsValid.value,
)

// 把编辑行映射成契约段（number 空串转 undefined）。
function rowToSegment(row: SegmentRow): BusinessConsoleCodeRuleSegment {
  const segment: BusinessConsoleCodeRuleSegment = {
    type: (row.type || 'constant') as SegmentType,
    required: row.required,
  }
  switch (row.type) {
    case 'constant':
      segment.value = row.value.trim() || undefined
      break
    case 'date':
      segment.format = row.format.trim() || undefined
      break
    case 'sequence':
      segment.width = parseNumber(row.width)
      segment.start = parseNumber(row.start)
      segment.padChar = row.padChar || undefined
      segment.reset = row.reset
      break
    case 'field':
      segment.source = row.source.trim() || undefined
      segment.transform = row.transform
      segment.maxLength = parseNumber(row.maxLength)
      break
    case 'checksum':
      segment.algorithm = row.algorithm.trim() || undefined
      break
  }
  return segment
}

function openCreate(row: BusinessConsoleCodeRuleItem) {
  if (!row.ruleKey) return
  editingRuleKey.value = row.ruleKey
  showErrors.value = false
  formPreview.value = ''
  Object.assign(form, {
    displayName: row.displayName ?? '',
    appliesTo: row.appliesTo ?? '',
    scope: row.scope ?? 'organization',
    effectiveFromUtc: null,
    changeReason: '',
    createdBy: '',
    segments: row.segments?.length ? row.segments.map(toSegmentRow) : [blankSegment()],
  })
  formOpen.value = true
}

function addSegment() {
  form.segments.push(blankSegment())
}
function removeSegment(index: number) {
  if (form.segments.length <= 1) return
  form.segments.splice(index, 1)
}

async function previewFormSample() {
  if (!editingRuleKey.value) return
  formPreviewPending.value = true
  formPreview.value = ''
  try {
    const segments = form.segments.map(rowToSegment)
    const sample = await previewCode(editingRuleKey.value, segments)
    formPreview.value = sample ?? '（无返回）'
  }
  catch (error) {
    notifyError(error)
  }
  finally {
    formPreviewPending.value = false
  }
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const ruleKey = editingRuleKey.value
  if (!ruleKey) return
  const displayName = form.displayName.trim()
  const body: BusinessConsoleCreateCodeRuleVersionRequest = {
    organizationId: filters.organizationId,
    environmentId: filters.environmentId,
    displayName,
    appliesTo: form.appliesTo.trim() || undefined,
    scope: form.scope,
    segments: form.segments.map(rowToSegment),
    isActive: true,
    effectiveFromUtc: form.effectiveFromUtc || null,
    createdBy: form.createdBy.trim(),
    changeReason: form.changeReason.trim() || undefined,
  }
  try {
    await createRuleVersion(ruleKey, body)
    notifySuccess(`规则「${displayName}」新版本已发布。`)
    showErrors.value = false
    formOpen.value = false
    editingRuleKey.value = null
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="编码规则"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${rules.length} 条规则`"
    >
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="rulesPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="pagedRules"
      row-key="ruleKey"
      :loading="rulesPending"
      empty-message="暂无编码规则。"
    >
      <template #cell-scope="{ row }">{{ scopeLabel(row.scope) }}</template>
      <template #cell-format="{ row }">
        <span class="text-muted-foreground">{{ formatSegments(row.segments) }}</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadge :label="ruleStatusLabel(row)" :tone="ruleStatusTone(row)" />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <Button type="button" variant="ghost" size="sm" @click="openView(row)">查看</Button>
          <Button type="button" variant="ghost" size="sm" @click="openCreate(row)">新建版本</Button>
        </div>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="rules.length" />

    <!-- 查看 Sheet 抽屉 -->
    <Dialog v-model:open="viewOpen">
      <DialogContent class="sm:max-w-4xl">
        <DialogHeader>
          <DialogTitle>编码规则 · 明细</DialogTitle>
          <DialogDescription>
            {{ viewTarget ? `${viewTarget.ruleKey} · ${viewTarget.displayName ?? ''}` : '' }}
          </DialogDescription>
        </DialogHeader>
        <div v-if="viewTarget" class="grid max-h-[70vh] content-start gap-4 overflow-y-auto px-1">
          <div class="grid gap-2 text-sm">
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">规则键</span>
              <span class="font-medium">{{ viewTarget.ruleKey ?? '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">名称</span>
              <span class="font-medium">{{ viewTarget.displayName ?? '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">适用对象</span>
              <span class="font-medium">{{ viewTarget.appliesTo ?? '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">范围</span>
              <span class="font-medium">{{ scopeLabel(viewTarget.scope) }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">当前版本</span>
              <span class="font-medium tabular-nums">{{ viewTarget.version ?? '—' }}</span>
            </div>
            <div class="flex justify-between gap-3">
              <span class="text-muted-foreground">状态</span>
              <StatusBadge :label="ruleStatusLabel(viewTarget)" :tone="ruleStatusTone(viewTarget)" />
            </div>
          </div>

          <div>
            <FormSectionTitle>段定义</FormSectionTitle>
            <div v-if="viewSegments.length" class="mt-2 overflow-x-auto rounded-md border">
              <table class="w-full text-sm">
                <thead class="bg-muted/40 text-muted-foreground">
                  <tr>
                    <th class="px-3 py-2 text-right font-medium">序号</th>
                    <th class="px-3 py-2 text-left font-medium">类型</th>
                    <th class="px-3 py-2 text-left font-medium">关键参数</th>
                    <th class="px-3 py-2 text-left font-medium">必填</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(segment, i) in viewSegments" :key="i" class="border-t">
                    <td class="px-3 py-2 text-right tabular-nums">{{ i + 1 }}</td>
                    <td class="px-3 py-2">{{ segmentTypeLabel(segment.type) }}</td>
                    <td class="px-3 py-2 text-muted-foreground">{{ segmentParams(segment) }}</td>
                    <td class="px-3 py-2">{{ segment.required ? '是' : '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-else class="mt-2 rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              该规则没有段定义。
            </p>
          </div>

          <div class="grid gap-2">
            <div class="flex items-center gap-2">
              <Button type="button" variant="outline" size="sm" :disabled="viewPreviewPending" @click="previewViewSample">
                <Spinner v-if="viewPreviewPending" aria-hidden="true" />
                预览示例编码
              </Button>
              <code v-if="viewPreview" class="rounded bg-muted px-2 py-1 text-sm font-mono">{{ viewPreview }}</code>
            </div>
          </div>

          <div>
            <FormSectionTitle>版本历史</FormSectionTitle>
            <div v-if="versionsPending" class="mt-2 flex items-center gap-2 py-2 text-sm text-muted-foreground">
              <Spinner aria-hidden="true" />
              加载版本历史…
            </div>
            <p v-else-if="versionsError" class="mt-2 rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive" role="alert">
              {{ versionsError }}
            </p>
            <div v-else-if="versions.length" class="mt-2 overflow-x-auto rounded-md border">
              <table class="w-full text-sm">
                <thead class="bg-muted/40 text-muted-foreground">
                  <tr>
                    <th class="px-3 py-2 text-right font-medium">版本</th>
                    <th class="px-3 py-2 text-left font-medium">状态</th>
                    <th class="px-3 py-2 text-left font-medium">生效时间</th>
                    <th class="px-3 py-2 text-left font-medium">创建人</th>
                    <th class="px-3 py-2 text-left font-medium">变更原因</th>
                    <th class="px-3 py-2 text-left font-medium">创建时间</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="(ver, i) in versions" :key="i" class="border-t">
                    <td class="px-3 py-2 text-right tabular-nums">{{ ver.version ?? '—' }}</td>
                    <td class="px-3 py-2">{{ ver.status ?? '—' }}</td>
                    <td class="px-3 py-2">{{ ver.effectiveFromUtc ?? '即时' }}</td>
                    <td class="px-3 py-2">{{ ver.createdBy ?? '—' }}</td>
                    <td class="px-3 py-2 text-muted-foreground">{{ ver.changeReason ?? '—' }}</td>
                    <td class="px-3 py-2 text-muted-foreground">{{ ver.createdAtUtc ?? '—' }}</td>
                  </tr>
                </tbody>
              </table>
            </div>
            <p v-else class="mt-2 rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
              暂无版本历史
            </p>
          </div>
        </div>
      </DialogContent>
    </Dialog>

    <!-- 新建版本 Dialog -->
    <Dialog v-model:open="formOpen">
      <DialogContent class="sm:max-w-3xl">
        <DialogHeader>
          <DialogTitle>新建编码规则版本</DialogTitle>
          <DialogDescription>
            发布新版本会带入该规则当前值，不影响已分配的历史编码。带 * 为必填项。
          </DialogDescription>
        </DialogHeader>
        <form class="grid gap-5" @submit.prevent="submitForm">
          <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
            请填写带 * 的必填项（名称、创建人），并确保至少 1 个段且每段已选类型。
          </p>

          <FormSectionTitle>版本信息</FormSectionTitle>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field :data-invalid="showErrors && !displayNameValid">
              <FieldLabel for="cr-name">名称 <span class="text-destructive">*</span></FieldLabel>
              <Input id="cr-name" v-model="form.displayName" placeholder="规则名称" />
            </Field>
            <Field>
              <FieldLabel for="cr-applies">适用对象</FieldLabel>
              <Input id="cr-applies" v-model="form.appliesTo" placeholder="如 物料 / 工单" />
            </Field>
            <Field>
              <FieldLabel for="cr-scope">范围</FieldLabel>
              <Select v-model="form.scope">
                <SelectTrigger id="cr-scope"><SelectValue placeholder="选择范围" /></SelectTrigger>
                <SelectContent>
                  <SelectItem v-for="o in SCOPE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                </SelectContent>
              </Select>
            </Field>
            <Field>
              <FieldLabel>生效时间</FieldLabel>
              <DatePicker v-model="form.effectiveFromUtc" placeholder="留空即时生效" class="w-full" />
              <FieldDescription>留空即时生效。</FieldDescription>
            </Field>
            <Field>
              <FieldLabel for="cr-reason">变更原因</FieldLabel>
              <Input id="cr-reason" v-model="form.changeReason" placeholder="可选，本次变更说明" />
            </Field>
            <Field :data-invalid="showErrors && !createdByValid">
              <FieldLabel for="cr-by">创建人 <span class="text-destructive">*</span></FieldLabel>
              <Input id="cr-by" v-model="form.createdBy" placeholder="你的账号/姓名" />
            </Field>
          </FieldGroup>

          <div class="flex items-center justify-between">
            <FormSectionTitle>段定义</FormSectionTitle>
            <Button type="button" variant="outline" size="sm" @click="addSegment">
              <PlusIcon aria-hidden="true" />
              增加段
            </Button>
          </div>

          <div class="grid gap-2">
            <div
              v-for="(segment, index) in form.segments"
              :key="index"
              class="grid gap-2 rounded-md border p-3"
            >
              <div class="grid grid-cols-[8rem_1fr_auto] items-end gap-2">
                <Field :data-invalid="showErrors && segment.type === ''">
                  <FieldLabel :for="`cr-type-${index}`">类型 <span class="text-destructive">*</span></FieldLabel>
                  <Select v-model="segment.type">
                    <SelectTrigger :id="`cr-type-${index}`"><SelectValue placeholder="选择类型" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in SEGMENT_TYPE_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                </Field>

                <!-- 常量 -->
                <Field v-if="segment.type === 'constant'">
                  <FieldLabel :for="`cr-value-${index}`">常量值</FieldLabel>
                  <Input :id="`cr-value-${index}`" v-model="segment.value" placeholder="如 SKU" />
                </Field>

                <!-- 日期 -->
                <Field v-else-if="segment.type === 'date'">
                  <FieldLabel :for="`cr-format-${index}`">日期格式</FieldLabel>
                  <Input :id="`cr-format-${index}`" v-model="segment.format" placeholder="yyMM" />
                </Field>

                <!-- 流水号 -->
                <div v-else-if="segment.type === 'sequence'" class="grid grid-cols-2 gap-2 sm:grid-cols-4">
                  <Field>
                    <FieldLabel :for="`cr-width-${index}`">宽度</FieldLabel>
                    <Input :id="`cr-width-${index}`" v-model="segment.width" type="number" min="0" placeholder="4" />
                  </Field>
                  <Field>
                    <FieldLabel :for="`cr-start-${index}`">起始</FieldLabel>
                    <Input :id="`cr-start-${index}`" v-model="segment.start" type="number" placeholder="1" />
                  </Field>
                  <Field>
                    <FieldLabel :for="`cr-pad-${index}`">补位字符</FieldLabel>
                    <Input :id="`cr-pad-${index}`" v-model="segment.padChar" placeholder="0" />
                  </Field>
                  <Field>
                    <FieldLabel :for="`cr-reset-${index}`">重置</FieldLabel>
                    <Select v-model="segment.reset">
                      <SelectTrigger :id="`cr-reset-${index}`"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in RESET_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                </div>

                <!-- 字段 -->
                <div v-else-if="segment.type === 'field'" class="grid grid-cols-1 gap-2 sm:grid-cols-3">
                  <Field>
                    <FieldLabel :for="`cr-source-${index}`">源</FieldLabel>
                    <Input :id="`cr-source-${index}`" v-model="segment.source" placeholder="如 categoryCode" />
                  </Field>
                  <Field>
                    <FieldLabel :for="`cr-transform-${index}`">变换</FieldLabel>
                    <Select v-model="segment.transform">
                      <SelectTrigger :id="`cr-transform-${index}`"><SelectValue /></SelectTrigger>
                      <SelectContent>
                        <SelectItem v-for="o in TRANSFORM_OPTIONS" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                      </SelectContent>
                    </Select>
                  </Field>
                  <Field>
                    <FieldLabel :for="`cr-maxlen-${index}`">上限</FieldLabel>
                    <Input :id="`cr-maxlen-${index}`" v-model="segment.maxLength" type="number" min="0" placeholder="不限" />
                  </Field>
                </div>

                <!-- 校验位 -->
                <Field v-else-if="segment.type === 'checksum'">
                  <FieldLabel :for="`cr-algo-${index}`">算法</FieldLabel>
                  <Input :id="`cr-algo-${index}`" v-model="segment.algorithm" placeholder="如 mod10" />
                </Field>

                <div v-else />

                <Button
                  type="button"
                  variant="ghost"
                  size="icon"
                  aria-label="删除该段"
                  :disabled="form.segments.length <= 1"
                  @click="removeSegment(index)"
                >
                  <Trash2Icon aria-hidden="true" />
                </Button>
              </div>

              <label :for="`cr-required-${index}`" class="flex cursor-pointer select-none items-center gap-2 text-sm">
                <Checkbox :id="`cr-required-${index}`" v-model:checked="segment.required" />
                <span>必填</span>
              </label>
            </div>
          </div>

          <div class="flex items-center gap-2">
            <Button type="button" variant="outline" size="sm" :disabled="formPreviewPending" @click="previewFormSample">
              <Spinner v-if="formPreviewPending" aria-hidden="true" />
              预览
            </Button>
            <code v-if="formPreview" class="rounded bg-muted px-2 py-1 text-sm font-mono">{{ formPreview }}</code>
          </div>

          <DialogFooter>
            <Button type="button" variant="outline" @click="formOpen = false">取消</Button>
            <Button type="submit" :disabled="createPending">
              <Spinner v-if="createPending" aria-hidden="true" />
              发布版本
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  </BusinessLayout>
</template>
