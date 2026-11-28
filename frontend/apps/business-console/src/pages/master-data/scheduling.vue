<script setup lang="ts">
import { usePermission } from '@/composables/usePermission'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import ShiftFormDialog from '@/components/masterData/ShiftFormDialog.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvButton,
  NvDataTable,
  NvPageHeader,
  NvStatusBadge,
  NvTabs,
  NvTabsContent,
  NvTabsList,
  NvTabsTrigger,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import ShiftWorkingTime from './components/ShiftWorkingTime.vue'
import WorkCalendarTab from './components/WorkCalendarTab.vue'
import { useShiftCalendarPage } from './useShiftCalendarPage'

const { can } = usePermission()

definePage({
  meta: {
    requiresAuth: true,
    title: '排班与日历',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const {
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
} = useShiftCalendarPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="排班与日历"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${shifts.total.value} 个班次`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="shifts.pending.value"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
      </template>
    </NvPageHeader>

    <NvTabs default-value="shift">
      <NvTabsList>
        <NvTabsTrigger value="shift">班次 ({{ shifts.total.value }})</NvTabsTrigger>
        <NvTabsTrigger value="work-calendar">工作日历 ({{ calendars.total.value }})</NvTabsTrigger>
      </NvTabsList>

      <!-- 班次 -->
      <NvTabsContent value="shift" class="grid gap-3">
        <NvToolbar v-model:search="shiftKeyword" search-placeholder="搜索班次编码、名称">
          <template #filters>
            <IncludeDisabledFilter v-model="includeDisabled" />
          </template>
          <template #actions>
            <NvButton
              v-if="can(P.masterDataResourcesManage)"
              size="sm"
              type="button"
              @click="openShiftForm()"
              ><PlusIcon aria-hidden="true" />新建班次</NvButton
            >
          </template>
        </NvToolbar>
        <p v-if="shiftListError" class="text-sm text-destructive" role="alert">
          {{ shiftListError }}
        </p>
        <NvDataTable
          manual
          :page="shiftPage"
          :page-size="shiftPageSize"
          :total-items="shifts.total.value"
          @update:page="shiftPage = $event"
          @update:page-size="(v) => (shiftPageSize = String(v))"
          :searchable="false"
          :column-settings="false"
          :columns="columns"
          :rows="shiftRows"
          :row-key="rowKey"
          :loading="shifts.pending.value"
          empty-message="暂无班次。可清空筛选或新建班次。"
        >
          <template #cell-active="{ row }"
            ><NvStatusBadge :value="row.active === false ? 'disabled' : 'active'"
          /></template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions
              :read-only="!can(P.masterDataResourcesManage)"
              :row="row"
              entity-label="班次"
              :detail-fields="baseDetailFields(row, '班次编码', '班次名称')"
              @toggle="(row) => lifecycle.request(row, shiftActions, '班次')"
              @edit="openEditShift"
            >
              <template #detail>
                <ShiftWorkingTime :code="row.code" :fetch-detail="shiftActions.fetchDetail" />
              </template>
            </MasterDataRowActions>
          </template>
        </NvDataTable>
      </NvTabsContent>

      <!-- 工作日历 -->
      <WorkCalendarTab
        v-model:cal-keyword="calKeyword"
        v-model:include-disabled="includeDisabled"
        v-model:cal-open="calOpen"
        v-model:manage-sheet-open="manageSheetOpen"
        v-model:holiday-date-model="holidayDateModel"
        v-model:exception-date-model="exceptionDateModel"
        v-model:delete-confirm-open="deleteConfirmOpen"
        :-w-e-e-k_-d-a-y-s="WEEK_DAYS"
        :add-exception="addException"
        :add-holiday="addHoliday"
        :base-detail-fields="baseDetailFields"
        :cal-actions="calActions"
        :cal-board-saving="calBoardSaving"
        :cal-detail-loaded="calDetailLoaded"
        :cal-detail-loading="calDetailLoading"
        :cal-edit-loading="calEditLoading"
        :cal-editing-code="calEditingCode"
        :cal-form="calForm"
        :cal-list-error="calListError"
        :cal-rows="calRows"
        :cal-show-errors="calShowErrors"
        :calendars="calendars"
        :can-create-cal="canCreateCal"
        :cancel-conflict="cancelConflict"
        :cell-class-map="cellClassMap"
        :confirm-delete="confirmDelete"
        :conflict="conflict"
        :conflict-description="conflictDescription"
        :conflict-title="conflictTitle"
        :delete-confirm-description="deleteConfirmDescription"
        :delete-confirm-title="deleteConfirmTitle"
        :exception-draft="exceptionDraft"
        :go-today="goToday"
        :has-any-detail="hasAnyDetail"
        :holiday-draft="holidayDraft"
        :is-non-empty="isNonEmpty"
        :lifecycle="lifecycle"
        :month-cells="monthCells"
        :month-label="monthLabel"
        :next-month="nextMonth"
        :open-create-cal="openCreateCal"
        :open-delete-exception="openDeleteException"
        :open-delete-holiday="openDeleteHoliday"
        :open-edit-cal="openEditCal"
        :open-manage-sheet="openManageSheet"
        :prev-month="prevMonth"
        :resolve-conflict="resolveConflict"
        :row-key="rowKey"
        :select-calendar="selectCalendar"
        :selected-cal-code="selectedCalCode"
        :selected-cal-name="selectedCalName"
        :sorted-exceptions="sortedExceptions"
        :sorted-holidays="sortedHolidays"
        :submit-cal="submitCal"
        :toggle-weekday="toggleWeekday"
        :working-day-set="workingDaySet"
      />
    </NvTabs>
    <MasterDataLifecycleDialog :controller="lifecycle" />
    <ShiftFormDialog
      v-if="shiftSession"
      :key="shiftSession"
      v-model:open="shiftOpen"
      :editing="shiftEditing"
    />
  </BusinessLayout>
</template>
