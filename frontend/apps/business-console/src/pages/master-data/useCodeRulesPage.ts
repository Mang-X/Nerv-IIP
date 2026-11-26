import type {
  BusinessConsoleCodeRuleItem,
  BusinessConsoleCodeRuleSegment,
  BusinessConsoleCreateCodeRuleVersionRequest,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn, StatusTone } from '@nerv-iip/ui'
import { useCodeRules } from '@/composables/master-data/useCodeRules'
import { computed, reactive, ref, shallowRef } from 'vue'
import { inlineErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

export function useCodeRulesPage() {
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

  // 后端 CodeRuleDefinition.Validate() 允许的日期格式 / 校验算法，下拉给定避免提交必被拒。
  const DATE_FORMAT_OPTIONS = ['yyyyMMdd', 'yyMMdd', 'yyyyMM', 'yyMM', 'yyyy', 'yy']
  const CHECKSUM_ALGORITHM_OPTIONS = ['hash-mod10', 'hash-mod11']

  const SEGMENT_TYPE_OPTIONS = (Object.keys(SEGMENT_TYPE_LABEL) as SegmentType[]).map((value) => ({
    value,
    label: SEGMENT_TYPE_LABEL[value],
  }))
  const SCOPE_OPTIONS = (Object.keys(SCOPE_LABEL) as ScopeDimension[]).map((value) => ({
    value,
    label: SCOPE_LABEL[value],
  }))
  const RESET_OPTIONS = (Object.keys(RESET_LABEL) as ResetPeriod[]).map((value) => ({
    value,
    label: RESET_LABEL[value],
  }))
  const TRANSFORM_OPTIONS = (Object.keys(TRANSFORM_LABEL) as FieldTransform[]).map((value) => ({
    value,
    label: TRANSFORM_LABEL[value],
  }))

  function segmentTypeLabel(type?: SegmentType | null) {
    return type ? (SEGMENT_TYPE_LABEL[type] ?? type) : '—'
  }
  function scopeLabel(scope?: ScopeDimension | null) {
    return scope ? (SCOPE_LABEL[scope] ?? scope) : '—'
  }
  function resetLabel(reset?: ResetPeriod | null) {
    return reset ? (RESET_LABEL[reset] ?? reset) : '不重置'
  }
  function transformLabel(transform?: FieldTransform | null) {
    return transform ? (TRANSFORM_LABEL[transform] ?? transform) : '原样'
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
  const listErrorMessage = computed(() => inlineErrorMessage(rulesError.value))

  // ── 客户端分页 ──────────────────────────────────────────────────
  const page = ref(1)
  const pageSize = ref('10')
  const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
  const pagedRules = computed(() => {
    const startIndex = (page.value - 1) * pageSizeNumber.value
    return rules.value.slice(startIndex, startIndex + pageSizeNumber.value)
  })

  const columns: NvDataTableColumn<BusinessConsoleCodeRuleItem>[] = [
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
    } catch (error) {
      versionsError.value = inlineErrorMessage(error) || '加载版本历史失败，请稍后重试。'
    } finally {
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
    } catch (error) {
      notifyOperationFailure('生成编码预览失败', error, '生成编码预览失败，请稍后重试。')
    } finally {
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
      segments: [blankSegment()],
    }
  }

  const formOpen = shallowRef(false)
  const showErrors = ref(false)
  const editingRuleKey = shallowRef<string | null>(null)
  // 从所选规则行带出的只读上下文（规则键 / 当前版本），不让用户重填。
  const editingCurrentVersion = shallowRef<string>('')
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
  const CHANGE_REASON_MAX_LENGTH = 500
  const changeReasonError = computed(() => {
    if (form.changeReason.length > CHANGE_REASON_MAX_LENGTH) {
      return `变更原因不能超过 ${CHANGE_REASON_MAX_LENGTH} 个字符。`
    }
    if (form.changeReason.length > 0 && form.changeReason.trim().length === 0) {
      return '变更原因不能只包含空白字符。'
    }
    if (form.changeReason.trim().length === 0) return '请输入变更原因。'
    return ''
  })
  const changeReasonValid = computed(() => changeReasonError.value === '')

  // 单段校验，对齐后端 CodeRuleDefinition.Validate()：返回 null 表示该段合法，否则给中文错误。
  function segmentError(row: SegmentRow): string | null {
    if (row.type === '') return '请选择段类型。'
    switch (row.type) {
      case 'constant':
        return row.value.trim() ? null : '常量段的常量值必填。'
      case 'date':
        return DATE_FORMAT_OPTIONS.includes(row.format.trim()) ? null : '请选择有效的日期格式。'
      case 'sequence': {
        const width = parseNumber(row.width)
        const start = parseNumber(row.start)
        if (width == null || width <= 0) return '流水段宽度必须大于 0。'
        if (start == null || start <= 0) return '流水段起始值必须大于 0。'
        return null
      }
      case 'field': {
        if (!row.source.trim()) return '字段段的源必填。'
        const maxLength = parseNumber(row.maxLength)
        if (maxLength == null || maxLength <= 0) return '字段段的上限必须大于 0。'
        return null
      }
      case 'checksum':
        return CHECKSUM_ALGORITHM_OPTIONS.includes(row.algorithm.trim())
          ? null
          : '请选择有效的校验算法。'
      default:
        return null
    }
  }

  const segmentErrors = computed(() => form.segments.map(segmentError))
  // 后端要求有且仅有 1 个流水段（0 或 ≥2 都报错）。
  const sequenceCount = computed(() => form.segments.filter((s) => s.type === 'sequence').length)
  const sequenceCountError = computed(() => {
    if (sequenceCount.value === 0) return '至少需要 1 个流水段。'
    if (sequenceCount.value > 1) return '只能有 1 个流水段。'
    return ''
  })
  const segmentsValid = computed(
    () =>
      form.segments.length > 0 &&
      segmentErrors.value.every((e) => e === null) &&
      sequenceCountError.value === '',
  )
  const canSubmit = computed(
    () =>
      displayNameValid.value &&
      changeReasonValid.value &&
      segmentsValid.value,
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
    editingCurrentVersion.value =
      row.version === null || row.version === undefined ? '' : String(row.version)
    showErrors.value = false
    formPreview.value = ''
    Object.assign(form, {
      displayName: row.displayName ?? '',
      appliesTo: row.appliesTo ?? '',
      scope: row.scope ?? 'organization',
      effectiveFromUtc: null,
      changeReason: '',
      segments: row.segments?.length ? row.segments.map(toSegmentRow) : [blankSegment()],
    })
    formOpen.value = true
  }

  function resetVersionForm() {
    Object.assign(form, blankForm())
    showErrors.value = false
    formPreview.value = ''
    editingRuleKey.value = null
    editingCurrentVersion.value = ''
  }

  function onFormOpenChange(value: boolean) {
    formOpen.value = value
    if (!value && !createPending.value) resetVersionForm()
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
    if (!canSubmit.value) {
      showErrors.value = true
      return
    }
    formPreviewPending.value = true
    formPreview.value = ''
    try {
      const segments = form.segments.map(rowToSegment)
      const sample = await previewCode(editingRuleKey.value, segments)
      formPreview.value = sample ?? '（无返回）'
    } catch (error) {
      notifyOperationFailure('生成编码预览失败', error, '生成编码预览失败，请稍后重试。')
    } finally {
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
      changeReason: form.changeReason.trim(),
    }
    try {
      await createRuleVersion(ruleKey, body)
      notifySuccess(`规则「${displayName}」新版本已发布。`)
      formOpen.value = false
      resetVersionForm()
    } catch (error) {
      notifyOperationFailure('发布编码规则失败', error, '发布编码规则失败，请稍后重试。')
    }
  }

  return {
    CHANGE_REASON_MAX_LENGTH,
    CHECKSUM_ALGORITHM_OPTIONS,
    DATE_FORMAT_OPTIONS,
    RESET_OPTIONS,
    SCOPE_OPTIONS,
    SEGMENT_TYPE_OPTIONS,
    TRANSFORM_OPTIONS,
    addSegment,
    canSubmit,
    changeReasonError,
    changeReasonValid,
    columns,
    createPending,
    displayNameValid,
    editingCurrentVersion,
    editingRuleKey,
    form,
    formOpen,
    formPreview,
    formPreviewPending,
    formatSegments,
    listErrorMessage,
    onFormOpenChange,
    openCreate,
    openView,
    page,
    pageSize,
    pagedRules,
    previewFormSample,
    previewViewSample,
    refresh,
    removeSegment,
    ruleStatusLabel,
    ruleStatusTone,
    rules,
    rulesPending,
    scopeLabel,
    segmentErrors,
    segmentParams,
    segmentTypeLabel,
    sequenceCountError,
    showErrors,
    submitForm,
    versions,
    versionsError,
    versionsPending,
    viewOpen,
    viewPreview,
    viewPreviewPending,
    viewSegments,
    viewTarget,
  }
}
