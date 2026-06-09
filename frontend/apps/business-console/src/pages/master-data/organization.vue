<script setup lang="ts">
import type {
  BusinessConsoleCreateDepartmentRequest,
  BusinessConsoleCreateShiftRequest,
  BusinessConsoleCreateTeamRequest,
  BusinessConsoleCreateWorkCalendarRequest,
  BusinessConsoleResourceItem,
} from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import TeamMembersDialog from '@/components/masterData/TeamMembersDialog.vue'
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import { useBusinessMasterDataResources, useMasterDataResource, useMasterDataResourceActions, usePersonnelSkillAssignment } from '@/composables/useBusinessMasterData'
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
import { PlusIcon, RefreshCwIcon, UsersIcon } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'

definePage({ meta: { requiresAuth: true, title: '组织与人员' } })

const departments = useMasterDataResource<BusinessConsoleCreateDepartmentRequest>('department')
const teams = useMasterDataResource<BusinessConsoleCreateTeamRequest>('team')
const shifts = useMasterDataResource<BusinessConsoleCreateShiftRequest>('shift')
const calendars = useMasterDataResource<BusinessConsoleCreateWorkCalendarRequest>('work-calendar')
// 人员技能：列表只读 + 登记可写（工人选择器 + 技能编码 + 等级）。
const skills = useBusinessMasterDataResources('personnel-skill')
const skillAssignment = usePersonnelSkillAssignment()
const deptActions = useMasterDataResourceActions('department')
const teamActions = useMasterDataResourceActions('team')
const shiftActions = useMasterDataResourceActions('shift')
const calActions = useMasterDataResourceActions('work-calendar')

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '版本', width: 'w-28', accessor: (r) => r.snapshotVersion ?? '无' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]
const orgActionError = computed(() =>
  formatError(
    deptActions.actionError.value ?? teamActions.actionError.value
    ?? shiftActions.actionError.value ?? calActions.actionError.value,
  ),
)
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
  void departments.refresh()
  void teams.refresh()
  void shifts.refresh()
  void calendars.refresh()
  void skills.refreshResources()
}

// ---- 部门 ----
const deptKeyword = ref('')
const deptPage = ref(1)
const deptPageSize = ref('10')
const deptOpen = ref(false)
const deptShowErrors = ref(false)
const deptForm = reactive({ code: '', name: '', parentDepartmentCode: '' })
const deptRows = computed(() => filterRows(departments.items.value, deptKeyword.value))
const canCreateDept = computed(() => [deptForm.code, deptForm.name].every(isNonEmpty))
const deptCreateError = computed(() => formatError(departments.createError.value))
const deptListError = computed(() => formatError(departments.error.value))
watch(deptOpen, (open) => { if (open) deptShowErrors.value = false })
watch([deptKeyword, deptPageSize], () => { deptPage.value = 1 })
watch([deptPage, deptPageSize], () => {
  departments.filters.skip = (deptPage.value - 1) * (Number(deptPageSize.value) || 10)
  departments.filters.take = Number(deptPageSize.value) || 10
}, { immediate: true })
async function submitDept() {
  if (!canCreateDept.value) {
    deptShowErrors.value = true
    return
  }
  await departments.create({
    organizationId: departments.filters.organizationId,
    environmentId: departments.filters.environmentId,
    code: deptForm.code.trim(),
    name: deptForm.name.trim(),
    parentDepartmentCode: deptForm.parentDepartmentCode.trim() || null,
  })
  toast.success(`部门「${deptForm.name.trim()}」已创建。`)
  Object.assign(deptForm, { code: '', name: '', parentDepartmentCode: '' })
  deptShowErrors.value = false
  deptOpen.value = false
}

// ---- 班组 ----
const teamKeyword = ref('')
const teamPage = ref(1)
const teamPageSize = ref('10')
const teamOpen = ref(false)
const teamShowErrors = ref(false)
const teamForm = reactive({ code: '', name: '', departmentCode: '', shiftCode: '' })
const teamRows = computed(() => filterRows(teams.items.value, teamKeyword.value))
const canCreateTeam = computed(() => [teamForm.code, teamForm.name, teamForm.departmentCode, teamForm.shiftCode].every(isNonEmpty))
const teamCreateError = computed(() => formatError(teams.createError.value))
const teamListError = computed(() => formatError(teams.error.value))
watch(teamOpen, (open) => { if (open) teamShowErrors.value = false })
watch([teamKeyword, teamPageSize], () => { teamPage.value = 1 })
watch([teamPage, teamPageSize], () => {
  teams.filters.skip = (teamPage.value - 1) * (Number(teamPageSize.value) || 10)
  teams.filters.take = Number(teamPageSize.value) || 10
}, { immediate: true })
async function submitTeam() {
  if (!canCreateTeam.value) {
    teamShowErrors.value = true
    return
  }
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
  teamShowErrors.value = false
  teamOpen.value = false
}

// ---- 班组成员维护（弹窗，按行打开）----
const membersOpen = ref(false)
const membersTeam = reactive({ code: '', name: '' })
function openMembers(row: BusinessConsoleResourceItem) {
  membersTeam.code = row.code ?? ''
  membersTeam.name = row.displayName ?? row.code ?? ''
  membersOpen.value = true
}

// ---- 班次 ----
const shiftKeyword = ref('')
const shiftPage = ref(1)
const shiftPageSize = ref('10')
const shiftOpen = ref(false)
const shiftShowErrors = ref(false)
const shiftForm = reactive({ code: '', name: '', startsAt: '08:00', endsAt: '16:00', paidMinutes: '480' })
const shiftRows = computed(() => filterRows(shifts.items.value, shiftKeyword.value))
const canCreateShift = computed(() => [shiftForm.code, shiftForm.name].every(isNonEmpty) && (Number(shiftForm.paidMinutes) || 0) > 0)
const shiftCreateError = computed(() => formatError(shifts.createError.value))
const shiftListError = computed(() => formatError(shifts.error.value))
watch(shiftOpen, (open) => { if (open) shiftShowErrors.value = false })
watch([shiftKeyword, shiftPageSize], () => { shiftPage.value = 1 })
watch([shiftPage, shiftPageSize], () => {
  shifts.filters.skip = (shiftPage.value - 1) * (Number(shiftPageSize.value) || 10)
  shifts.filters.take = Number(shiftPageSize.value) || 10
}, { immediate: true })
async function submitShift() {
  if (!canCreateShift.value) {
    shiftShowErrors.value = true
    return
  }
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
  shiftShowErrors.value = false
  shiftOpen.value = false
}

// ---- 工作日历 ----
const calKeyword = ref('')
const calPage = ref(1)
const calPageSize = ref('10')
const calOpen = ref(false)
const calShowErrors = ref(false)
const calForm = reactive({ code: '', name: '' })
const calRows = computed(() => filterRows(calendars.items.value, calKeyword.value))
const canCreateCal = computed(() => [calForm.code, calForm.name].every(isNonEmpty))
const calCreateError = computed(() => formatError(calendars.createError.value))
const calListError = computed(() => formatError(calendars.error.value))
watch(calOpen, (open) => { if (open) calShowErrors.value = false })
watch([calKeyword, calPageSize], () => { calPage.value = 1 })
watch([calPage, calPageSize], () => {
  calendars.filters.skip = (calPage.value - 1) * (Number(calPageSize.value) || 10)
  calendars.filters.take = Number(calPageSize.value) || 10
}, { immediate: true })
async function submitCal() {
  if (!canCreateCal.value) {
    calShowErrors.value = true
    return
  }
  await calendars.create({
    organizationId: calendars.filters.organizationId,
    environmentId: calendars.filters.environmentId,
    code: calForm.code.trim(),
    name: calForm.name.trim(),
  })
  toast.success(`工作日历「${calForm.name.trim()}」已创建。`)
  Object.assign(calForm, { code: '', name: '' })
  calShowErrors.value = false
  calOpen.value = false
}

// ---- 人员技能（列表只读 + 登记可写）----
const skillKeyword = ref('')
const skillPage = ref(1)
const skillPageSize = ref('10')
const skillRows = computed(() => filterRows(skills.resources.value, skillKeyword.value))
const skillListError = computed(() => formatError(skills.resourcesError.value))
const skillActions = useMasterDataResourceActions('personnel-skill')
watch([skillKeyword, skillPageSize], () => { skillPage.value = 1 })
watch([skillPage, skillPageSize], () => {
  skills.filters.skip = (skillPage.value - 1) * (Number(skillPageSize.value) || 10)
  skills.filters.take = Number(skillPageSize.value) || 10
}, { immediate: true })

const SKILL_LEVELS = [
  { value: 'junior', label: '初级' },
  { value: 'intermediate', label: '中级' },
  { value: 'senior', label: '高级' },
  { value: 'expert', label: '专家' },
] as const
const skillOpen = ref(false)
const skillShowErrors = ref(false)
const skillForm = reactive({ userId: '', skillCode: '', level: '', effectiveFrom: '' })
const canAssignSkill = computed(() =>
  isNonEmpty(skillForm.userId) && isNonEmpty(skillForm.skillCode) && isNonEmpty(skillForm.level),
)
const skillAssignError = computed(() => formatError(skillAssignment.assignError.value))
watch(skillOpen, (open) => {
  if (open) {
    skillShowErrors.value = false
    Object.assign(skillForm, { userId: '', skillCode: '', level: '', effectiveFrom: '' })
  }
})
async function submitSkill() {
  if (!canAssignSkill.value) {
    skillShowErrors.value = true
    return
  }
  await skillAssignment.assign({
    userId: skillForm.userId,
    skillCode: skillForm.skillCode.trim(),
    level: skillForm.level,
    effectiveFrom: skillForm.effectiveFrom.trim() || undefined,
  })
  toast.success('已登记人员技能。')
  skillShowErrors.value = false
  skillOpen.value = false
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="组织与人员" :breadcrumbs="[{ label: '基础数据' }]" :count="`${departments.total.value} 个部门`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="departments.pending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      工人来自系统用户（IAM）。可在班组行内维护成员（含组长），并为工人登记技能与等级；选择工人时按姓名 / 工号检索。
    </p>

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
        <TabsTrigger value="personnel-skill">人员技能 ({{ skills.resourcesTotal.value }})</TabsTrigger>
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
                    <Field :data-invalid="deptShowErrors && !isNonEmpty(deptForm.code)">
                      <FieldLabel for="dept-code">部门编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="dept-code" v-model="deptForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="deptShowErrors && !isNonEmpty(deptForm.name)">
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
                    <Button type="submit" :disabled="departments.createPending.value">
                      <Spinner v-if="departments.createPending.value" aria-hidden="true" />保存部门
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="deptListError" class="text-sm text-destructive" role="alert">{{ deptListError }}</p>
        <p v-else-if="orgActionError" class="text-sm text-destructive" role="alert">{{ orgActionError }}</p>
        <DataTable :columns="columns" :rows="deptRows" :row-key="rowKey" :loading="departments.pending.value" empty-message="暂无部门。可清空筛选或新建部门。">
          <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="部门" :detail-fields="baseDetailFields(row, '部门编码', '部门名称')" :actions="deptActions" />
          </template>
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
                    <Field :data-invalid="teamShowErrors && !isNonEmpty(teamForm.code)">
                      <FieldLabel for="team-code">班组编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="team-code" v-model="teamForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="teamShowErrors && !isNonEmpty(teamForm.name)">
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
                    <Button type="submit" :disabled="teams.createPending.value">
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
          <template #cell-actions="{ row }">
            <div class="flex items-center justify-end gap-1">
              <Button
                type="button"
                variant="ghost"
                size="sm"
                :disabled="!row.code"
                @click="openMembers(row)"
              >
                <UsersIcon aria-hidden="true" />管理成员
              </Button>
              <MasterDataRowActions :row="row" entity-label="班组" :detail-fields="baseDetailFields(row, '班组编码', '班组名称')" :actions="teamActions" />
            </div>
          </template>
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
                    <Field :data-invalid="shiftShowErrors && !isNonEmpty(shiftForm.code)">
                      <FieldLabel for="shift-code">班次编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-code" v-model="shiftForm.code" autocomplete="off" required />
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
                    </Field>
                    <Field>
                      <FieldLabel for="shift-paid">计薪时长（分钟） <span class="text-destructive">*</span></FieldLabel>
                      <Input id="shift-paid" v-model="shiftForm.paidMinutes" type="number" min="1" inputmode="numeric" />
                      <FieldDescription>扣除休息后的有效计薪分钟数，默认 480（8 小时）。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="shiftOpen = false">取消</Button>
                    <Button type="submit" :disabled="shifts.createPending.value">
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
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="班次" :detail-fields="baseDetailFields(row, '班次编码', '班次名称')" :actions="shiftActions" />
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
                    <Field :data-invalid="calShowErrors && !isNonEmpty(calForm.code)">
                      <FieldLabel for="cal-code">日历编码 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="cal-code" v-model="calForm.code" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="calShowErrors && !isNonEmpty(calForm.name)">
                      <FieldLabel for="cal-name">日历名称 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="cal-name" v-model="calForm.name" autocomplete="off" required />
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="calOpen = false">取消</Button>
                    <Button type="submit" :disabled="calendars.createPending.value">
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
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="工作日历" :detail-fields="baseDetailFields(row, '日历编码', '日历名称')" :actions="calActions" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="calPage" v-model:page-size="calPageSize" :total-items="calendars.total.value" />
      </TabsContent>

      <!-- 人员技能（列表只读 + 登记可写：工人选择器 + 技能编码 + 等级 + 生效日期） -->
      <TabsContent value="personnel-skill" class="grid gap-3">
        <Toolbar v-model:search="skillKeyword" search-placeholder="在当前页内筛选技能编码、名称">
          <template #actions>
            <Dialog v-model:open="skillOpen">
              <DialogTrigger as-child>
                <Button size="sm" type="button"><PlusIcon aria-hidden="true" />登记技能</Button>
              </DialogTrigger>
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>登记人员技能</DialogTitle>
                  <DialogDescription>为某位工人登记一项技能与等级，可选填生效日期。带 * 为必填项。</DialogDescription>
                </DialogHeader>
                <form class="grid gap-4" @submit.prevent="submitSkill">
                  <p v-if="skillAssignError" class="text-sm text-destructive" role="alert">{{ skillAssignError }}</p>
                  <FieldGroup class="grid gap-3 sm:grid-cols-2">
                    <Field class="sm:col-span-2" :data-invalid="skillShowErrors && !isNonEmpty(skillForm.userId)">
                      <FieldLabel for="skill-worker">工人 <span class="text-destructive">*</span></FieldLabel>
                      <WorkerSelect id="skill-worker" v-model="skillForm.userId" placeholder="搜索并选择工人" />
                    </Field>
                    <Field :data-invalid="skillShowErrors && !isNonEmpty(skillForm.skillCode)">
                      <FieldLabel for="skill-code">技能 <span class="text-destructive">*</span></FieldLabel>
                      <Input id="skill-code" v-model="skillForm.skillCode" autocomplete="off" required />
                    </Field>
                    <Field :data-invalid="skillShowErrors && !isNonEmpty(skillForm.level)">
                      <FieldLabel for="skill-level">等级 <span class="text-destructive">*</span></FieldLabel>
                      <Select v-model="skillForm.level">
                        <SelectTrigger id="skill-level"><SelectValue placeholder="请选择等级" /></SelectTrigger>
                        <SelectContent>
                          <SelectItem v-for="lvl in SKILL_LEVELS" :key="lvl.value" :value="lvl.value">{{ lvl.label }}</SelectItem>
                        </SelectContent>
                      </Select>
                    </Field>
                    <Field>
                      <FieldLabel for="skill-from">生效日期</FieldLabel>
                      <Input id="skill-from" v-model="skillForm.effectiveFrom" type="date" />
                      <FieldDescription>留空表示即时生效。</FieldDescription>
                    </Field>
                  </FieldGroup>
                  <DialogFooter>
                    <Button type="button" variant="outline" @click="skillOpen = false">取消</Button>
                    <Button type="submit" :disabled="skillAssignment.assignPending.value">
                      <Spinner v-if="skillAssignment.assignPending.value" aria-hidden="true" />登记技能
                    </Button>
                  </DialogFooter>
                </form>
              </DialogContent>
            </Dialog>
          </template>
        </Toolbar>
        <p v-if="skillListError" class="text-sm text-destructive" role="alert">{{ skillListError }}</p>
        <DataTable :columns="columns" :rows="skillRows" :row-key="rowKey" :loading="skills.resourcesPending.value" empty-message="暂无人员技能。可清空筛选或登记技能。">
          <template #cell-active="{ row }"><StatusBadge :value="row.active === false ? 'disabled' : 'active'" /></template>
          <template #cell-actions="{ row }">
            <MasterDataRowActions :row="row" entity-label="人员技能" :detail-fields="baseDetailFields(row, '技能编码', '技能名称')" :actions="skillActions" />
          </template>
        </DataTable>
        <DataTablePagination v-model:page="skillPage" v-model:page-size="skillPageSize" :total-items="skills.resourcesTotal.value" />
      </TabsContent>
    </Tabs>

    <TeamMembersDialog v-model:open="membersOpen" :team-code="membersTeam.code" :team-name="membersTeam.name" />
  </BusinessLayout>
</template>
