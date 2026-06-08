<script setup lang="ts">
import type {
  BusinessConsoleCreateDepartmentRequest,
  BusinessConsoleCreateShiftRequest,
  BusinessConsoleCreateTeamRequest,
  BusinessConsoleCreateWorkCalendarRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import { useMasterDataResource } from '@/composables/useBusinessMasterData'
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
  SectionCard,
  SectionCards,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  toast,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '组织与日历' } })

const departments = useMasterDataResource<BusinessConsoleCreateDepartmentRequest>('department')
const teams = useMasterDataResource<BusinessConsoleCreateTeamRequest>('team')
const shifts = useMasterDataResource<BusinessConsoleCreateShiftRequest>('shift')
const calendars = useMasterDataResource<BusinessConsoleCreateWorkCalendarRequest>('work-calendar')

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
]

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
  void departments.refresh()
  void teams.refresh()
  void shifts.refresh()
  void calendars.refresh()
}

// ---- 部门 ----
const deptKeyword = ref('')
const deptPage = ref(1)
const deptPageSize = ref('10')
const deptOpen = ref(false)
const deptForm = reactive({ code: '', name: '', parentDepartmentCode: '' })
const deptRows = computed(() => filterRows(departments.items.value, deptKeyword.value))
const canCreateDept = computed(() => [deptForm.code, deptForm.name].every(isNonEmpty))
const deptCreateError = computed(() => formatError(departments.createError.value))
const deptListError = computed(() => formatError(departments.error.value))
watch([deptKeyword, deptPageSize], () => { deptPage.value = 1 })
watch([deptPage, deptPageSize], () => {
  departments.filters.skip = (deptPage.value - 1) * (Number(deptPageSize.value) || 10)
  departments.filters.take = Number(deptPageSize.value) || 10
}, { immediate: true })
async function submitDept() {
  if (!canCreateDept.value) return
  await departments.create({
    organizationId: departments.filters.organizationId,
    environmentId: departments.filters.environmentId,
    code: deptForm.code.trim(),
    name: deptForm.name.trim(),
    parentDepartmentCode: deptForm.parentDepartmentCode.trim() || null,
  })
  toast.success(`部门「${deptForm.name.trim()}」已创建。`)
  Object.assign(deptForm, { code: '', name: '', parentDepartmentCode: '' })
  deptOpen.value = false
}

// ---- 班组 ----
const teamKeyword = ref('')
const teamPage = ref(1)
const teamPageSize = ref('10')
const teamOpen = ref(false)
const teamForm = reactive({ code: '', name: '', departmentCode: '', shiftCode: '' })
const teamRows = computed(() => filterRows(teams.items.value, teamKeyword.value))
const canCreateTeam = computed(() => [teamForm.code, teamForm.name, teamForm.departmentCode, teamForm.shiftCode].every(isNonEmpty))
const teamCreateError = computed(() => formatError(teams.createError.value))
const teamListError = computed(() => formatError(teams.error.value))
watch([teamKeyword, teamPageSize], () => { teamPage.value = 1 })
watch([teamPage, teamPageSize], () => {
  teams.filters.skip = (teamPage.value - 1) * (Number(teamPageSize.value) || 10)
  teams.filters.take = Number(teamPageSize.value) || 10
}, { immediate: true })
async function submitTeam() {
  if (!canCreateTeam.value) return
  await teams.create({
    organizationId: teams.filters.organizationId,
    environmentId: teams.filters.environmentId,
    code: teamForm.code.trim(),
    name: teamForm.name.trim(),
    departmentCode: teamForm.departmentCode.trim(),
    shiftCode: teamForm.shiftCode.trim(),
  })
  toast.success(`班组「${teamForm.name.trim()}」已创建。`)
  Object.assign(teamForm, { code: '', name: '', departmentCode: '', shiftCode: '' })
  teamOpen.value = false
}

// ---- 班次 ----
const shiftKeyword = ref('')
const shiftPage = ref(1)
const shiftPageSize = ref('10')
const shiftOpen = ref(false)
const shiftForm = reactive({ code: '', name: '', startsAt: '08:00', endsAt: '16:00', paidMinutes: '480' })
const shiftRows = computed(() => filterRows(shifts.items.value, shiftKeyword.value))
const canCreateShift = computed(() => [shiftForm.code, shiftForm.name].every(isNonEmpty) && (Number(shiftForm.paidMinutes) || 0) > 0)
const shiftCreateError = computed(() => formatError(shifts.createError.value))
const shiftListError = computed(() => formatError(shifts.error.value))
watch([shiftKeyword, shiftPageSize], () => { shiftPage.value = 1 })
watch([shiftPage, shiftPageSize], () => {
  shifts.filters.skip = (shiftPage.value - 1) * (Number(shiftPageSize.value) || 10)
  shifts.filters.take = Number(shiftPageSize.value) || 10
}, { immediate: true })
async function submitShift() {
  if (!canCreateShift.value) return
  await shifts.create({
    organizationId: shifts.filters.organizationId,
    environmentId: shifts.filters.environmentId,
    code: shiftForm.code.trim(),
    name: shiftForm.name.trim(),
    startsAt: shiftForm.startsAt.trim() || undefined,
    endsAt: shiftForm.endsAt.trim() || undefined,
    paidMinutes: Number(shiftForm.paidMinutes) || 480,
  })
  toast.success(`班次「${shiftForm.name.trim()}」已创建。`)
  Object.assign(shiftForm, { code: '', name: '', startsAt: '08:00', endsAt: '16:00', paidMinutes: '480' })
  shiftOpen.value = false
}

// ---- 工作日历 ----
const calKeyword = ref('')
const calPage = ref(1)
const calPageSize = ref('10')
const calOpen = ref(false)
const calForm = reactive({ code: '', name: '' })
const calRows = computed(() => filterRows(calendars.items.value, calKeyword.value))
const canCreateCal = computed(() => [calForm.code, calForm.name].every(isNonEmpty))
const calCreateError = computed(() => formatError(calendars.createError.value))
const calListError = computed(() => formatError(calendars.error.value))
watch([calKeyword, calPageSize], () => { calPage.value = 1 })
watch([calPage, calPageSize], () => {
  calendars.filters.skip = (calPage.value - 1) * (Number(calPageSize.value) || 10)
  calendars.filters.take = Number(calPageSize.value) || 10
}, { immediate: true })
async function submitCal() {
  if (!canCreateCal.value) return
  await calendars.create({
    organizationId: calendars.filters.organizationId,
    environmentId: calendars.filters.environmentId,
    code: calForm.code.trim(),
    name: calForm.name.trim(),
  })
  toast.success(`工作日历「${calForm.name.trim()}」已创建。`)
  Object.assign(calForm, { code: '', name: '' })
  calOpen.value = false
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="组织与日历" :breadcrumbs="[{ label: '基础数据' }]" :count="`${departments.total.value} 个部门`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="departments.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <SectionCards :columns="4">
      <SectionCard description="部门数" :value="departments.total.value" hint="组织结构" />
      <SectionCard description="班组数" :value="teams.total.value" hint="挂靠部门与班次" />
      <SectionCard description="班次数" :value="shifts.total.value" hint="排班时段" />
      <SectionCard description="工作日历数" :value="calendars.total.value" hint="可用工作日" />
    </SectionCards>

    <Tabs default-value="department">
      <TabsList>
        <TabsTrigger value="department">部门 ({{ departments.total.value }})</TabsTrigger>
        <TabsTrigger value="team">班组 ({{ teams.total.value }})</TabsTrigger>
        <TabsTrigger value="shift">班次 ({{ shifts.total.value }})</TabsTrigger>
        <TabsTrigger value="work-calendar">工作日历 ({{ calendars.total.value }})</TabsTrigger>
      </TabsList>

      <!-- 部门 -->
      <TabsContent value="department" class="grid gap-3">
        <Toolbar v-model:search="deptKeyword" search-placeholder="在当前页内筛选部门编码、名称">
          <template #actions>
            <Dialog v-model:open="deptOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button"><PlusIcon aria-hidden="true" />新建部门</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>新建部门</DialogTitle>
                  <DialogDescription>登记一个组织部门，可选挂靠上级部门。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitDept">
                  <p v-if="deptCreateError" class="text-sm text-destructive" role="alert">{{ deptCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="!isNonEmpty(deptForm.code)">
                      <FieldLabel for="dept-code">部门编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="dept-code" v-model="deptForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="!isNonEmpty(deptForm.name)">
                      <FieldLabel for="dept-name">部门名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="dept-name" v-model="deptForm.name" autocomplete="off" required />
                    </Field>
                    <Field>
                      <FieldLabel for="dept-parent">上级部门</FieldLabel>
                      <Select v-model="deptForm.parentDepartmentCode">
                        <SelectTrigger id="dept-parent"><SelectValue placeholder="无（顶级部门）" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="d in departments.items.value" :key="d.code" :value="d.code ?? ''">
                            {{ d.displayName ?? d.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                      <FieldDescription>留空表示顶级部门。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="deptOpen = false">取消</Button>
                    <Button type="submit" :disabled="departments.createPending.value || !canCreateDept">
                      <Spinner v-if="departments.createPending.value" aria-hidden="true" />保存部门
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="deptListError" class="text-sm text-destructive" role="alert">{{ deptListError }}</p>
        <DataTable :columns="columns" :rows="deptRows" :row-key="rowKey" :loading="departments.pending.value" empty-message="暂无部门。可清空筛选或新建部门。">
          <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
        </DataTable>
        <DataTablePagination v-model:page="deptPage" v-model:page-size="deptPageSize" :total-items="departments.total.value" />
      </TabsContent>

      <!-- 班组 -->
      <TabsContent value="team" class="grid gap-3">
        <Toolbar v-model:search="teamKeyword" search-placeholder="在当前页内筛选班组编码、名称">
          <template #actions>
            <Dialog v-model:open="teamOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button"><PlusIcon aria-hidden="true" />新建班组</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>新建班组</DialogTitle>
                  <DialogDescription>将班组挂靠到部门与班次。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitTeam">
                  <p v-if="teamCreateError" class="text-sm text-destructive" role="alert">{{ teamCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="!isNonEmpty(teamForm.code)">
                      <FieldLabel for="team-code">班组编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="team-code" v-model="teamForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="!isNonEmpty(teamForm.name)">
                      <FieldLabel for="team-name">班组名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="team-name" v-model="teamForm.name" autocomplete="off" required />
                    </Field>
                    <Field>
                      <FieldLabel for="team-dept">所属部门 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="teamForm.departmentCode">
                        <SelectTrigger id="team-dept"><SelectValue placeholder="请选择部门" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="d in departments.items.value" :key="d.code" :value="d.code ?? ''">
                            {{ d.displayName ?? d.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <FieldLabel for="team-shift">所属班次 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="teamForm.shiftCode">
                        <SelectTrigger id="team-shift"><SelectValue placeholder="请选择班次" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="s in shifts.items.value" :key="s.code" :value="s.code ?? ''">
                            {{ s.displayName ?? s.code }}
                          </SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="teamOpen = false">取消</Button>
                    <Button type="submit" :disabled="teams.createPending.value || !canCreateTeam">
                      <Spinner v-if="teams.createPending.value" aria-hidden="true" />保存班组
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="teamListError" class="text-sm text-destructive" role="alert">{{ teamListError }}</p>
        <DataTable :columns="columns" :rows="teamRows" :row-key="rowKey" :loading="teams.pending.value" empty-message="暂无班组。可清空筛选或新建班组。">
          <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
        </DataTable>
        <DataTablePagination v-model:page="teamPage" v-model:page-size="teamPageSize" :total-items="teams.total.value" />
      </TabsContent>

      <!-- 班次 -->
      <TabsContent value="shift" class="grid gap-3">
        <Toolbar v-model:search="shiftKeyword" search-placeholder="在当前页内筛选班次编码、名称">
          <template #actions>
            <Dialog v-model:open="shiftOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button"><PlusIcon aria-hidden="true" />新建班次</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>新建班次</DialogTitle>
                  <DialogDescription>定义一个排班时段及计薪时长。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitShift">
                  <p v-if="shiftCreateError" class="text-sm text-destructive" role="alert">{{ shiftCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="!isNonEmpty(shiftForm.code)">
                      <FieldLabel for="shift-code">班次编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-code" v-model="shiftForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="!isNonEmpty(shiftForm.name)">
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
                    </Field>
                    <Field>
                      <FieldLabel for="shift-paid">计薪时长（分钟） <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-paid" v-model="shiftForm.paidMinutes" type="number" min="1" inputmode="numeric" />
                      <FieldDescription>扣除休息后的有效计薪分钟数，默认 480（8 小时）。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="shiftOpen = false">取消</Button>
                    <Button type="submit" :disabled="shifts.createPending.value || !canCreateShift">
                      <Spinner v-if="shifts.createPending.value" aria-hidden="true" />保存班次
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
        </DataTable>
        <DataTablePagination v-model:page="shiftPage" v-model:page-size="shiftPageSize" :total-items="shifts.total.value" />
      </TabsContent>

      <!-- 工作日历 -->
      <TabsContent value="work-calendar" class="grid gap-3">
        <Toolbar v-model:search="calKeyword" search-placeholder="在当前页内筛选日历编码、名称">
          <template #actions>
            <Dialog v-model:open="calOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button"><PlusIcon aria-hidden="true" />新建工作日历</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>新建工作日历</DialogTitle>
                  <DialogDescription>登记一个工作日历，供工作中心与排程引用。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitCal">
                  <p v-if="calCreateError" class="text-sm text-destructive" role="alert">{{ calCreateError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field :data-invalid="!isNonEmpty(calForm.code)">
                      <FieldLabel for="cal-code">日历编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="cal-code" v-model="calForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="!isNonEmpty(calForm.name)">
                      <FieldLabel for="cal-name">日历名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="cal-name" v-model="calForm.name" autocomplete="off" required />
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="calOpen = false">取消</Button>
                    <Button type="submit" :disabled="calendars.createPending.value || !canCreateCal">
                      <Spinner v-if="calendars.createPending.value" aria-hidden="true" />保存日历
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
        </DataTable>
        <DataTablePagination v-model:page="calPage" v-model:page-size="calPageSize" :total-items="calendars.total.value" />
      </TabsContent>
    </Tabs>
  </BusinessLayout>
</template>
