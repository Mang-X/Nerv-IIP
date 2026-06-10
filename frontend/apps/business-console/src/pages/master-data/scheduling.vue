<script setup lang="ts">
import type {
  BusinessConsoleCreateShiftRequest,
  BusinessConsoleCreateWorkCalendarRequest,
  BusinessConsoleResourceItem,
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
import { CalendarRangeIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
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
const canCreateShift = computed(() => [shiftForm.code, shiftForm.name].every(isNonEmpty) && (Number(shiftForm.paidMinutes) || 0) > 0)
const shiftFormValid = computed(() => (shiftEditingCode.value ? isNonEmpty(shiftForm.name) : canCreateShift.value))
const shiftListError = computed(() => formatError(shifts.error.value))
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
  }
  finally {
    shiftEditLoading.value = false
  }
}
async function submitShift() {
  if (shiftEditingCode.value) {
    if (!isNonEmpty(shiftForm.name)) {
      shiftShowErrors.value = true
      return
    }
    try {
      await shiftActions.update(shiftEditingCode.value, { name: shiftForm.name.trim() })
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

// ---- 工作日历（Phase 1 平表；月历视图待 #373 后端日历明细，入口禁用） ----
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
                  <DialogDescription>{{ shiftEditingCode ? '修改班次名称（编码不可修改）。带 * 为必填项。' : '定义一个排班时段及计薪时长。带 * 为必填项。' }}</DialogDescription>
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
                      <Input id="shift-start" v-model="shiftForm.startsAt" type="time" :disabled="!!shiftEditingCode" />
                    </Field>
                    <Field>
                      <FieldLabel for="shift-end">结束时间</FieldLabel>
                      <Input id="shift-end" v-model="shiftForm.endsAt" type="time" :disabled="!!shiftEditingCode" />
                    </Field>
                    <Field :data-invalid="shiftShowErrors && !shiftEditingCode && !((Number(shiftForm.paidMinutes) || 0) > 0)">
                      <FieldLabel for="shift-paid">计薪时长（分钟） <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-paid" v-model="shiftForm.paidMinutes" type="number" min="1" inputmode="numeric" :disabled="!!shiftEditingCode" />
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
        <div class="flex flex-wrap items-center justify-between gap-2 rounded-md border border-dashed border-border bg-muted/30 px-3 py-2">
          <p class="text-sm text-muted-foreground">月历视图（按日标注工作日 / 休息日 / 法定节假日）</p>
          <Button size="sm" variant="outline" type="button" disabled>
            <CalendarRangeIcon aria-hidden="true" />
            月历视图 · 建设中
          </Button>
        </div>
        <p class="text-xs text-muted-foreground">月历可视化待后端日历明细（工作日 / 节假日）上线后开放；当前以平表维护日历主档。</p>
        <Toolbar v-model:search="calKeyword" search-placeholder="在当前页内筛选日历编码、名称">
          <template #actions>
            <Dialog v-model:open="calOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button" @click="openCreateCal"><PlusIcon aria-hidden="true" />新建工作日历</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ calEditingCode ? `编辑工作日历 · ${calEditingCode}` : '新建工作日历' }}</DialogTitle>
                  <DialogDescription>{{ calEditingCode ? '修改日历名称（编码不可修改）。带 * 为必填项。' : '登记一个工作日历，供工作中心与排程引用。带 * 为必填项。' }}</DialogDescription>
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
        <DataTable :columns="columns" :rows="calRows" :row-key="rowKey" :loading="calendars.pending.value" empty-message="暂无工作日历。可清空筛选或新建日历。">
          <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="工作日历" :detail-fields="baseDetailFields(row, '日历编码', '日历名称')" :actions="calActions" @edit="openEditCal" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="calPage" v-model:page-size="calPageSize" :total-items="calendars.total.value" />
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
