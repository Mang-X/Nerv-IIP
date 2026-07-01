<script setup lang="ts">
import type {
  BusinessConsoleCreateShiftRequest,
  BusinessConsoleCreateWorkCalendarRequest,
  BusinessConsoleResourceItem,
  BusinessConsoleWorkCalendarException,
  BusinessConsoleWorkCalendarHoliday,
  BusinessConsoleWorkCalendarWorkingTime,
  SystemDayOfWeek,
} from '@nerv-iip/api-client'
import type { DataTableProColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialogPro,
  AlertDialogProAction,
  AlertDialogProCancel,
  AlertDialogProContent,
  AlertDialogProDescription,
  AlertDialogProFooter,
  AlertDialogProHeader,
  AlertDialogProTitle,
  AlertDialogProTrigger,
  ButtonPro,
  DataTablePro,
  DatePickerPro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  FieldPro,
  FieldProDescription,
  FieldProGroup,
  FieldProLabel,
  InputPro,
  PageHeader,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  SheetPro,
  SheetProContent,
  SheetProDescription,
  SheetProHeader,
  SheetProTitle,
  Spinner,
  StatusBadgePro,
  TabsPro,
  TabsProContent,
  TabsProList,
  TabsProTrigger,
  Toolbar,
} from '@nerv-iip/ui'
import { CalendarCogIcon, CalendarRangeIcon, ChevronLeftIcon, ChevronRightIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '排班与日历', requiredPermissions: ['business.masterdata.resources.read'] } })

const shifts = useMasterDataResource<BusinessConsoleCreateShiftRequest>('shift')
const calendars = useMasterDataResource<BusinessConsoleCreateWorkCalendarRequest>('work-calendar')
const shiftActions = useMasterDataResourceActions('shift')
const calActions = useMasterDataResourceActions('work-calendar')

const columns: DataTableProColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]
function baseDetailFields(row: BusinessConsoleResourceItem, codeLabel: string, nameLabel: string) {
  return [
    { label: codeLabel, value: row.code ?? '' },
    { label: nameLabel, value: row.displayName ?? '' },
  ]
}
function rowKey(item: BusinessConsoleResourceItem) {
  return `${item.resourceType ?? ''}:${item.code || item.displayName || ''}`
}
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
function formatError(error: unknown) {
  return error instanceof Error ? error.message : error ? '请求失败，请稍后重试。' : ''
}
function filterRows(items: BusinessConsoleResourceItem[], keyword: string) {
  const kw = keyword.trim().toLowerCase()
  if (!kw) return items
  return items.filter((row) =>
    [row.code, row.displayName, row.snapshotVersion].some((value) => (value ?? '').toLowerCase().includes(kw)),
  )
}
function refreshAll() {
  void shifts.refresh()
  void calendars.refresh()
}

// ---- 班次 ----
const shiftKeyword = ref('')
const shiftPage = ref(1)
const shiftPageSize = ref('10')
const shiftOpen = ref(false)
const shiftShowErrors = ref(false)
const shiftEditingCode = shallowRef<string | null>(null)
const shiftEditLoading = shallowRef(false)
const shiftForm = reactive({ code: '', name: '', startsAt: '08:00', endsAt: '16:00', paidMinutes: '480' })
const shiftRows = computed(() => filterRows(shifts.items.value, shiftKeyword.value))
const shiftPaidValid = computed(() => (Number(shiftForm.paidMinutes) || 0) > 0)
const canCreateShift = computed(() => isNonEmpty(shiftForm.name) && shiftPaidValid.value)
// 编辑态也可改时段/计薪：名称必填 + 计薪 > 0。
const shiftFormValid = computed(() => (shiftEditingCode.value ? isNonEmpty(shiftForm.name) && shiftPaidValid.value : canCreateShift.value))
const shiftListError = computed(() => formatError(shifts.error.value))
const shiftCrossesMidnight = computed(() => {
  const start = shiftForm.startsAt.trim()
  const end = shiftForm.endsAt.trim()
  return !!start && !!end && end <= start
})
watch(shiftOpen, (open) => { if (open) shiftShowErrors.value = false })
watch([shiftKeyword, shiftPageSize], () => { shiftPage.value = 1 })
watch([shiftPage, shiftPageSize], () => {
  shifts.filters.skip = (shiftPage.value - 1) * (Number(shiftPageSize.value) || 10)
  shifts.filters.take = Number(shiftPageSize.value) || 10
}, { immediate: true })
function resetShiftForm() {
  Object.assign(shiftForm, { code: '', name: '', startsAt: '08:00', endsAt: '16:00', paidMinutes: '480' })
}
function openCreateShift() {
  shiftEditingCode.value = null
  resetShiftForm()
  shiftShowErrors.value = false
  shiftOpen.value = true
}
// 班次起止后端以 HH:mm:ss 存；表单 <input type=time> 用 HH:mm，互转。
function toTimeInput(value?: string | null) {
  if (!value) return ''
  return value.slice(0, 5)
}
function toTimePayload(value: string) {
  const v = value.trim()
  if (!v) return undefined
  return v.length === 5 ? `${v}:00` : v
}
async function openEditShift(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  shiftEditingCode.value = row.code
  shiftShowErrors.value = false
  shiftEditLoading.value = true
  shiftOpen.value = true
  try {
    const d = await shiftActions.fetchDetail(row.code)
    shiftForm.code = row.code
    shiftForm.name = d?.name ?? row.displayName ?? ''
    shiftForm.startsAt = toTimeInput(d?.startsAt) || '08:00'
    shiftForm.endsAt = toTimeInput(d?.endsAt) || '16:00'
    shiftForm.paidMinutes = d?.paidMinutes != null ? String(d.paidMinutes) : '480'
  }
  finally {
    shiftEditLoading.value = false
  }
}
async function submitShift() {
  if (shiftEditingCode.value) {
    if (!shiftFormValid.value) {
      shiftShowErrors.value = true
      return
    }
    try {
      await shiftActions.update(shiftEditingCode.value, {
        name: shiftForm.name.trim(),
        startsAt: toTimePayload(shiftForm.startsAt),
        endsAt: toTimePayload(shiftForm.endsAt),
        paidMinutes: Number(shiftForm.paidMinutes) || 480,
      })
      notifySuccess(`班次「${shiftForm.name.trim()}」已更新。`)
      resetShiftForm()
      shiftEditingCode.value = null
      shiftShowErrors.value = false
      shiftOpen.value = false
    }
    catch (error) {
      notifyError(error)
    }
    return
  }
  if (!canCreateShift.value) {
    shiftShowErrors.value = true
    return
  }
  try {
    await shifts.create({
      organizationId: shifts.filters.organizationId,
      environmentId: shifts.filters.environmentId,
      name: shiftForm.name.trim(),
      startsAt: shiftForm.startsAt.trim() || undefined,
      endsAt: shiftForm.endsAt.trim() || undefined,
      paidMinutes: Number(shiftForm.paidMinutes) || 480,
    })
    notifySuccess(`班次「${shiftForm.name.trim()}」已创建。`)
    resetShiftForm()
    shiftShowErrors.value = false
    shiftOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ---- 工作日历 ----
const calKeyword = ref('')
const calOpen = ref(false)
const calShowErrors = ref(false)
const calEditingCode = shallowRef<string | null>(null)
const calEditLoading = shallowRef(false)
const calForm = reactive({ code: '', name: '' })
const calRows = computed(() => filterRows(calendars.items.value, calKeyword.value))
const canCreateCal = computed(() => isNonEmpty(calForm.name))
const calListError = computed(() => formatError(calendars.error.value))
watch(calOpen, (open) => { if (open) calShowErrors.value = false })
// 日历用整行可点列表（无分页），一次取足；通常数量很少。
calendars.filters.skip = 0
calendars.filters.take = 200
function resetCalForm() {
  Object.assign(calForm, { code: '', name: '' })
}
function openCreateCal() {
  calEditingCode.value = null
  resetCalForm()
  calShowErrors.value = false
  calOpen.value = true
}
async function openEditCal(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  calEditingCode.value = row.code
  calShowErrors.value = false
  calEditLoading.value = true
  calOpen.value = true
  try {
    const d = await calActions.fetchDetail(row.code)
    calForm.code = row.code
    calForm.name = d?.name ?? row.displayName ?? ''
  }
  finally {
    calEditLoading.value = false
  }
}
async function submitCal() {
  if (!canCreateCal.value) {
    calShowErrors.value = true
    return
  }
  try {
    if (calEditingCode.value) {
      await calActions.update(calEditingCode.value, { name: calForm.name.trim() })
      notifySuccess(`工作日历「${calForm.name.trim()}」已更新。`)
    }
    else {
      await calendars.create({
        organizationId: calendars.filters.organizationId,
        environmentId: calendars.filters.environmentId,
        name: calForm.name.trim(),
      })
      notifySuccess(`工作日历「${calForm.name.trim()}」已创建。`)
    }
    resetCalForm()
    calEditingCode.value = null
    calShowErrors.value = false
    calOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}

// ---- 工作日历月历视图（读真实明细 + 编辑写回） ----
const WEEK_DAYS: { key: SystemDayOfWeek, label: string, short: string }[] = [
  { key: 'sunday', label: '周日', short: '日' },
  { key: 'monday', label: '周一', short: '一' },
  { key: 'tuesday', label: '周二', short: '二' },
  { key: 'wednesday', label: '周三', short: '三' },
  { key: 'thursday', label: '周四', short: '四' },
  { key: 'friday', label: '周五', short: '五' },
  { key: 'saturday', label: '周六', short: '六' },
]
const WEEK_KEYS: SystemDayOfWeek[] = WEEK_DAYS.map((d) => d.key)

// 后端 dayOfWeek 实际回传整数 0-6(契约写的是字符串,实测不一致),统一归一化成 'sunday'..'saturday'
// 字符串键,避免「数字 vs 字符串」比较永远不成立导致星期亮不起来。数字字符串('1')也兼容。
function normalizeDow(dow: string | number | null | undefined): SystemDayOfWeek | undefined {
  if (dow == null || dow === '') return undefined
  const n = typeof dow === 'number' ? dow : Number(dow)
  if (Number.isInteger(n) && n >= 0 && n <= 6) return WEEK_KEYS[n]
  return dow as SystemDayOfWeek
}

const selectedCalCode = shallowRef<string | null>(null)
const calDetailLoading = shallowRef(false)
const calDetailLoaded = shallowRef(false)
// 选中日历的真实明细（全部读自 fetchDetail，不画假数据）。
const workingTimes = ref<BusinessConsoleWorkCalendarWorkingTime[]>([])
const holidays = ref<BusinessConsoleWorkCalendarHoliday[]>([])
const exceptions = ref<BusinessConsoleWorkCalendarException[]>([])
const calBoardSaving = shallowRef(false)
// 节假日 / 例外日的增删收进右侧抽屉，月历网格保持只读展示。
const manageSheetOpen = ref(false)

const today = new Date()
const viewYear = ref(today.getFullYear())
const viewMonth = ref(today.getMonth()) // 0-based
const monthLabel = computed(() => `${viewYear.value}-${String(viewMonth.value + 1).padStart(2, '0')}`)

// 该日历配置了工作时段的星期集合（有 workingTime 即为工作日）。
const workingDaySet = computed(() => {
  const set = new Set<SystemDayOfWeek>()
  for (const wt of workingTimes.value) { const k = normalizeDow(wt.dayOfWeek); if (k) set.add(k) }
  return set
})
const holidayMap = computed(() => {
  const map = new Map<string, BusinessConsoleWorkCalendarHoliday>()
  for (const h of holidays.value) if (h.date) map.set(toDateKey(h.date), h)
  return map
})
const exceptionMap = computed(() => {
  const map = new Map<string, BusinessConsoleWorkCalendarException>()
  for (const e of exceptions.value) if (e.date) map.set(toDateKey(e.date), e)
  return map
})
const hasAnyDetail = computed(() =>
  workingTimes.value.length > 0 || holidays.value.length > 0 || exceptions.value.length > 0,
)

function toDateKey(value: string) {
  // 后端日期可能是 YYYY-MM-DD 或带时间；统一取本地 YYYY-MM-DD。
  return value.length >= 10 ? value.slice(0, 10) : value
}
function dateKey(year: number, month0: number, day: number) {
  return `${year}-${String(month0 + 1).padStart(2, '0')}-${String(day).padStart(2, '0')}`
}

interface DayCell {
  key: string
  day: number
  inMonth: boolean
  dow: SystemDayOfWeek
  // 'working' | 'rest' | 'holiday' | 'exception-working' | 'exception-rest'
  kind: 'working' | 'rest' | 'holiday' | 'exception-working' | 'exception-rest'
  label: string
  badge: string
  title: string
  isToday: boolean
}

const monthCells = computed<DayCell[]>(() => {
  const first = new Date(viewYear.value, viewMonth.value, 1)
  const startOffset = first.getDay() // 0=Sun
  const daysInMonth = new Date(viewYear.value, viewMonth.value + 1, 0).getDate()
  const cells: DayCell[] = []
  const todayKey = dateKey(today.getFullYear(), today.getMonth(), today.getDate())
  // 前导补白
  for (let i = 0; i < startOffset; i++) {
    cells.push(emptyCell(`pad-pre-${i}`))
  }
  for (let day = 1; day <= daysInMonth; day++) {
    const d = new Date(viewYear.value, viewMonth.value, day)
    const dow = WEEK_KEYS[d.getDay()]!
    const key = dateKey(viewYear.value, viewMonth.value, day)
    const holiday = holidayMap.value.get(key)
    const exception = exceptionMap.value.get(key)
    const baseWorking = workingDaySet.value.has(dow)

    let kind: DayCell['kind']
    let badge = ''
    let title = ''
    if (holiday) {
      kind = 'holiday'
      badge = '假'
      title = `法定节假日${holiday.name ? `：${holiday.name}` : ''}`
    }
    else if (exception) {
      kind = exception.isWorkingDay ? 'exception-working' : 'exception-rest'
      badge = exception.isWorkingDay ? '班' : '休'
      title = `例外日${exception.reason ? `：${exception.reason}` : ''}（${exception.isWorkingDay ? '当日上班' : '当日休息'}）`
    }
    else if (baseWorking) {
      kind = 'working'
      title = '工作日'
    }
    else {
      kind = 'rest'
      title = '休息日'
    }
    cells.push({
      key,
      day,
      inMonth: true,
      dow,
      kind,
      label: String(day),
      badge,
      title,
      isToday: key === todayKey,
    })
  }
  // 尾部补白凑满整周
  while (cells.length % 7 !== 0) {
    cells.push(emptyCell(`pad-post-${cells.length}`))
  }
  return cells
})

function emptyCell(key: string): DayCell {
  return { key, day: 0, inMonth: false, dow: 'sunday', kind: 'rest', label: '', badge: '', title: '', isToday: false }
}

// 月历格底色（语义 token，跟随主题；色盲友好靠角标 + title 文字双编码）。
// 4 类语义色,均为主题 token(随主题/明暗自适应):工作日=绿(明确可见,修隐形)、休息日=灰(下沉)、
// 节假日=红、例外日=琥珀(班/休 靠角标区分)。
const cellClassMap: Record<DayCell['kind'], string> = {
  'working': 'bg-success/15 text-foreground ring-1 ring-inset ring-success/35',
  'rest': 'bg-muted/50 text-muted-foreground',
  'holiday': 'bg-destructive/15 text-destructive ring-1 ring-inset ring-destructive/40',
  'exception-working': 'bg-warning/20 text-foreground ring-1 ring-inset ring-warning/45',
  'exception-rest': 'bg-warning/20 text-foreground ring-1 ring-inset ring-warning/45',
}

async function selectCalendar(row: BusinessConsoleResourceItem) {
  if (!row.code) return
  selectedCalCode.value = row.code
  await loadCalendarDetail(row.code)
}
async function loadCalendarDetail(code: string) {
  calDetailLoading.value = true
  calDetailLoaded.value = false
  try {
    const d = await calActions.fetchDetail(code)
    workingTimes.value = (d?.workingTimes ?? []).map((w) => ({ ...w }))
    holidays.value = (d?.holidays ?? []).map((h) => ({ ...h }))
    exceptions.value = (d?.exceptions ?? []).map((e) => ({ ...e }))
    calDetailLoaded.value = true
  }
  catch (error) {
    notifyError(error)
  }
  finally {
    calDetailLoading.value = false
  }
}
function prevMonth() {
  if (viewMonth.value === 0) { viewMonth.value = 11; viewYear.value-- }
  else viewMonth.value--
}
function nextMonth() {
  if (viewMonth.value === 11) { viewMonth.value = 0; viewYear.value++ }
  else viewMonth.value++
}
function goToday() {
  viewYear.value = today.getFullYear()
  viewMonth.value = today.getMonth()
}
// 点月历某天 → 打开抽屉并把该日期预填进两个草稿（加分项）。
function openManageSheet(prefillDate?: string) {
  if (prefillDate) {
    holidayDraft.date = prefillDate
    exceptionDraft.date = prefillDate
  }
  manageSheetOpen.value = true
}

const selectedCalName = computed(() =>
  calendars.items.value.find((c) => c.code === selectedCalCode.value)?.displayName ?? selectedCalCode.value ?? '',
)

// 防御性去重：按某个键保留第一条。配合后端 #382(Clear 累积 bug),前端发送前先去重,
// 避免自身制造重复数据。
function dedupeBy<T>(items: T[], keyOf: (item: T) => string | null | undefined): T[] {
  const seen = new Set<string>()
  const out: T[] = []
  for (const item of items) {
    const key = keyOf(item)
    if (key == null || key === '') {
      out.push(item)
      continue
    }
    if (seen.has(key)) continue
    seen.add(key)
    out.push(item)
  }
  return out
}

// 写回：把当前编辑后的 workingTimes/holidays/exceptions 经 update 提交。
async function persistCalendar(successMsg: string) {
  if (!selectedCalCode.value) return
  calBoardSaving.value = true
  // 发送前去重：workingTimes 按 dayOfWeek、holidays / exceptions 按 date(归一化),各保留第一条。
  const dedupedWorkingTimes = dedupeBy(workingTimes.value, (w) => normalizeDow(w.dayOfWeek) ?? '')
  const dedupedHolidays = dedupeBy(holidays.value, (h) => (h.date ? toDateKey(h.date) : ''))
  const dedupedExceptions = dedupeBy(exceptions.value, (e) => (e.date ? toDateKey(e.date) : ''))
  workingTimes.value = dedupedWorkingTimes
  holidays.value = dedupedHolidays
  exceptions.value = dedupedExceptions
  try {
    await calActions.update(selectedCalCode.value, {
      workingTimes: dedupedWorkingTimes,
      holidays: dedupedHolidays,
      exceptions: dedupedExceptions,
    })
    notifySuccess(successMsg)
  }
  catch (error) {
    notifyError(error)
  }
  finally {
    calBoardSaving.value = false
  }
}

// 设每周工作模式：切换某星期是否为工作日（默认时段 08:00–17:00）。
async function toggleWeekday(day: SystemDayOfWeek) {
  if (!selectedCalCode.value) return
  const exists = workingDaySet.value.has(day)
  if (exists) {
    workingTimes.value = workingTimes.value.filter((w) => normalizeDow(w.dayOfWeek) !== day)
  }
  // 仅当该星期尚无工作时段时才追加,避免重复添加同一 dayOfWeek。
  else if (!workingTimes.value.some((w) => normalizeDow(w.dayOfWeek) === day)) {
    workingTimes.value = [...workingTimes.value, { dayOfWeek: day }]
  }
  await persistCalendar('每周工作模式已更新。')
}

// 节假日与例外日互斥：一个日期至多一种覆盖。检测到跨类型冲突时，弹确认对话框，
// 确认后删掉另一类型再写入当前类型。冲突的待写入项暂存于 conflict。
const conflict = reactive({
  open: false,
  // 'holiday' = 待加节假日（该日已有例外日）；'exception' = 待加例外日（该日已有节假日）。
  kind: '' as '' | 'holiday' | 'exception',
  date: '',
  // 节假日待写入
  name: '',
  // 例外日待写入
  isWorkingDay: true,
  reason: null as string | null,
})
const conflictTitle = computed(() => {
  if (conflict.kind === 'holiday') return `${formatDate(conflict.date)} 已设为例外日`
  if (conflict.kind === 'exception') return `${formatDate(conflict.date)} 已是节假日`
  return ''
})
const conflictDescription = computed(() => {
  if (conflict.kind === 'holiday') return '节假日与例外日不能并存。确认后将删除该日期的例外日，并将其设为节假日。'
  if (conflict.kind === 'exception') return '节假日与例外日不能并存。确认后将删除该日期的节假日，并将其设为例外日。'
  return ''
})

// 加 / 删 节假日。
const holidayDraft = reactive({ date: '', name: '' })
async function addHoliday() {
  if (!selectedCalCode.value || !holidayDraft.date) return
  const key = holidayDraft.date
  if (holidayMap.value.has(key)) {
    notifyError(new Error('该日期已是节假日。'))
    return
  }
  // 与例外日冲突 → 弹确认（替换）。
  if (exceptionMap.value.has(key)) {
    Object.assign(conflict, { open: true, kind: 'holiday', date: key, name: holidayDraft.name.trim() })
    return
  }
  holidays.value = [...holidays.value, { date: key, name: holidayDraft.name.trim() || '节假日' }]
  holidayDraft.date = ''
  holidayDraft.name = ''
  await persistCalendar('节假日已添加。')
}
async function removeHoliday(date: string) {
  if (!selectedCalCode.value) return
  const key = toDateKey(date)
  holidays.value = holidays.value.filter((h) => toDateKey(h.date ?? '') !== key)
  await persistCalendar('节假日已删除。')
}

// 加 / 删 例外日（指定某日上班或休息，覆盖每周模式）。
const exceptionDraft = reactive({ date: '', isWorkingDay: 'true', reason: '' })
async function addException() {
  if (!selectedCalCode.value || !exceptionDraft.date) return
  const key = exceptionDraft.date
  if (exceptionMap.value.has(key)) {
    notifyError(new Error('该日期已有例外设置。'))
    return
  }
  // 与节假日冲突 → 弹确认（替换）。
  if (holidayMap.value.has(key)) {
    Object.assign(conflict, {
      open: true,
      kind: 'exception',
      date: key,
      isWorkingDay: exceptionDraft.isWorkingDay === 'true',
      reason: exceptionDraft.reason.trim() || null,
    })
    return
  }
  exceptions.value = [...exceptions.value, {
    date: key,
    isWorkingDay: exceptionDraft.isWorkingDay === 'true',
    reason: exceptionDraft.reason.trim() || null,
  }]
  exceptionDraft.date = ''
  exceptionDraft.reason = ''
  await persistCalendar('例外日已添加。')
}

// 确认替换：删除另一类型在该日期的项，写入当前类型，单次持久化。
async function resolveConflict() {
  const key = conflict.date
  if (!selectedCalCode.value || !key) {
    conflict.open = false
    return
  }
  if (conflict.kind === 'holiday') {
    // 删该日例外日，加节假日。
    exceptions.value = exceptions.value.filter((e) => toDateKey(e.date ?? '') !== key)
    holidays.value = [...holidays.value, { date: key, name: conflict.name || '节假日' }]
    holidayDraft.date = ''
    holidayDraft.name = ''
    conflict.open = false
    conflict.kind = ''
    await persistCalendar('已替换为节假日（原例外日已删除）。')
  }
  else if (conflict.kind === 'exception') {
    // 删该日节假日，加例外日。
    holidays.value = holidays.value.filter((h) => toDateKey(h.date ?? '') !== key)
    exceptions.value = [...exceptions.value, { date: key, isWorkingDay: conflict.isWorkingDay, reason: conflict.reason }]
    exceptionDraft.date = ''
    exceptionDraft.reason = ''
    conflict.open = false
    conflict.kind = ''
    await persistCalendar('已替换为例外日（原节假日已删除）。')
  }
}
function cancelConflict() {
  conflict.open = false
  conflict.kind = ''
}
async function removeException(date: string) {
  if (!selectedCalCode.value) return
  const key = toDateKey(date)
  exceptions.value = exceptions.value.filter((e) => toDateKey(e.date ?? '') !== key)
  await persistCalendar('例外日已删除。')
}

// DatePicker 的 modelValue 是 'YYYY-MM-DD' | null；草稿仍用字符串，故清空(null)归一为 ''。
const holidayDateModel = computed<string | null>({
  get: () => holidayDraft.date || null,
  set: (value) => { holidayDraft.date = value ?? '' },
})
const exceptionDateModel = computed<string | null>({
  get: () => exceptionDraft.date || null,
  set: (value) => { exceptionDraft.date = value ?? '' },
})

const sortedHolidays = computed(() =>
  [...holidays.value].filter((h) => h.date).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? '')),
)
const sortedExceptions = computed(() =>
  [...exceptions.value].filter((e) => e.date).sort((a, b) => (a.date ?? '').localeCompare(b.date ?? '')),
)
</script>

<template>
  <BusinessLayout>
    <PageHeader title="排班与日历" :breadcrumbs="[{ label: '基础数据' }]" :count="`${shifts.total.value} 个班次`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="shifts.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
      </template>
    </PageHeader>


    <TabsPro default-value="shift">
      <TabsProList>
        <TabsProTrigger value="shift">班次 ({{ shifts.total.value }})</TabsProTrigger>
        <TabsProTrigger value="work-calendar">工作日历 ({{ calendars.total.value }})</TabsProTrigger>
      </TabsProList>

      <!-- 班次 -->
      <TabsProContent value="shift" class="grid gap-3">
        <Toolbar v-model:search="shiftKeyword" search-placeholder="在当前页内筛选班次编码、名称">
          <template #actions>
            <DialogPro v-model:open="shiftOpen">
              <DialogProTrigger as-child>
                <ButtonPro size="sm" type="button" @click="openCreateShift"><PlusIcon aria-hidden="true" />新建班次</ButtonPro>
              </DialogProTrigger>
              <DialogProContent class="sm:max-w-lg">
                <DialogProHeader>
                  <DialogProTitle>{{ shiftEditingCode ? `编辑班次 · ${shiftEditingCode}` : '新建班次' }}</DialogProTitle>
                  <DialogProDescription>{{ shiftEditingCode ? '可修改名称、起止时间与计薪时长（编码不可修改）。带 * 为必填项。' : '定义一个排班时段及计薪时长。带 * 为必填项。' }}</DialogProDescription>
                </DialogProHeader>
                <form class="grid gap-4" @submit.prevent="submitShift">
                  <p v-if="shiftShowErrors && !shiftFormValid" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                    <FieldPro v-if="shiftEditingCode">
                      <FieldProLabel for="shift-code">班次编码</FieldProLabel>
                      <InputPro id="shift-code" :model-value="shiftForm.code" disabled />
                    </FieldPro>
                    <FieldPro :data-invalid="shiftShowErrors && !isNonEmpty(shiftForm.name)">
                      <FieldProLabel for="shift-name">班次名称 <span class="text-destructive">*</span></FieldProLabel>
                      <InputPro id="shift-name" v-model="shiftForm.name" autocomplete="off" required />
                      <FieldProDescription v-if="!shiftEditingCode">编码由系统自动生成。</FieldProDescription>
                    </FieldPro>
                    <FieldPro>
                      <FieldProLabel for="shift-start">开始时间</FieldProLabel>
                      <InputPro id="shift-start" v-model="shiftForm.startsAt" type="time" />
                    </FieldPro>
                    <FieldPro>
                      <FieldProLabel for="shift-end">结束时间</FieldProLabel>
                      <InputPro id="shift-end" v-model="shiftForm.endsAt" type="time" />
                      <FieldProDescription v-if="shiftCrossesMidnight">结束早于开始，按跨天班次处理。</FieldProDescription>
                    </FieldPro>
                    <FieldPro :data-invalid="shiftShowErrors && !shiftPaidValid">
                      <FieldProLabel for="shift-paid">计薪时长（分钟） <span class="text-destructive">*</span></FieldProLabel>
                      <InputPro id="shift-paid" v-model="shiftForm.paidMinutes" type="number" min="1" inputmode="numeric" />
                      <FieldProDescription>扣除休息后的有效计薪分钟数，默认 480（8 小时）。</FieldProDescription>
                    </FieldPro>
                  </FieldProGroup>
                  <DialogProFooter>
                    <ButtonPro type="button" variant="outline" @click="shiftOpen = false">取消</ButtonPro>
                    <ButtonPro type="submit" :disabled="shifts.createPending.value || shiftActions.updatePending.value || shiftEditLoading">
                      <Spinner v-if="shifts.createPending.value || shiftActions.updatePending.value" aria-hidden="true" />{{ shiftEditingCode ? '保存修改' : '保存班次' }}
                    </ButtonPro>
                  </DialogProFooter>
                </form>
              </DialogProContent>
            </DialogPro>
          </template>
        </Toolbar>
        <p v-if="shiftListError" class="text-sm text-destructive" role="alert">{{ shiftListError }}</p>
        <DataTablePro
      manual
      :page="shiftPage"
      :page-size="shiftPageSize"
      :total-items="shifts.total.value"
      @update:page="shiftPage = $event"
      @update:page-size="(v) => (shiftPageSize = String(v))" :searchable="false" :column-settings="false" :columns="columns" :rows="shiftRows" :row-key="rowKey" :loading="shifts.pending.value" empty-message="暂无班次。可清空筛选或新建班次。">
          <template #cell-active="{ row }"><StatusBadgePro :value="row.active === false ? 'disabled' : 'active'" /></template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="班次" :detail-fields="baseDetailFields(row, '班次编码', '班次名称')" :actions="shiftActions" @edit="openEditShift" />
          </template>
        </DataTablePro>
      </TabsProContent>

      <!-- 工作日历 -->
      <TabsProContent value="work-calendar" class="grid gap-3">
        <Toolbar v-model:search="calKeyword" search-placeholder="在当前页内筛选日历编码、名称">
          <template #actions>
            <DialogPro v-model:open="calOpen">
              <DialogProTrigger as-child>
                <ButtonPro size="sm" type="button" @click="openCreateCal"><PlusIcon aria-hidden="true" />新建工作日历</ButtonPro>
              </DialogProTrigger>
              <DialogProContent class="sm:max-w-lg">
                <DialogProHeader>
                  <DialogProTitle>{{ calEditingCode ? `编辑工作日历 · ${calEditingCode}` : '新建工作日历' }}</DialogProTitle>
                  <DialogProDescription>{{ calEditingCode ? '修改日历名称（编码不可修改）。工作日 / 节假日在下方月历里维护。带 * 为必填项。' : '登记一个工作日历，供工作中心与排程引用。带 * 为必填项。' }}</DialogProDescription>
                </DialogProHeader>
                <form class="grid gap-4" @submit.prevent="submitCal">
                  <p v-if="calShowErrors && !canCreateCal" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                    <FieldPro v-if="calEditingCode">
                      <FieldProLabel for="cal-code">日历编码</FieldProLabel>
                      <InputPro id="cal-code" :model-value="calForm.code" disabled />
                    </FieldPro>
                    <FieldPro :data-invalid="calShowErrors && !isNonEmpty(calForm.name)">
                      <FieldProLabel for="cal-name">日历名称 <span class="text-destructive">*</span></FieldProLabel>
                      <InputPro id="cal-name" v-model="calForm.name" autocomplete="off" required />
                      <FieldProDescription v-if="!calEditingCode">编码由系统自动生成。</FieldProDescription>
                    </FieldPro>
                  </FieldProGroup>
                  <DialogProFooter>
                    <ButtonPro type="button" variant="outline" @click="calOpen = false">取消</ButtonPro>
                    <ButtonPro type="submit" :disabled="calendars.createPending.value || calActions.updatePending.value || calEditLoading">
                      <Spinner v-if="calendars.createPending.value || calActions.updatePending.value" aria-hidden="true" />{{ calEditingCode ? '保存修改' : '保存日历' }}
                    </ButtonPro>
                  </DialogProFooter>
                </form>
              </DialogProContent>
            </DialogPro>
          </template>
        </Toolbar>
        <p v-if="calListError" class="text-sm text-destructive" role="alert">{{ calListError }}</p>

        <div class="grid items-start gap-4 md:grid-cols-[16rem_minmax(0,1fr)]">
          <!-- 左：日历列表（整行可点，选中一个驱动右侧月历）。 -->
          <div class="grid h-fit min-w-0 gap-1.5">
            <div v-if="calendars.pending.value" class="flex items-center gap-2 px-2 py-6 text-sm text-muted-foreground">
              <Spinner aria-hidden="true" />加载工作日历…
            </div>
            <p v-else-if="!calRows.length" class="rounded-md border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground">
              暂无工作日历。可清空筛选或新建日历。
            </p>
            <ul v-else class="grid gap-1">
              <li
                v-for="row in calRows"
                :key="rowKey(row)"
                class="relative"
              >
                <button
                  type="button"
                  class="flex w-full items-center gap-2 rounded-md py-2 pl-3 pr-10 text-left transition-colors hover:bg-accent/50"
                  :class="row.code === selectedCalCode ? 'bg-accent text-accent-foreground' : ''"
                  :aria-current="row.code === selectedCalCode ? 'true' : undefined"
                  @click="selectCalendar(row)"
                >
                  <span class="min-w-0 flex-1 truncate text-sm font-medium">{{ row.displayName ?? '无' }}</span>
                  <StatusBadgePro :value="row.active === false ? 'disabled' : 'active'" />
                </button>
                <!-- 行尾「⋯」操作菜单：编辑 / 停用。绝对定位避免按钮嵌套。 -->
                <div class="absolute inset-y-0 right-1 flex items-center">
                  <MasterDataRowActions :row="row" entity-label="工作日历" :detail-fields="baseDetailFields(row, '日历编码', '日历名称')" :actions="calActions" @edit="openEditCal" />
                </div>
              </li>
            </ul>
          </div>

          <!-- 右：月历视图（全部读真实 detail，无明细给空态引导） -->
          <div class="grid h-fit gap-3 rounded-lg border border-border bg-card p-4">
            <div v-if="!selectedCalCode" class="flex flex-col items-center justify-center gap-3 py-16 text-center">
              <CalendarRangeIcon class="size-8 text-muted-foreground" aria-hidden="true" />
              <p class="text-sm font-medium">先在左侧选择或新建一个工作日历</p>
              <p class="text-xs text-muted-foreground">选中后这里按工作日 / 休息日 / 节假日 / 例外日渲染月历，可直接维护。</p>
            </div>

            <template v-else>
              <!-- 头部：选中日历名 + 月份切换 + 管理入口 -->
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-semibold">{{ selectedCalName }}</span>
                  <span v-if="calBoardSaving" class="inline-flex items-center gap-1 text-xs text-muted-foreground"><Spinner aria-hidden="true" />保存中</span>
                </div>
                <div class="flex items-center gap-1">
                  <ButtonPro size="icon" variant="outline" type="button" aria-label="上个月" @click="prevMonth"><ChevronLeftIcon aria-hidden="true" /></ButtonPro>
                  <span class="min-w-20 text-center text-sm font-medium tabular-nums">{{ monthLabel }}</span>
                  <ButtonPro size="icon" variant="outline" type="button" aria-label="下个月" @click="nextMonth"><ChevronRightIcon aria-hidden="true" /></ButtonPro>
                  <ButtonPro size="sm" variant="outline" type="button" @click="goToday">今天</ButtonPro>
                </div>
              </div>

              <div v-if="calDetailLoading" class="flex items-center justify-center gap-2 py-16 text-sm text-muted-foreground">
                <Spinner aria-hidden="true" />加载日历明细…
              </div>

              <template v-else>
                <!-- 每周工作模式：月历正上方一行 7 个星期 chip，点亮=工作日，即时保存 -->
                <div class="grid gap-2">
                  <div class="flex flex-wrap items-center justify-between gap-2">
                    <p class="text-sm font-medium">每周工作模式</p>
                    <ButtonPro size="sm" variant="outline" type="button" @click="openManageSheet()">
                      <CalendarCogIcon aria-hidden="true" />管理节假日 / 例外日
                    </ButtonPro>
                  </div>
                  <div class="flex flex-wrap gap-1.5">
                    <ButtonPro
                      v-for="wd in WEEK_DAYS"
                      :key="wd.key"
                      size="sm"
                      type="button"
                      :variant="workingDaySet.has(wd.key) ? 'default' : 'outline'"
                      :disabled="calBoardSaving"
                      :aria-pressed="workingDaySet.has(wd.key)"
                      @click="toggleWeekday(wd.key)"
                    >
                      {{ wd.label }}
                    </ButtonPro>
                  </div>
                  <p class="text-xs text-muted-foreground">点亮的星期为工作日；点击切换，立即保存。每天的作息时段由「班次」定义，日历只决定哪几天开工。</p>
                </div>

                <!-- 图例（放月历上方，免下拉才能看到；色 + 文字角标双编码，色盲友好） -->
                <div class="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-muted-foreground">
                  <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-success/20 ring-1 ring-inset ring-success/40" aria-hidden="true" />工作日</span>
                  <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-muted/50 ring-1 ring-inset ring-border" aria-hidden="true" />休息日</span>
                  <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-destructive/20 ring-1 ring-inset ring-destructive/40" aria-hidden="true" /><span class="font-medium text-foreground">假</span> 节假日</span>
                  <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-warning/25 ring-1 ring-inset ring-warning/45" aria-hidden="true" /><span class="text-foreground">例外日 <span class="font-medium">班</span>上班 / <span class="font-medium">休</span>休息</span></span>
                </div>

                <!-- 月历网格（只读展示；点某天可在抽屉里预填该日期） -->
                <div class="grid grid-cols-7 gap-1">
                  <div v-for="wd in WEEK_DAYS" :key="wd.key" class="pb-1 text-center text-xs font-medium text-muted-foreground">{{ wd.short }}</div>
                  <button
                    v-for="cell in monthCells"
                    :key="cell.key"
                    type="button"
                    class="relative aspect-square rounded-md p-1.5 text-left text-sm"
                    :class="cell.inMonth ? [cellClassMap[cell.kind], cell.isToday ? 'outline outline-2 outline-primary' : '', 'transition-shadow hover:ring-1 hover:ring-inset hover:ring-primary/50'] : 'pointer-events-none opacity-0'"
                    :title="cell.inMonth ? cell.title : undefined"
                    :disabled="!cell.inMonth"
                    @click="cell.inMonth && openManageSheet(cell.key)"
                  >
                    <template v-if="cell.inMonth">
                      <span class="tabular-nums">{{ cell.label }}</span>
                      <span v-if="cell.badge" class="absolute bottom-1 right-1 text-[10px] font-semibold leading-none">{{ cell.badge }}</span>
                    </template>
                  </button>
                </div>

                <!-- 空态引导：选中日历但完全没有明细 -->
                <div v-if="calDetailLoaded && !hasAnyDetail" class="rounded-md border border-dashed border-border bg-muted/30 px-3 py-3 text-sm text-muted-foreground">
                  该日历还没有任何工作时间 / 节假日设置，当前全部按休息日显示。请在上方设置每周工作模式，或点「管理节假日 / 例外日」添加。
                </div>
              </template>
            </template>
          </div>
        </div>

        <!-- 节假日 / 例外日管理抽屉（右侧滑出） -->
        <SheetPro v-model:open="manageSheetOpen">
          <SheetProContent class="w-full gap-0 overflow-y-auto sm:max-w-md">
            <SheetProHeader>
              <SheetProTitle>节假日 / 例外日 · {{ selectedCalName }}</SheetProTitle>
              <SheetProDescription>增删后立即保存到该日历，月历会同步刷新。</SheetProDescription>
            </SheetProHeader>

            <div class="grid gap-6 px-4 pb-6">
              <!-- 节假日 -->
              <section class="grid gap-2">
                <p class="text-sm font-medium">法定节假日</p>
                <form class="grid gap-2 sm:grid-cols-[auto_1fr_auto] sm:items-end" @submit.prevent="addHoliday">
                  <div class="grid gap-1">
                    <label class="text-xs text-muted-foreground">日期</label>
                    <DatePickerPro v-model="holidayDateModel" placeholder="选择日期" class="w-40" />
                  </div>
                  <div class="grid gap-1">
                    <label for="holiday-name" class="text-xs text-muted-foreground">名称（可选）</label>
                    <InputPro id="holiday-name" v-model="holidayDraft.name" placeholder="如 端午节" />
                  </div>
                  <ButtonPro size="sm" type="submit" :disabled="!holidayDraft.date || calBoardSaving"><PlusIcon aria-hidden="true" />添加</ButtonPro>
                </form>
                <p v-if="!sortedHolidays.length" class="text-xs text-muted-foreground">暂无节假日。</p>
                <ul v-else class="grid gap-1">
                  <li v-for="h in sortedHolidays" :key="h.date" class="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-1.5 text-sm">
                    <span><span class="font-medium tabular-nums">{{ formatDate(h.date) }}</span> <span class="text-muted-foreground">{{ h.name || '节假日' }}</span></span>
                    <AlertDialogPro>
                      <AlertDialogProTrigger as-child>
                        <ButtonPro size="icon" variant="ghost" type="button" aria-label="删除节假日" :disabled="calBoardSaving"><Trash2Icon aria-hidden="true" /></ButtonPro>
                      </AlertDialogProTrigger>
                      <AlertDialogProContent>
                        <AlertDialogProHeader>
                          <AlertDialogProTitle>确定删除节假日 {{ formatDate(h.date) }}{{ h.name ? ` ${h.name}` : '' }}？</AlertDialogProTitle>
                          <AlertDialogProDescription>此操作立即生效，该日将不再标记为节假日。</AlertDialogProDescription>
                        </AlertDialogProHeader>
                        <AlertDialogProFooter>
                          <AlertDialogProCancel>取消</AlertDialogProCancel>
                          <AlertDialogProAction :disabled="calBoardSaving" @click="removeHoliday(h.date!)">确认删除</AlertDialogProAction>
                        </AlertDialogProFooter>
                      </AlertDialogProContent>
                    </AlertDialogPro>
                  </li>
                </ul>
              </section>

              <!-- 例外日 -->
              <section class="grid gap-2 border-t border-border pt-4">
                <p class="text-sm font-medium">例外日</p>
                <p class="text-xs text-muted-foreground">指定某天「当日上班 / 当日休息」，覆盖每周工作模式（如调休）。</p>
                <form class="grid gap-2" @submit.prevent="addException">
                  <div class="grid gap-2 sm:grid-cols-2">
                    <div class="grid gap-1">
                      <label class="text-xs text-muted-foreground">日期</label>
                      <DatePickerPro v-model="exceptionDateModel" placeholder="选择日期" class="w-40" />
                    </div>
                    <div class="grid gap-1">
                      <label for="exception-kind" class="text-xs text-muted-foreground">类型</label>
                      <SelectPro v-model="exceptionDraft.isWorkingDay">
                        <SelectProTrigger id="exception-kind" class="w-40"><SelectProValue /></SelectProTrigger>
                        <SelectProContent>
                          <SelectProItem value="true">当日上班</SelectProItem>
                          <SelectProItem value="false">当日休息</SelectProItem>
                        </SelectProContent>
                      </SelectPro>
                    </div>
                  </div>
                  <div class="grid gap-2 sm:grid-cols-[1fr_auto] sm:items-end">
                    <div class="grid gap-1">
                      <label for="exception-reason" class="text-xs text-muted-foreground">原因（可选）</label>
                      <InputPro id="exception-reason" v-model="exceptionDraft.reason" placeholder="如 调休" />
                    </div>
                    <ButtonPro size="sm" type="submit" :disabled="!exceptionDraft.date || calBoardSaving"><PlusIcon aria-hidden="true" />添加</ButtonPro>
                  </div>
                </form>
                <p v-if="!sortedExceptions.length" class="text-xs text-muted-foreground">暂无例外日。</p>
                <ul v-else class="grid gap-1">
                  <li v-for="e in sortedExceptions" :key="e.date" class="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-1.5 text-sm">
                    <span><span class="font-medium tabular-nums">{{ formatDate(e.date) }}</span> <span class="text-muted-foreground">{{ e.isWorkingDay ? '当日上班' : '当日休息' }}{{ e.reason ? ` · ${e.reason}` : '' }}</span></span>
                    <AlertDialogPro>
                      <AlertDialogProTrigger as-child>
                        <ButtonPro size="icon" variant="ghost" type="button" aria-label="删除例外日" :disabled="calBoardSaving"><Trash2Icon aria-hidden="true" /></ButtonPro>
                      </AlertDialogProTrigger>
                      <AlertDialogProContent>
                        <AlertDialogProHeader>
                          <AlertDialogProTitle>确定删除例外日 {{ formatDate(e.date) }}（{{ e.isWorkingDay ? '当日上班' : '当日休息' }}）？</AlertDialogProTitle>
                          <AlertDialogProDescription>此操作立即生效，该日将恢复按每周工作模式判定。</AlertDialogProDescription>
                        </AlertDialogProHeader>
                        <AlertDialogProFooter>
                          <AlertDialogProCancel>取消</AlertDialogProCancel>
                          <AlertDialogProAction :disabled="calBoardSaving" @click="removeException(e.date!)">确认删除</AlertDialogProAction>
                        </AlertDialogProFooter>
                      </AlertDialogProContent>
                    </AlertDialogPro>
                  </li>
                </ul>
              </section>
            </div>
          </SheetProContent>
        </SheetPro>

        <!-- 节假日 / 例外日互斥冲突确认（受控） -->
        <AlertDialogPro v-model:open="conflict.open">
          <AlertDialogProContent>
            <AlertDialogProHeader>
              <AlertDialogProTitle>{{ conflictTitle }}</AlertDialogProTitle>
              <AlertDialogProDescription>{{ conflictDescription }}</AlertDialogProDescription>
            </AlertDialogProHeader>
            <AlertDialogProFooter>
              <AlertDialogProCancel @click="cancelConflict">取消</AlertDialogProCancel>
              <AlertDialogProAction :disabled="calBoardSaving" @click="resolveConflict">确认替换</AlertDialogProAction>
            </AlertDialogProFooter>
          </AlertDialogProContent>
        </AlertDialogPro>
      </TabsProContent>
    </TabsPro>
  </BusinessLayout>
</template>
