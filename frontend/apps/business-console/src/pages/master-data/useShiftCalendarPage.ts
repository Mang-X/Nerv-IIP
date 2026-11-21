import type {
  BusinessConsoleCreateShiftRequest,
  BusinessConsoleCreateWorkCalendarRequest,
  BusinessConsoleResourceItem,
  BusinessConsoleWorkCalendarException,
  BusinessConsoleWorkCalendarHoliday,
  BusinessConsoleWorkCalendarWorkingTime,
  SystemDayOfWeek,
} from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import { useIncludeDisabledFilter } from '@/composables/master-data/masterDataIncludeDisabled'
import { useMasterDataLifecycleConfirm } from '@/composables/master-data/masterDataLifecycleConfirm'
import { useMasterDataResource, useMasterDataResourceActions } from '@/composables/master-data'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDate, formatDateTime } from '@/utils/format'
import {
  inlineErrorMessage,
  notifyError,
  notifyOperationFailure,
  notifySuccess,
} from '@/utils/notify'

export function useShiftCalendarPage() {
  const shifts = useMasterDataResource<BusinessConsoleCreateShiftRequest>('shift')
  const calendars = useMasterDataResource<BusinessConsoleCreateWorkCalendarRequest>('work-calendar')
  const shiftActions = useMasterDataResourceActions('shift')
  const calActions = useMasterDataResourceActions('work-calendar')
  // 停用/启用确认框收在页面层单实例，行操作只负责指向当前行（#1591）。
  const lifecycle = useMasterDataLifecycleConfirm()

  const columns: NvDataTableColumn<BusinessConsoleResourceItem>[] = [
    { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
    { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
    { key: 'active', header: '状态', width: 'w-24' },
    {
      key: 'snapshotVersion',
      header: '更新时间',
      width: 'w-40',
      accessor: (r) => formatDateTime(r.snapshotVersion),
    },
    { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
  ]
  function baseDetailFields(
    row: BusinessConsoleResourceItem,
    codeLabel: string,
    nameLabel: string,
  ) {
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
  function filterRows(items: BusinessConsoleResourceItem[], keyword: string) {
    const kw = keyword.trim().toLowerCase()
    if (!kw) return items
    return items.filter((row) =>
      [row.code, row.displayName, row.snapshotVersion].some((value) =>
        (value ?? '').toLowerCase().includes(kw),
      ),
    )
  }
  function refreshAll() {
    void shifts.refresh()
    void calendars.refresh()
  }

  // ---- 班次 ----
  const shiftKeyword = ref('')
  const shiftPage = ref(1)
  // 停用后的行默认不在列表里，「启用」入口就永远够不到；班次与日历共用一个开关（#1594）。
  const includeDisabled = useIncludeDisabledFilter([shifts.filters, calendars.filters], () => {
    shiftPage.value = 1
  })
  const shiftPageSize = ref('10')
  // 班次弹窗：每次打开递增作 key，拿到全新实例（按 editing 初始化表单）；0 表示还没打开过。
  const shiftOpen = shallowRef(false)
  const shiftSession = shallowRef(0)
  const shiftEditing = shallowRef<BusinessConsoleResourceItem>()
  const shiftRows = computed(() => filterRows(shifts.items.value, shiftKeyword.value))
  const shiftListError = computed(() => inlineErrorMessage(shifts.error.value))
  watch([shiftKeyword, shiftPageSize], () => {
    shiftPage.value = 1
  })
  watch(
    [shiftPage, shiftPageSize],
    () => {
      shifts.filters.skip = (shiftPage.value - 1) * (Number(shiftPageSize.value) || 10)
      shifts.filters.take = Number(shiftPageSize.value) || 10
    },
    { immediate: true },
  )
  function openShiftForm(row?: BusinessConsoleResourceItem) {
    shiftEditing.value = row
    shiftSession.value += 1
    shiftOpen.value = true
  }
  function openEditShift(row: BusinessConsoleResourceItem) {
    if (!row.code) return
    openShiftForm(row)
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
  const calListError = computed(() => inlineErrorMessage(calendars.error.value))
  watch(calOpen, (open) => {
    if (open) calShowErrors.value = false
  })
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
    } finally {
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
      } else {
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
    } catch (error) {
      notifyOperationFailure('保存工作日历失败', error, '保存工作日历失败，请稍后重试。')
    }
  }

  // ---- 工作日历月历视图（读真实明细 + 编辑写回） ----
  const WEEK_DAYS: { key: SystemDayOfWeek; label: string; short: string }[] = [
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
  const monthLabel = computed(
    () => `${viewYear.value}-${String(viewMonth.value + 1).padStart(2, '0')}`,
  )

  // 该日历配置了工作时段的星期集合（有 workingTime 即为工作日）。
  const workingDaySet = computed(() => {
    const set = new Set<SystemDayOfWeek>()
    for (const wt of workingTimes.value) {
      const k = normalizeDow(wt.dayOfWeek)
      if (k) set.add(k)
    }
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
  const hasAnyDetail = computed(
    () => workingTimes.value.length > 0 || holidays.value.length > 0 || exceptions.value.length > 0,
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
      } else if (exception) {
        kind = exception.isWorkingDay ? 'exception-working' : 'exception-rest'
        badge = exception.isWorkingDay ? '班' : '休'
        title = `例外日${exception.reason ? `：${exception.reason}` : ''}（${exception.isWorkingDay ? '当日上班' : '当日休息'}）`
      } else if (baseWorking) {
        kind = 'working'
        title = '工作日'
      } else {
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
    return {
      key,
      day: 0,
      inMonth: false,
      dow: 'sunday',
      kind: 'rest',
      label: '',
      badge: '',
      title: '',
      isToday: false,
    }
  }

  // 月历格底色（语义 token，跟随主题；色盲友好靠角标 + title 文字双编码）。
  // 4 类语义色,均为主题 token(随主题/明暗自适应):工作日=绿(明确可见,修隐形)、休息日=灰(下沉)、
  // 节假日=红、例外日=琥珀(班/休 靠角标区分)。
  const cellClassMap: Record<DayCell['kind'], string> = {
    working: 'bg-success/15 text-foreground ring-1 ring-inset ring-success/35',
    rest: 'bg-muted/50 text-muted-foreground',
    holiday: 'bg-destructive/15 text-destructive ring-1 ring-inset ring-destructive/40',
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
    } catch (error) {
      notifyError(error, '加载工作日历明细失败，请稍后重试。')
    } finally {
      calDetailLoading.value = false
    }
  }
  function prevMonth() {
    if (viewMonth.value === 0) {
      viewMonth.value = 11
      viewYear.value--
    } else viewMonth.value--
  }
  function nextMonth() {
    if (viewMonth.value === 11) {
      viewMonth.value = 0
      viewYear.value++
    } else viewMonth.value++
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

  const selectedCalName = computed(
    () =>
      calendars.items.value.find((c) => c.code === selectedCalCode.value)?.displayName ??
      selectedCalCode.value ??
      '',
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
  // 返回是否写回成功——删除/替换确认框据此决定「关框」还是「保留原地重试」（confirm-destroy 规则 3）。
  async function persistCalendar(successMsg: string) {
    if (!selectedCalCode.value) return false
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
      return true
    } catch (error) {
      notifyOperationFailure('保存工作日历失败', error, '保存工作日历失败，请稍后重试。')
      return false
    } finally {
      calBoardSaving.value = false
    }
  }

  // 设每周工作模式：切换某星期是否为工作日（默认时段 08:00–17:00）。
  async function toggleWeekday(day: SystemDayOfWeek) {
    if (!selectedCalCode.value) return
    // 快照必须在改本地**之前**取整份 workingTimes：persistCalendar 会在 try 之前就把集合就地写成
    // 去重结果，而回滚只能还原整份快照——反向补一条 { dayOfWeek } 会把该星期原有的多条时段抹平。
    const snapshot = workingTimes.value
    const exists = workingDaySet.value.has(day)
    if (exists) {
      workingTimes.value = workingTimes.value.filter((w) => normalizeDow(w.dayOfWeek) !== day)
    }
    // 仅当该星期尚无工作时段时才追加,避免重复添加同一 dayOfWeek。
    else if (!workingTimes.value.some((w) => normalizeDow(w.dayOfWeek) === day)) {
      workingTimes.value = [...workingTimes.value, { dayOfWeek: day }]
    }
    const ok = await persistCalendar('每周工作模式已更新。')
    if (!ok) workingTimes.value = snapshot
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
    if (conflict.kind === 'holiday')
      return '节假日与例外日不能并存。确认后将删除该日期的例外日，并将其设为节假日。'
    if (conflict.kind === 'exception')
      return '节假日与例外日不能并存。确认后将删除该日期的节假日，并将其设为例外日。'
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
      Object.assign(conflict, {
        open: true,
        kind: 'holiday',
        date: key,
        name: holidayDraft.name.trim(),
      })
      return
    }
    // 写回失败要回滚并保留草稿：否则界面多出一条服务端没有的幻影节假日，草稿又被清空，连原地重试都做不到。
    const snapshot = holidays.value
    holidays.value = [...holidays.value, { date: key, name: holidayDraft.name.trim() || '节假日' }]
    const ok = await persistCalendar('节假日已添加。')
    if (!ok) {
      holidays.value = snapshot
      return
    }
    holidayDraft.date = ''
    holidayDraft.name = ''
  }
  async function removeHoliday(date: string) {
    if (!selectedCalCode.value) return false
    const key = toDateKey(date)
    // 写回失败要回滚：否则界面显示已删、服务端仍在，是静默的数据不一致。
    const snapshot = holidays.value
    holidays.value = holidays.value.filter((h) => toDateKey(h.date ?? '') !== key)
    const ok = await persistCalendar('节假日已删除。')
    if (!ok) holidays.value = snapshot
    return ok
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
    // 同 addHoliday：失败回滚集合并保留草稿，成功后才清草稿。
    const snapshot = exceptions.value
    exceptions.value = [
      ...exceptions.value,
      {
        date: key,
        isWorkingDay: exceptionDraft.isWorkingDay === 'true',
        reason: exceptionDraft.reason.trim() || null,
      },
    ]
    const ok = await persistCalendar('例外日已添加。')
    if (!ok) {
      exceptions.value = snapshot
      return
    }
    exceptionDraft.date = ''
    exceptionDraft.reason = ''
  }

  // 确认替换：删除另一类型在该日期的项，写入当前类型，单次持久化。
  async function resolveConflict() {
    const key = conflict.date
    if (!selectedCalCode.value || !key || !conflict.kind) {
      conflict.open = false
      conflict.kind = ''
      return
    }
    if (calBoardSaving.value) return
    // 失败要回滚并保留草稿与确认框：确认按钮是普通 NvButton，关框由成功路径决定。
    const holidaySnapshot = holidays.value
    const exceptionSnapshot = exceptions.value
    let ok = false
    if (conflict.kind === 'holiday') {
      // 删该日例外日，加节假日。
      exceptions.value = exceptions.value.filter((e) => toDateKey(e.date ?? '') !== key)
      holidays.value = [...holidays.value, { date: key, name: conflict.name || '节假日' }]
      ok = await persistCalendar('已替换为节假日（原例外日已删除）。')
      if (ok) {
        holidayDraft.date = ''
        holidayDraft.name = ''
      }
    } else if (conflict.kind === 'exception') {
      // 删该日节假日，加例外日。
      holidays.value = holidays.value.filter((h) => toDateKey(h.date ?? '') !== key)
      exceptions.value = [
        ...exceptions.value,
        { date: key, isWorkingDay: conflict.isWorkingDay, reason: conflict.reason },
      ]
      ok = await persistCalendar('已替换为例外日（原节假日已删除）。')
      if (ok) {
        exceptionDraft.date = ''
        exceptionDraft.reason = ''
      }
    }
    if (!ok) {
      holidays.value = holidaySnapshot
      exceptions.value = exceptionSnapshot
      return
    }
    conflict.open = false
    conflict.kind = ''
  }
  function cancelConflict() {
    conflict.open = false
    conflict.kind = ''
  }
  async function removeException(date: string) {
    if (!selectedCalCode.value) return false
    const key = toDateKey(date)
    const snapshot = exceptions.value
    exceptions.value = exceptions.value.filter((e) => toDateKey(e.date ?? '') !== key)
    const ok = await persistCalendar('例外日已删除。')
    if (!ok) exceptions.value = snapshot
    return ok
  }

  // 删除确认：节假日与例外日共用**页面层单实例**确认框（confirm-destroy 规则 5，#1608）。
  // 两类目标共用一个 deleteTarget，必须靠 kind 判别——删错类型是静默的数据损失，不会报错。
  type CalendarDeleteTarget =
    | { kind: 'holiday'; date: string; name: string }
    | { kind: 'exception'; date: string; isWorkingDay: boolean }
  const deleteConfirmOpen = ref(false)
  const deleteTarget = shallowRef<CalendarDeleteTarget | null>(null)

  function openDeleteHoliday(holiday: BusinessConsoleWorkCalendarHoliday) {
    if (!holiday.date) return
    deleteTarget.value = { kind: 'holiday', date: holiday.date, name: holiday.name ?? '' }
    deleteConfirmOpen.value = true
  }
  function openDeleteException(exception: BusinessConsoleWorkCalendarException) {
    if (!exception.date) return
    deleteTarget.value = {
      kind: 'exception',
      date: exception.date,
      isWorkingDay: exception.isWorkingDay ?? false,
    }
    deleteConfirmOpen.value = true
  }
  const deleteConfirmTitle = computed(() => {
    const target = deleteTarget.value
    if (!target) return ''
    if (target.kind === 'holiday')
      return `确定删除节假日 ${formatDate(target.date)}${target.name ? ` ${target.name}` : ''}？`
    return `确定删除例外日 ${formatDate(target.date)}（${target.isWorkingDay ? '当日上班' : '当日休息'}）？`
  })
  const deleteConfirmDescription = computed(() => {
    const target = deleteTarget.value
    if (!target) return ''
    return target.kind === 'holiday' ? '该日将不再标记为节假日。' : '该日将恢复按每周工作模式判定。'
  })
  // 成功才关框：失败时留在原地可重试（确认按钮是普通 NvButton，不是点击即关的 NvAlertDialogAction）。
  async function confirmDelete() {
    const target = deleteTarget.value
    if (!target || calBoardSaving.value) return
    const ok =
      target.kind === 'holiday'
        ? await removeHoliday(target.date)
        : await removeException(target.date)
    if (!ok) return
    deleteConfirmOpen.value = false
    deleteTarget.value = null
  }

  // DatePicker 的 modelValue 是 'YYYY-MM-DD' | null；草稿仍用字符串，故清空(null)归一为 ''。
  const holidayDateModel = computed<string | null>({
    get: () => holidayDraft.date || null,
    set: (value) => {
      holidayDraft.date = value ?? ''
    },
  })
  const exceptionDateModel = computed<string | null>({
    get: () => exceptionDraft.date || null,
    set: (value) => {
      exceptionDraft.date = value ?? ''
    },
  })

  const sortedHolidays = computed(() =>
    [...holidays.value]
      .filter((h) => h.date)
      .sort((a, b) => (a.date ?? '').localeCompare(b.date ?? '')),
  )
  const sortedExceptions = computed(() =>
    [...exceptions.value]
      .filter((e) => e.date)
      .sort((a, b) => (a.date ?? '').localeCompare(b.date ?? '')),
  )

  return {
    WEEK_DAYS,
    addException,
    addHoliday,
    baseDetailFields,
    calActions,
    calBoardSaving,
    calDetailLoaded,
    calDetailLoading,
    calEditLoading,
    calEditingCode,
    calForm,
    calKeyword,
    calListError,
    calOpen,
    calRows,
    calShowErrors,
    calendars,
    canCreateCal,
    cancelConflict,
    cellClassMap,
    columns,
    confirmDelete,
    conflict,
    conflictDescription,
    conflictTitle,
    deleteConfirmDescription,
    deleteConfirmOpen,
    deleteConfirmTitle,
    exceptionDateModel,
    exceptionDraft,
    goToday,
    hasAnyDetail,
    holidayDateModel,
    holidayDraft,
    includeDisabled,
    isNonEmpty,
    lifecycle,
    manageSheetOpen,
    monthCells,
    monthLabel,
    nextMonth,
    openCreateCal,
    openDeleteException,
    openDeleteHoliday,
    openEditCal,
    openEditShift,
    openManageSheet,
    openShiftForm,
    prevMonth,
    refreshAll,
    resolveConflict,
    rowKey,
    selectCalendar,
    selectedCalCode,
    selectedCalName,
    shiftActions,
    shiftEditing,
    shiftKeyword,
    shiftListError,
    shiftOpen,
    shiftPage,
    shiftPageSize,
    shiftRows,
    shiftSession,
    shifts,
    sortedExceptions,
    sortedHolidays,
    submitCal,
    toggleWeekday,
    workingDaySet,
  }
}
