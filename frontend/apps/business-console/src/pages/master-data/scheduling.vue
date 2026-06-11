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
import type { DataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { useMasterDataResource, useMasterDataResourceActions } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  Button,
  DataTable,
  DataTablePagination,
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
  Spinner,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Toolbar,
} from '@nerv-iip/ui'
import { CalendarRangeIcon, ChevronLeftIcon, ChevronRightIcon, PlusIcon, RefreshCwIcon, Trash2Icon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '排班与日历' } })

const shifts = useMasterDataResource<BusinessConsoleCreateShiftRequest>('shift')
const calendars = useMasterDataResource<BusinessConsoleCreateWorkCalendarRequest>('work-calendar')
const shiftActions = useMasterDataResourceActions('shift')
const calActions = useMasterDataResourceActions('work-calendar')

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
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
const canCreateShift = computed(() => [shiftForm.code, shiftForm.name].every(isNonEmpty) && shiftPaidValid.value)
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
      code: shiftForm.code.trim(),
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
const calPage = ref(1)
const calPageSize = ref('10')
const calOpen = ref(false)
const calShowErrors = ref(false)
const calEditingCode = shallowRef<string | null>(null)
const calEditLoading = shallowRef(false)
const calForm = reactive({ code: '', name: '' })
const calRows = computed(() => filterRows(calendars.items.value, calKeyword.value))
const canCreateCal = computed(() => [calForm.code, calForm.name].every(isNonEmpty))
const calListError = computed(() => formatError(calendars.error.value))
watch(calOpen, (open) => { if (open) calShowErrors.value = false })
watch([calKeyword, calPageSize], () => { calPage.value = 1 })
watch([calPage, calPageSize], () => {
  calendars.filters.skip = (calPage.value - 1) * (Number(calPageSize.value) || 10)
  calendars.filters.take = Number(calPageSize.value) || 10
}, { immediate: true })
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
        code: calForm.code.trim(),
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

// 左侧日历选择列表只显示「名称 ｜ 状态」两列（窄栏）。
const calListColumns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'displayName', header: '工作日历', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-20' },
  { key: 'actions', header: '', align: 'end', width: 'w-12' },
]

const selectedCalCode = shallowRef<string | null>(null)
const calDetailLoading = shallowRef(false)
const calDetailLoaded = shallowRef(false)
// 选中日历的真实明细（全部读自 fetchDetail，不画假数据）。
const workingTimes = ref<BusinessConsoleWorkCalendarWorkingTime[]>([])
const holidays = ref<BusinessConsoleWorkCalendarHoliday[]>([])
const exceptions = ref<BusinessConsoleWorkCalendarException[]>([])
const calBoardSaving = shallowRef(false)

const today = new Date()
const viewYear = ref(today.getFullYear())
const viewMonth = ref(today.getMonth()) // 0-based
const monthLabel = computed(() => `${viewYear.value}-${String(viewMonth.value + 1).padStart(2, '0')}`)

// 该日历配置了工作时段的星期集合（有 workingTime 即为工作日）。
const workingDaySet = computed(() => {
  const set = new Set<SystemDayOfWeek>()
  for (const wt of workingTimes.value) if (wt.dayOfWeek) set.add(wt.dayOfWeek)
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
      badge = '例'
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
const cellClassMap: Record<DayCell['kind'], string> = {
  'working': 'bg-card text-card-foreground',
  'rest': 'bg-muted/60 text-muted-foreground',
  'holiday': 'bg-destructive/15 text-destructive ring-1 ring-inset ring-destructive/40',
  'exception-working': 'bg-primary/15 text-primary ring-1 ring-inset ring-primary/40',
  'exception-rest': 'bg-accent text-accent-foreground ring-1 ring-inset ring-border',
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

const selectedCalName = computed(() =>
  calendars.items.value.find((c) => c.code === selectedCalCode.value)?.displayName ?? selectedCalCode.value ?? '',
)

// 写回：把当前编辑后的 workingTimes/holidays/exceptions 经 update 提交。
async function persistCalendar(successMsg: string) {
  if (!selectedCalCode.value) return
  calBoardSaving.value = true
  try {
    await calActions.update(selectedCalCode.value, {
      workingTimes: workingTimes.value,
      holidays: holidays.value,
      exceptions: exceptions.value,
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
    workingTimes.value = workingTimes.value.filter((w) => w.dayOfWeek !== day)
  }
  else {
    workingTimes.value = [...workingTimes.value, { dayOfWeek: day, startsAt: '08:00:00', endsAt: '17:00:00' }]
  }
  await persistCalendar('每周工作模式已更新。')
}

// 加 / 删 节假日。
const holidayDraft = reactive({ date: '', name: '' })
async function addHoliday() {
  if (!selectedCalCode.value || !holidayDraft.date) return
  const key = holidayDraft.date
  if (holidayMap.value.has(key)) {
    notifyError(new Error('该日期已是节假日。'))
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
  exceptions.value = [...exceptions.value, {
    date: key,
    isWorkingDay: exceptionDraft.isWorkingDay === 'true',
    reason: exceptionDraft.reason.trim() || null,
  }]
  exceptionDraft.date = ''
  exceptionDraft.reason = ''
  await persistCalendar('例外日已添加。')
}
async function removeException(date: string) {
  if (!selectedCalCode.value) return
  const key = toDateKey(date)
  exceptions.value = exceptions.value.filter((e) => toDateKey(e.date ?? '') !== key)
  await persistCalendar('例外日已删除。')
}

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
        <Button size="sm" variant="outline" type="button" :disabled="shifts.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      班次定义作息时段与计薪时长；工作日历定义工作日 / 休息日，驱动工作中心可用产能。
    </p>

    <Tabs default-value="shift">
      <TabsList>
        <TabsTrigger value="shift">班次 ({{ shifts.total.value }})</TabsTrigger>
        <TabsTrigger value="work-calendar">工作日历 ({{ calendars.total.value }})</TabsTrigger>
      </TabsList>

      <!-- 班次 -->
      <TabsContent value="shift" class="grid gap-3">
        <Toolbar v-model:search="shiftKeyword" search-placeholder="在当前页内筛选班次编码、名称">
          <template #actions>
            <Dialog v-model:open="shiftOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateShift"><PlusIcon aria-hidden="true" />新建班次</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ shiftEditingCode ? `编辑班次 · ${shiftEditingCode}` : '新建班次' }}</DialogTitle>
                  <DialogDescription>{{ shiftEditingCode ? '可修改名称、起止时间与计薪时长（编码不可修改）。带 * 为必填项。' : '定义一个排班时段及计薪时长。带 * 为必填项。' }}</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitShift">
                  <p v-if="shiftShowErrors && !shiftFormValid" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="shiftShowErrors && !isNonEmpty(shiftForm.code)">
                      <FieldLabel for="shift-code">班次编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-code" v-model="shiftForm.code" autocomplete="off" :disabled="!!shiftEditingCode" required />
                    </Field>
                    <Field :data-invalid="shiftShowErrors && !isNonEmpty(shiftForm.name)">
                      <FieldLabel for="shift-name">班次名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-name" v-model="shiftForm.name" autocomplete="off" required />
                    </Field>
                    <Field>
                      <FieldLabel for="shift-start">开始时间</FieldLabel>
                      <Input id="shift-start" v-model="shiftForm.startsAt" type="time" />
                    </Field>
                    <Field>
                      <FieldLabel for="shift-end">结束时间</FieldLabel>
                      <Input id="shift-end" v-model="shiftForm.endsAt" type="time" />
                      <FieldDescription v-if="shiftCrossesMidnight">结束早于开始，按跨天班次处理。</FieldDescription>
                    </Field>
                    <Field :data-invalid="shiftShowErrors && !shiftPaidValid">
                      <FieldLabel for="shift-paid">计薪时长（分钟） <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-paid" v-model="shiftForm.paidMinutes" type="number" min="1" inputmode="numeric" />
                      <FieldDescription>扣除休息后的有效计薪分钟数，默认 480（8 小时）。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="shiftOpen = false">取消</Button>
                    <Button type="submit" :disabled="shifts.createPending.value || shiftActions.updatePending.value || shiftEditLoading">
                      <Spinner v-if="shifts.createPending.value || shiftActions.updatePending.value" aria-hidden="true" />{{ shiftEditingCode ? '保存修改' : '保存班次' }}
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="shiftListError" class="text-sm text-destructive" role="alert">{{ shiftListError }}</p>
        <DataTable :columns="columns" :rows="shiftRows" :row-key="rowKey" :loading="shifts.pending.value" empty-message="暂无班次。可清空筛选或新建班次。">
          <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="班次" :detail-fields="baseDetailFields(row, '班次编码', '班次名称')" :actions="shiftActions" @edit="openEditShift" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="shiftPage" v-model:page-size="shiftPageSize" :total-items="shifts.total.value" />
      </TabsContent>

      <!-- 工作日历 -->
      <TabsContent value="work-calendar" class="grid gap-3">
        <Toolbar v-model:search="calKeyword" search-placeholder="在当前页内筛选日历编码、名称">
          <template #actions>
            <Dialog v-model:open="calOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateCal"><PlusIcon aria-hidden="true" />新建工作日历</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ calEditingCode ? `编辑工作日历 · ${calEditingCode}` : '新建工作日历' }}</DialogTitle>
                  <DialogDescription>{{ calEditingCode ? '修改日历名称（编码不可修改）。工作日 / 节假日在下方月历里维护。带 * 为必填项。' : '登记一个工作日历，供工作中心与排程引用。带 * 为必填项。' }}</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitCal">
                  <p v-if="calShowErrors && !canCreateCal" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="calShowErrors && !isNonEmpty(calForm.code)">
                      <FieldLabel for="cal-code">日历编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="cal-code" v-model="calForm.code" autocomplete="off" :disabled="!!calEditingCode" required />
                    </Field>
                    <Field :data-invalid="calShowErrors && !isNonEmpty(calForm.name)">
                      <FieldLabel for="cal-name">日历名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="cal-name" v-model="calForm.name" autocomplete="off" required />
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="calOpen = false">取消</Button>
                    <Button type="submit" :disabled="calendars.createPending.value || calActions.updatePending.value || calEditLoading">
                      <Spinner v-if="calendars.createPending.value || calActions.updatePending.value" aria-hidden="true" />{{ calEditingCode ? '保存修改' : '保存日历' }}
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="calListError" class="text-sm text-destructive" role="alert">{{ calListError }}</p>

        <div class="grid gap-4 md:grid-cols-[260px_minmax(0,1fr)]">
          <!-- 左：日历列表（选中一个驱动右侧月历） -->
          <div class="grid h-fit gap-3">
            <DataTable
              :columns="calListColumns"
              :rows="calRows"
              :row-key="rowKey"
              :loading="calendars.pending.value"
              empty-message="暂无工作日历。可清空筛选或新建日历。"
            >
              <template #cell-displayName="{ row }">
                <button
                  type="button"
                  class="rounded text-left font-medium hover:underline"
                  :class="row.code === selectedCalCode ? 'text-primary' : ''"
                  :aria-current="row.code === selectedCalCode ? 'true' : undefined"
                  @click="selectCalendar(row)"
                >{{ row.displayName ?? '无' }}</button>
              </template>
              <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
              <template #cell-actions="{ row }">
                <MasterDataRowActions :row="row" entity-label="工作日历" :detail-fields="baseDetailFields(row, '日历编码', '日历名称')" :actions="calActions" @edit="openEditCal" />
              </template>
            </DataTable>
            <DataTablePagination v-model:page="calPage" v-model:page-size="calPageSize" :total-items="calendars.total.value" />
          </div>

          <!-- 右：月历视图（全部读真实 detail，无明细给空态引导） -->
          <div class="grid h-fit gap-3 rounded-lg border border-border bg-card p-4">
            <div v-if="!selectedCalCode" class="flex flex-col items-center justify-center gap-3 py-16 text-center">
              <CalendarRangeIcon class="size-8 text-muted-foreground" aria-hidden="true" />
              <p class="text-sm font-medium">先在左侧选择或新建一个工作日历</p>
              <p class="text-xs text-muted-foreground">选中后这里按工作日 / 休息日 / 节假日 / 例外日渲染月历，可直接维护。</p>
            </div>

            <template v-else>
              <!-- 头部：月份切换 + 选中日历名 -->
              <div class="flex flex-wrap items-center justify-between gap-2">
                <div class="flex items-center gap-2">
                  <span class="text-sm font-semibold">{{ selectedCalName }}</span>
                  <span v-if="calBoardSaving" class="inline-flex items-center gap-1 text-xs text-muted-foreground"><Spinner aria-hidden="true" />保存中</span>
                </div>
                <div class="flex items-center gap-1">
                  <Button size="icon" variant="outline" type="button" aria-label="上个月" @click="prevMonth"><ChevronLeftIcon aria-hidden="true" /></Button>
                  <span class="min-w-20 text-center text-sm font-medium tabular-nums">{{ monthLabel }}</span>
                  <Button size="icon" variant="outline" type="button" aria-label="下个月" @click="nextMonth"><ChevronRightIcon aria-hidden="true" /></Button>
                  <Button size="sm" variant="outline" type="button" @click="goToday">今天</Button>
                </div>
              </div>

              <!-- 图例（色 + 文字/角标双编码，色盲友好） -->
              <div class="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs text-muted-foreground">
                <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm border border-border bg-card" aria-hidden="true" />工作日</span>
                <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-muted/60" aria-hidden="true" />休息日</span>
                <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-destructive/15 ring-1 ring-inset ring-destructive/40" aria-hidden="true" /><span class="font-medium">假</span> 法定节假日</span>
                <span class="inline-flex items-center gap-1.5"><span class="size-3 rounded-sm bg-primary/15 ring-1 ring-inset ring-primary/40" aria-hidden="true" /><span class="font-medium">例</span> 例外日</span>
              </div>

              <div v-if="calDetailLoading" class="flex items-center justify-center gap-2 py-16 text-sm text-muted-foreground">
                <Spinner aria-hidden="true" />加载日历明细…
              </div>

              <template v-else>
                <!-- 月历网格 -->
                <div class="grid grid-cols-7 gap-1">
                  <div v-for="wd in WEEK_DAYS" :key="wd.key" class="pb-1 text-center text-xs font-medium text-muted-foreground">{{ wd.short }}</div>
                  <div
                    v-for="cell in monthCells"
                    :key="cell.key"
                    class="relative aspect-square rounded-md p-1.5 text-sm"
                    :class="cell.inMonth ? [cellClassMap[cell.kind], cell.isToday ? 'outline outline-2 outline-primary' : ''] : 'opacity-0'"
                    :title="cell.inMonth ? cell.title : undefined"
                  >
                    <template v-if="cell.inMonth">
                      <span class="tabular-nums">{{ cell.label }}</span>
                      <span v-if="cell.badge" class="absolute bottom-1 right-1 text-[10px] font-semibold leading-none">{{ cell.badge }}</span>
                    </template>
                  </div>
                </div>

                <!-- 空态引导：选中日历但完全没有明细 -->
                <div v-if="calDetailLoaded && !hasAnyDetail" class="rounded-md border border-dashed border-border bg-muted/30 px-3 py-3 text-sm text-muted-foreground">
                  该日历还没有任何工作时间 / 节假日设置，当前全部按休息日显示。请在下方设置每周工作模式、添加节假日或例外日。
                </div>

                <!-- 编辑：每周工作模式 -->
                <div class="grid gap-2 border-t border-border pt-3">
                  <p class="text-sm font-medium">每周工作模式</p>
                  <p class="text-xs text-muted-foreground">点亮的星期为工作日（默认 08:00–17:00）；点击切换，立即保存。</p>
                  <div class="flex flex-wrap gap-2">
                    <Button
                      v-for="wd in WEEK_DAYS"
                      :key="wd.key"
                      size="sm"
                      type="button"
                      :variant="workingDaySet.has(wd.key) ? 'default' : 'outline'"
                      :disabled="calBoardSaving"
                      @click="toggleWeekday(wd.key)"
                    >
                      {{ wd.label }}
                    </Button>
                  </div>
                </div>

                <!-- 编辑：节假日 -->
                <div class="grid gap-2 border-t border-border pt-3">
                  <p class="text-sm font-medium">法定节假日</p>
                  <form class="flex flex-wrap items-end gap-2" @submit.prevent="addHoliday">
                    <div class="grid gap-1">
                      <label for="holiday-date" class="text-xs text-muted-foreground">日期</label>
                      <Input id="holiday-date" v-model="holidayDraft.date" type="date" class="w-40" />
                    </div>
                    <div class="grid gap-1">
                      <label for="holiday-name" class="text-xs text-muted-foreground">名称（可选）</label>
                      <Input id="holiday-name" v-model="holidayDraft.name" placeholder="如 端午节" class="w-40" />
                    </div>
                    <Button size="sm" type="submit" :disabled="!holidayDraft.date || calBoardSaving"><PlusIcon aria-hidden="true" />添加</Button>
                  </form>
                  <p v-if="!sortedHolidays.length" class="text-xs text-muted-foreground">暂无节假日。</p>
                  <ul v-else class="grid gap-1">
                    <li v-for="h in sortedHolidays" :key="h.date" class="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-1.5 text-sm">
                      <span><span class="font-medium tabular-nums">{{ formatDate(h.date) }}</span> <span class="text-muted-foreground">{{ h.name || '节假日' }}</span></span>
                      <Button size="icon" variant="ghost" type="button" aria-label="删除节假日" :disabled="calBoardSaving" @click="removeHoliday(h.date!)"><Trash2Icon aria-hidden="true" /></Button>
                    </li>
                  </ul>
                </div>

                <!-- 编辑：例外日 -->
                <div class="grid gap-2 border-t border-border pt-3">
                  <p class="text-sm font-medium">例外日</p>
                  <p class="text-xs text-muted-foreground">指定某天「当日上班 / 当日休息」，覆盖每周工作模式（如调休）。</p>
                  <form class="flex flex-wrap items-end gap-2" @submit.prevent="addException">
                    <div class="grid gap-1">
                      <label for="exception-date" class="text-xs text-muted-foreground">日期</label>
                      <Input id="exception-date" v-model="exceptionDraft.date" type="date" class="w-40" />
                    </div>
                    <div class="grid gap-1">
                      <label for="exception-kind" class="text-xs text-muted-foreground">类型</label>
                      <select id="exception-kind" v-model="exceptionDraft.isWorkingDay" class="h-9 rounded-md border border-input bg-transparent px-3 text-sm">
                        <option value="true">当日上班</option>
                        <option value="false">当日休息</option>
                      </select>
                    </div>
                    <div class="grid gap-1">
                      <label for="exception-reason" class="text-xs text-muted-foreground">原因（可选）</label>
                      <Input id="exception-reason" v-model="exceptionDraft.reason" placeholder="如 调休" class="w-40" />
                    </div>
                    <Button size="sm" type="submit" :disabled="!exceptionDraft.date || calBoardSaving"><PlusIcon aria-hidden="true" />添加</Button>
                  </form>
                  <p v-if="!sortedExceptions.length" class="text-xs text-muted-foreground">暂无例外日。</p>
                  <ul v-else class="grid gap-1">
                    <li v-for="e in sortedExceptions" :key="e.date" class="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-1.5 text-sm">
                      <span><span class="font-medium tabular-nums">{{ formatDate(e.date) }}</span> <span class="text-muted-foreground">{{ e.isWorkingDay ? '当日上班' : '当日休息' }}{{ e.reason ? ` · ${e.reason}` : '' }}</span></span>
                      <Button size="icon" variant="ghost" type="button" aria-label="删除例外日" :disabled="calBoardSaving" @click="removeException(e.date!)"><Trash2Icon aria-hidden="true" /></Button>
                    </li>
                  </ul>
                </div>
              </template>
            </template>
          </div>
        </div>
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
