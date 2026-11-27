<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDatePicker,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSheet,
  NvSheetContent,
  NvSheetDescription,
  NvSheetHeader,
  NvSheetTitle,
  NvSpinner,
  NvStatusBadge,
  NvTabsContent,
  NvToolbar,
} from '@nerv-iip/ui'
import {
  CalendarCogIcon,
  CalendarRangeIcon,
  ChevronLeftIcon,
  ChevronRightIcon,
  PlusIcon,
  Trash2Icon,
} from '@lucide/vue'
import { formatDate } from '@/utils/format'
import type { ShallowUnwrapRef } from 'vue'
import type { useShiftCalendarPage } from '../useShiftCalendarPage'

type View = ShallowUnwrapRef<ReturnType<typeof useShiftCalendarPage>>

defineProps<{
  WEEK_DAYS: View['WEEK_DAYS']
  addException: View['addException']
  addHoliday: View['addHoliday']
  baseDetailFields: View['baseDetailFields']
  calActions: View['calActions']
  calBoardSaving: View['calBoardSaving']
  calDetailLoaded: View['calDetailLoaded']
  calDetailLoading: View['calDetailLoading']
  calEditLoading: View['calEditLoading']
  calEditingCode: View['calEditingCode']
  calForm: View['calForm']
  calListError: View['calListError']
  calRows: View['calRows']
  calShowErrors: View['calShowErrors']
  calendars: View['calendars']
  canCreateCal: View['canCreateCal']
  cancelConflict: View['cancelConflict']
  cellClassMap: View['cellClassMap']
  confirmDelete: View['confirmDelete']
  conflict: View['conflict']
  conflictDescription: View['conflictDescription']
  conflictTitle: View['conflictTitle']
  deleteConfirmDescription: View['deleteConfirmDescription']
  deleteConfirmTitle: View['deleteConfirmTitle']
  exceptionDraft: View['exceptionDraft']
  goToday: View['goToday']
  hasAnyDetail: View['hasAnyDetail']
  holidayDraft: View['holidayDraft']
  isNonEmpty: View['isNonEmpty']
  lifecycle: View['lifecycle']
  monthCells: View['monthCells']
  monthLabel: View['monthLabel']
  nextMonth: View['nextMonth']
  openCreateCal: View['openCreateCal']
  openDeleteException: View['openDeleteException']
  openDeleteHoliday: View['openDeleteHoliday']
  openEditCal: View['openEditCal']
  openManageSheet: View['openManageSheet']
  prevMonth: View['prevMonth']
  resolveConflict: View['resolveConflict']
  rowKey: View['rowKey']
  selectCalendar: View['selectCalendar']
  selectedCalCode: View['selectedCalCode']
  selectedCalName: View['selectedCalName']
  sortedExceptions: View['sortedExceptions']
  sortedHolidays: View['sortedHolidays']
  submitCal: View['submitCal']
  toggleWeekday: View['toggleWeekday']
  workingDaySet: View['workingDaySet']
}>()

const calKeyword = defineModel<View['calKeyword']>('calKeyword', { required: true })

const includeDisabled = defineModel<View['includeDisabled']>('includeDisabled', { required: true })

const calOpen = defineModel<View['calOpen']>('calOpen', { required: true })

const manageSheetOpen = defineModel<View['manageSheetOpen']>('manageSheetOpen', { required: true })

const holidayDateModel = defineModel<View['holidayDateModel']>('holidayDateModel', {
  required: true,
})

const exceptionDateModel = defineModel<View['exceptionDateModel']>('exceptionDateModel', {
  required: true,
})

const deleteConfirmOpen = defineModel<View['deleteConfirmOpen']>('deleteConfirmOpen', {
  required: true,
})
</script>

<template>
  <NvTabsContent value="work-calendar" class="grid gap-3">
    <NvToolbar v-model:search="calKeyword" search-placeholder="搜索日历编码、名称">
      <template #filters>
        <IncludeDisabledFilter v-model="includeDisabled" />
      </template>
      <template #actions>
        <NvDialog v-model:open="calOpen">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreateCal"
              ><PlusIcon aria-hidden="true" />新建工作日历</NvButton
            >
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-lg">
            <NvDialogHeader>
              <NvDialogTitle>{{
                calEditingCode ? `编辑工作日历 · ${calEditingCode}` : '新建工作日历'
              }}</NvDialogTitle>
              <NvDialogDescription class="sr-only">{{
                calEditingCode ? `工作日历 ${calEditingCode}` : '新建工作日历'
              }}</NvDialogDescription>
            </NvDialogHeader>
            <form class="grid gap-4" @submit.prevent="submitCal">
              <CarriedContextSummary
                v-if="calEditingCode"
                label="日历标识"
                :items="[{ label: '日历编码', value: calForm.code }]"
              />
              <p
                v-if="calShowErrors && !canCreateCal"
                class="text-sm text-destructive"
                role="alert"
              >
                请完整填写带 * 的必填项（已标红）。
              </p>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField :data-invalid="calShowErrors && !isNonEmpty(calForm.name)">
                  <NvFieldLabel for="cal-name"
                    >日历名称 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="cal-name" v-model="calForm.name" autocomplete="off" required />
                </NvField>
              </NvFieldGroup>
              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="calOpen = false">取消</NvButton>
                <NvButton
                  type="submit"
                  :disabled="
                    calendars.createPending.value ||
                    calActions.updatePending.value ||
                    calEditLoading
                  "
                >
                  <NvSpinner
                    v-if="calendars.createPending.value || calActions.updatePending.value"
                    aria-hidden="true"
                  />{{ calEditingCode ? '保存修改' : '保存日历' }}
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvToolbar>
    <p v-if="calListError" class="text-sm text-destructive" role="alert">{{ calListError }}</p>

    <div class="grid items-start gap-4 md:grid-cols-[16rem_minmax(0,1fr)]">
      <!-- 左：日历列表（整行可点，选中一个驱动右侧月历）。 -->
      <div class="grid h-fit min-w-0 gap-1.5">
        <div
          v-if="calendars.pending.value"
          class="flex items-center gap-2 px-2 py-6 text-sm text-muted-foreground"
        >
          <NvSpinner aria-hidden="true" />加载工作日历…
        </div>
        <p
          v-else-if="!calRows.length"
          class="rounded-md border border-dashed border-border px-3 py-6 text-center text-sm text-muted-foreground"
        >
          暂无工作日历。可清空筛选或新建日历。
        </p>
        <ul v-else class="grid gap-1">
          <li v-for="row in calRows" :key="rowKey(row)" class="relative">
            <button
              type="button"
              class="flex w-full items-center gap-2 rounded-md py-2 pl-3 pr-10 text-left transition-colors hover:bg-accent/50"
              :class="row.code === selectedCalCode ? 'bg-accent text-accent-foreground' : ''"
              :aria-current="row.code === selectedCalCode ? 'true' : undefined"
              @click="selectCalendar(row)"
            >
              <span class="min-w-0 flex-1 truncate text-sm font-medium">{{
                row.displayName ?? '无'
              }}</span>
              <NvStatusBadge :value="row.active === false ? 'disabled' : 'active'" />
            </button>
            <!-- 行尾「⋯」操作菜单：编辑 / 停用。绝对定位避免按钮嵌套。 -->
            <div class="absolute inset-y-0 right-1 flex items-center">
              <MasterDataRowActions
                :row="row"
                entity-label="工作日历"
                :detail-fields="baseDetailFields(row, '日历编码', '日历名称')"
                @toggle="(row) => lifecycle.request(row, calActions, '工作日历')"
                @edit="openEditCal"
              />
            </div>
          </li>
        </ul>
      </div>

      <!-- 右：月历视图（全部读真实 detail，无明细给空态引导） -->
      <div class="grid h-fit gap-3 rounded-lg border border-border bg-card p-4">
        <div
          v-if="!selectedCalCode"
          class="flex flex-col items-center justify-center gap-3 py-16 text-center"
        >
          <CalendarRangeIcon class="size-8 text-muted-foreground" aria-hidden="true" />
          <p class="text-sm font-medium">先在左侧选择或新建一个工作日历</p>
          <p class="text-xs text-muted-foreground">
            选中后这里按工作日 / 休息日 / 节假日 / 例外日渲染月历，可直接维护。
          </p>
        </div>

        <template v-else>
          <!-- 头部：选中日历名 + 月份切换 + 管理入口 -->
          <div class="flex flex-wrap items-center justify-between gap-2">
            <div class="flex items-center gap-2">
              <span class="text-sm font-semibold">{{ selectedCalName }}</span>
              <span
                v-if="calBoardSaving"
                class="inline-flex items-center gap-1 text-xs text-muted-foreground"
                ><NvSpinner aria-hidden="true" />保存中</span
              >
            </div>
            <div class="flex items-center gap-1">
              <NvButton
                size="icon"
                variant="outline"
                type="button"
                aria-label="上个月"
                @click="prevMonth"
                ><ChevronLeftIcon aria-hidden="true"
              /></NvButton>
              <span class="min-w-20 text-center text-sm font-medium tabular-nums">{{
                monthLabel
              }}</span>
              <NvButton
                size="icon"
                variant="outline"
                type="button"
                aria-label="下个月"
                @click="nextMonth"
                ><ChevronRightIcon aria-hidden="true"
              /></NvButton>
              <NvButton size="sm" variant="outline" type="button" @click="goToday">今天</NvButton>
            </div>
          </div>

          <div
            v-if="calDetailLoading"
            class="flex items-center justify-center gap-2 py-16 text-sm text-muted-foreground"
          >
            <NvSpinner aria-hidden="true" />加载日历明细…
          </div>

          <template v-else>
            <!-- 每周工作模式：月历正上方一行 7 个星期 chip，点亮=工作日，即时保存 -->
            <div class="grid gap-2">
              <div class="flex flex-wrap items-center justify-between gap-2">
                <p class="text-sm font-medium">每周工作模式</p>
                <NvButton size="sm" variant="outline" type="button" @click="openManageSheet()">
                  <CalendarCogIcon aria-hidden="true" />管理节假日 / 例外日
                </NvButton>
              </div>
              <div class="flex flex-wrap gap-1.5">
                <NvButton
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
                </NvButton>
              </div>
              <p class="text-xs text-muted-foreground">
                点亮的星期为工作日；点击切换，立即保存。每天的作息时段由「班次」定义，日历只决定哪几天开工。
              </p>
            </div>

            <!-- 图例（放月历上方，免下拉才能看到；色 + 文字角标双编码，色盲友好） -->
            <div
              class="flex flex-wrap items-center gap-x-4 gap-y-1.5 text-xs text-muted-foreground"
            >
              <span class="inline-flex items-center gap-1.5"
                ><span
                  class="size-3 rounded-sm bg-success/20 ring-1 ring-inset ring-success/40"
                  aria-hidden="true"
                />工作日</span
              >
              <span class="inline-flex items-center gap-1.5"
                ><span
                  class="size-3 rounded-sm bg-muted/50 ring-1 ring-inset ring-border"
                  aria-hidden="true"
                />休息日</span
              >
              <span class="inline-flex items-center gap-1.5"
                ><span
                  class="size-3 rounded-sm bg-destructive/20 ring-1 ring-inset ring-destructive/40"
                  aria-hidden="true"
                /><span class="font-medium text-foreground">假</span> 节假日</span
              >
              <span class="inline-flex items-center gap-1.5"
                ><span
                  class="size-3 rounded-sm bg-warning/25 ring-1 ring-inset ring-warning/45"
                  aria-hidden="true"
                /><span class="text-foreground"
                  >例外日 <span class="font-medium">班</span>上班 /
                  <span class="font-medium">休</span>休息</span
                ></span
              >
            </div>

            <!-- 月历网格（只读展示；点某天可在抽屉里预填该日期） -->
            <div class="grid grid-cols-7 gap-1">
              <div
                v-for="wd in WEEK_DAYS"
                :key="wd.key"
                class="pb-1 text-center text-xs font-medium text-muted-foreground"
              >
                {{ wd.short }}
              </div>
              <button
                v-for="cell in monthCells"
                :key="cell.key"
                type="button"
                class="relative aspect-square rounded-md p-1.5 text-left text-sm"
                :class="
                  cell.inMonth
                    ? [
                        cellClassMap[cell.kind],
                        cell.isToday ? 'outline outline-2 outline-primary' : '',
                        'transition-shadow hover:ring-1 hover:ring-inset hover:ring-primary/50',
                      ]
                    : 'pointer-events-none opacity-0'
                "
                :title="cell.inMonth ? cell.title : undefined"
                :disabled="!cell.inMonth"
                @click="cell.inMonth && openManageSheet(cell.key)"
              >
                <template v-if="cell.inMonth">
                  <span class="tabular-nums">{{ cell.label }}</span>
                  <span
                    v-if="cell.badge"
                    class="absolute bottom-1 right-1 text-[10px] font-semibold leading-none"
                    >{{ cell.badge }}</span
                  >
                </template>
              </button>
            </div>

            <!-- 空态引导：选中日历但完全没有明细 -->
            <div
              v-if="calDetailLoaded && !hasAnyDetail"
              class="rounded-md border border-dashed border-border bg-muted/30 px-3 py-3 text-sm text-muted-foreground"
            >
              该日历还没有任何工作时间 /
              节假日设置，当前全部按休息日显示。请在上方设置每周工作模式，或点「管理节假日 /
              例外日」添加。
            </div>
          </template>
        </template>
      </div>
    </div>

    <!-- 节假日 / 例外日管理抽屉（右侧滑出） -->
    <NvSheet v-model:open="manageSheetOpen">
      <NvSheetContent class="w-full gap-0 overflow-y-auto sm:max-w-md">
        <NvSheetHeader>
          <NvSheetTitle>节假日 / 例外日 · {{ selectedCalName }}</NvSheetTitle>
          <NvSheetDescription class="sr-only"
            >日历 {{ selectedCalName }} 的节假日与例外日</NvSheetDescription
          >
        </NvSheetHeader>

        <div class="grid gap-6 px-4 pb-6">
          <!-- 节假日 -->
          <section class="grid gap-2">
            <p class="text-sm font-medium">法定节假日</p>
            <form
              class="grid gap-2 sm:grid-cols-[auto_1fr_auto] sm:items-end"
              @submit.prevent="addHoliday"
            >
              <div class="grid gap-1">
                <label class="text-xs text-muted-foreground">日期</label>
                <NvDatePicker v-model="holidayDateModel" placeholder="选择日期" class="w-40" />
              </div>
              <div class="grid gap-1">
                <label for="holiday-name" class="text-xs text-muted-foreground">名称（可选）</label>
                <NvInput id="holiday-name" v-model="holidayDraft.name" placeholder="如 端午节" />
              </div>
              <NvButton size="sm" type="submit" :disabled="!holidayDraft.date || calBoardSaving"
                ><PlusIcon aria-hidden="true" />添加</NvButton
              >
            </form>
            <p v-if="!sortedHolidays.length" class="text-xs text-muted-foreground">暂无节假日。</p>
            <ul v-else class="grid gap-1">
              <li
                v-for="h in sortedHolidays"
                :key="h.date"
                class="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-1.5 text-sm"
              >
                <span
                  ><span class="font-medium tabular-nums">{{ formatDate(h.date) }}</span>
                  <span class="text-muted-foreground">{{ h.name || '节假日' }}</span></span
                >
                <!-- 触发只开框；确认框是页面层单实例（v-for 外），见文件末尾。 -->
                <NvButton
                  size="icon"
                  variant="ghost"
                  type="button"
                  aria-label="删除节假日"
                  :disabled="calBoardSaving"
                  @click="openDeleteHoliday(h)"
                  ><Trash2Icon aria-hidden="true"
                /></NvButton>
              </li>
            </ul>
          </section>

          <!-- 例外日 -->
          <section class="grid gap-2 border-t border-border pt-4">
            <p class="text-sm font-medium">例外日</p>
            <form class="grid gap-2" @submit.prevent="addException">
              <div class="grid gap-2 sm:grid-cols-2">
                <div class="grid gap-1">
                  <label class="text-xs text-muted-foreground">日期</label>
                  <NvDatePicker v-model="exceptionDateModel" placeholder="选择日期" class="w-40" />
                </div>
                <div class="grid gap-1">
                  <label for="exception-kind" class="text-xs text-muted-foreground">类型</label>
                  <NvSelect v-model="exceptionDraft.isWorkingDay">
                    <NvSelectTrigger id="exception-kind" class="w-40"
                      ><NvSelectValue
                    /></NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem value="true">当日上班</NvSelectItem>
                      <NvSelectItem value="false">当日休息</NvSelectItem>
                    </NvSelectContent>
                  </NvSelect>
                </div>
              </div>
              <div class="grid gap-2 sm:grid-cols-[1fr_auto] sm:items-end">
                <div class="grid gap-1">
                  <label for="exception-reason" class="text-xs text-muted-foreground"
                    >原因（可选）</label
                  >
                  <NvInput
                    id="exception-reason"
                    v-model="exceptionDraft.reason"
                    placeholder="如 调休"
                  />
                </div>
                <NvButton size="sm" type="submit" :disabled="!exceptionDraft.date || calBoardSaving"
                  ><PlusIcon aria-hidden="true" />添加</NvButton
                >
              </div>
            </form>
            <p v-if="!sortedExceptions.length" class="text-xs text-muted-foreground">
              暂无例外日。
            </p>
            <ul v-else class="grid gap-1">
              <li
                v-for="e in sortedExceptions"
                :key="e.date"
                class="flex items-center justify-between rounded-md bg-muted/40 px-2.5 py-1.5 text-sm"
              >
                <span
                  ><span class="font-medium tabular-nums">{{ formatDate(e.date) }}</span>
                  <span class="text-muted-foreground"
                    >{{ e.isWorkingDay ? '当日上班' : '当日休息'
                    }}{{ e.reason ? ` · ${e.reason}` : '' }}</span
                  ></span
                >
                <!-- 触发只开框；与节假日共用页面层那一个确认框，靠 deleteTarget.kind 判别。 -->
                <NvButton
                  size="icon"
                  variant="ghost"
                  type="button"
                  aria-label="删除例外日"
                  :disabled="calBoardSaving"
                  @click="openDeleteException(e)"
                  ><Trash2Icon aria-hidden="true"
                /></NvButton>
              </li>
            </ul>
          </section>
        </div>
      </NvSheetContent>
    </NvSheet>

    <!-- 节假日 / 例外日删除确认（受控，页面层单实例；两类目标共用，靠 deleteTarget.kind 判别） -->
    <NvAlertDialog v-model:open="deleteConfirmOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>{{ deleteConfirmTitle }}</NvAlertDialogTitle>
          <NvAlertDialogDescription>{{ deleteConfirmDescription }}</NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框（confirm-destroy 规则 3）。 -->
          <NvButton
            type="button"
            variant="destructive"
            :disabled="calBoardSaving"
            @click="confirmDelete"
            >确认删除</NvButton
          >
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>

    <!-- 节假日 / 例外日互斥冲突确认（受控） -->
    <NvAlertDialog v-model:open="conflict.open">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>{{ conflictTitle }}</NvAlertDialogTitle>
          <NvAlertDialogDescription>{{ conflictDescription }}</NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel @click="cancelConflict">取消</NvAlertDialogCancel>
          <!-- 同上：替换要等写回结果，失败时保留草稿与本框，故不用 NvAlertDialogAction。 -->
          <NvButton type="button" :disabled="calBoardSaving" @click="resolveConflict"
            >确认替换</NvButton
          >
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </NvTabsContent>
</template>
