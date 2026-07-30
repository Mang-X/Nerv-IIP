<script setup lang="ts">
import type { BusinessConsoleWorkerDirectoryItem } from '@nerv-iip/api-client'
import type { NvDataTableColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useMasterDataResource, useWorkerRegistry } from '@/composables/useBusinessMasterData'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  NvAlertDialog,
  NvAlertDialogAction,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvDataTable,
  NvDialog,
  NvDialogContent,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvDialogTrigger,
  NvField,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvPageHeader,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
  NvToolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { friendlyErrorMessage, notifyOperationFailure, notifySuccess } from '@/utils/notify'

definePage({
  meta: {
    requiresAuth: true,
    title: '员工',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const {
  create,
  createPending,
  disable,
  disablePending,
  enable,
  enablePending,
  filters,
  refresh,
  update,
  updatePending,
  workers,
  workersError,
  workersPending,
  workersTotal,
} = useWorkerRegistry()

const { items: departments } = useMasterDataResource('department')

/** 在岗状态受控值——与后端 Worker.EmploymentStatus 常量一一对应。 */
const EMPLOYMENT_STATUS = [
  { value: 'active', label: '在岗', tone: 'success' as const },
  { value: 'on-leave', label: '休假', tone: 'warning' as const },
  { value: 'resigned', label: '离职', tone: 'neutral' as const },
]
function statusLabel(value?: string | null) {
  return EMPLOYMENT_STATUS.find((x) => x.value === value)?.label ?? '在岗'
}
function statusTone(value?: string | null) {
  return EMPLOYMENT_STATUS.find((x) => x.value === value)?.tone ?? 'success'
}

const search = computed({
  get: () => filters.keyword ?? '',
  set: (value: string) => {
    filters.keyword = value.trim() ? value.trim() : undefined
  },
})

const departmentFilter = shallowRef('all')
watch(departmentFilter, (value) => {
  filters.departmentCode = value === 'all' ? undefined : value
  filters.pageIndex = 1
})

const page = ref(1)
const pageSize = ref('20')
watch([page, pageSize], () => {
  filters.pageIndex = page.value
  filters.pageSize = Number(pageSize.value) || 20
})

// 非 Error 形态的 rejection 也必须显示出来，否则错误横幅整条消失、页面退化成空态把故障吞掉。
const listErrorMessage = computed(() =>
  workersError.value
    ? friendlyErrorMessage(workersError.value, '人员名册加载失败，请刷新重试。')
    : '',
)

const columns: NvDataTableColumn<BusinessConsoleWorkerDirectoryItem>[] = [
  { key: 'employeeNo', header: '工号', width: 'w-32' },
  { key: 'displayName', header: '姓名', cellClass: 'font-medium' },
  { key: 'jobTitle', header: '岗位', width: 'w-40' },
  { key: 'departmentName', header: '部门', width: 'w-36' },
  { key: 'teams', header: '班组' },
  { key: 'skills', header: '技能' },
  { key: 'employmentStatus', header: '在岗状态', width: 'w-28' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-32' },
]

interface WorkerForm {
  name: string
  departmentCode: string
  jobTitle: string
  employmentStatus: string
  phone: string
}

function blankForm(): WorkerForm {
  return { name: '', departmentCode: '', jobTitle: '', employmentStatus: 'active', phone: '' }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建；否则为正在编辑员工的工号（工号即身份，编辑态只读）。
const editingCode = shallowRef<string | null>(null)
const form = reactive<WorkerForm>(blankForm())

const nameValid = computed(() => form.name.trim().length > 0)
const canSubmit = computed(() => nameValid.value)

function openCreate() {
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}

function openEdit(row: BusinessConsoleWorkerDirectoryItem) {
  if (!row.employeeNo) return
  editingCode.value = row.employeeNo
  showErrors.value = false
  Object.assign(form, {
    name: row.displayName ?? '',
    departmentCode: row.departmentCode ?? '',
    jobTitle: row.jobTitle ?? '',
    employmentStatus: row.employmentStatus ?? 'active',
    phone: row.phone ?? '',
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const name = form.name.trim()
  try {
    if (editingCode.value) {
      await update(editingCode.value, {
        name,
        departmentCode: form.departmentCode || null,
        jobTitle: form.jobTitle.trim() || null,
        employmentStatus: form.employmentStatus,
        phone: form.phone.trim() || null,
      })
      notifySuccess(`员工「${name}」已更新。`)
    } else {
      await create({
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        code: null,
        name,
        userId: null,
        departmentCode: form.departmentCode || null,
        jobTitle: form.jobTitle.trim() || null,
        employmentStatus: form.employmentStatus,
        phone: form.phone.trim() || null,
      })
      notifySuccess(`已入职员工「${name}」。`)
    }
    showErrors.value = false
    formOpen.value = false
    editingCode.value = null
  } catch (error) {
    notifyOperationFailure('保存员工失败', error, '保存员工失败，请稍后重试。')
  }
}

const disableOpen = shallowRef(false)
const disableTarget = shallowRef<BusinessConsoleWorkerDirectoryItem | null>(null)
function openDisable(row: BusinessConsoleWorkerDirectoryItem) {
  if (!row.employeeNo) return
  disableTarget.value = row
  disableOpen.value = true
}
async function confirmDisable() {
  const target = disableTarget.value
  if (!target?.employeeNo) return
  try {
    await disable(target.employeeNo)
    notifySuccess(`员工「${target.displayName}」已停用。`)
    disableOpen.value = false
    disableTarget.value = null
  } catch (error) {
    notifyOperationFailure('停用员工失败', error, '停用员工失败，请稍后重试。')
  }
}
async function restore(row: BusinessConsoleWorkerDirectoryItem) {
  if (!row.employeeNo) return
  try {
    await enable(row.employeeNo)
    notifySuccess(`员工「${row.displayName}」已恢复。`)
  } catch (error) {
    notifyOperationFailure('恢复员工失败', error, '恢复员工失败，请稍后重试。')
  }
}
</script>

<template>
  <BusinessLayout>
    <NvPageHeader title="员工" :breadcrumbs="[{ label: '基础数据' }]" :count="`${workersTotal} 人`">
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="workersPending"
          @click="refresh"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvDialog v-model:open="formOpen">
          <NvDialogTrigger as-child>
            <NvButton size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新增员工
            </NvButton>
          </NvDialogTrigger>
          <NvDialogContent class="sm:max-w-2xl">
            <NvDialogHeader>
              <NvDialogTitle>{{ editingCode ? '编辑员工' : '新增员工' }}</NvDialogTitle>
            </NvDialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写姓名。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField :data-invalid="showErrors && !nameValid">
                  <NvFieldLabel for="worker-name"
                    >姓名 <span class="text-destructive">*</span></NvFieldLabel
                  >
                  <NvInput id="worker-name" v-model="form.name" />
                </NvField>
                <NvField v-if="editingCode">
                  <NvFieldLabel>工号</NvFieldLabel>
                  <NvInput :model-value="editingCode" readonly disabled />
                </NvField>
                <NvField>
                  <NvFieldLabel for="worker-department">部门</NvFieldLabel>
                  <NvSelect v-model="form.departmentCode">
                    <NvSelectTrigger id="worker-department">
                      <NvSelectValue placeholder="选择部门" />
                    </NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="item in departments"
                        :key="item.code ?? ''"
                        :value="item.code ?? ''"
                        >{{ item.displayName ?? item.code }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField>
                  <NvFieldLabel for="worker-title">岗位</NvFieldLabel>
                  <NvInput id="worker-title" v-model="form.jobTitle" />
                </NvField>
              </NvFieldGroup>

              <FormSectionTitle>在岗与联系方式</FormSectionTitle>
              <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
                <NvField>
                  <NvFieldLabel for="worker-status">在岗状态</NvFieldLabel>
                  <NvSelect v-model="form.employmentStatus">
                    <NvSelectTrigger id="worker-status">
                      <NvSelectValue />
                    </NvSelectTrigger>
                    <NvSelectContent>
                      <NvSelectItem
                        v-for="option in EMPLOYMENT_STATUS"
                        :key="option.value"
                        :value="option.value"
                        >{{ option.label }}</NvSelectItem
                      >
                    </NvSelectContent>
                  </NvSelect>
                </NvField>
                <NvField>
                  <NvFieldLabel for="worker-phone">联系电话</NvFieldLabel>
                  <NvInput id="worker-phone" v-model="form.phone" />
                </NvField>
              </NvFieldGroup>

              <NvDialogFooter>
                <NvButton type="button" variant="outline" @click="formOpen = false">取消</NvButton>
                <NvButton type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '创建员工' }}
                </NvButton>
              </NvDialogFooter>
            </form>
          </NvDialogContent>
        </NvDialog>
      </template>
    </NvPageHeader>

    <NvToolbar v-model:search="search" search-placeholder="按姓名或工号筛选">
      <NvSelect v-model="departmentFilter">
        <NvSelectTrigger class="w-44"><NvSelectValue placeholder="全部部门" /></NvSelectTrigger>
        <NvSelectContent>
          <NvSelectItem value="all">全部部门</NvSelectItem>
          <NvSelectItem
            v-for="item in departments"
            :key="item.code ?? ''"
            :value="item.code ?? ''"
            >{{ item.displayName ?? item.code }}</NvSelectItem
          >
        </NvSelectContent>
      </NvSelect>
    </NvToolbar>

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">
      {{ listErrorMessage }}
    </p>

    <NvDataTable
      manual
      :page="page"
      :page-size="pageSize"
      :total-items="workersTotal"
      @update:page="page = $event"
      @update:page-size="(v) => (pageSize = String(v))"
      :columns="columns"
      :rows="workers"
      row-key="employeeNo"
      :loading="workersPending"
      empty-message="尚未维护员工。新增员工后即可编入班组、登记技能，并在派工时选人。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-jobTitle="{ row }">
        <span>{{ row.jobTitle || '—' }}</span>
      </template>
      <template #cell-departmentName="{ row }">
        <span>{{ row.departmentName || row.departmentCode || '—' }}</span>
      </template>
      <template #cell-teams="{ row }">
        <div v-if="row.teams?.length" class="flex flex-wrap gap-1">
          <NvStatusBadge
            v-for="team in row.teams"
            :key="team.teamCode"
            :label="team.isLeader ? `${team.teamName} · 组长` : (team.teamName ?? '')"
            :tone="team.isLeader ? 'info' : 'neutral'"
          />
        </div>
        <span v-else class="text-muted-foreground">未编组</span>
      </template>
      <template #cell-skills="{ row }">
        <div v-if="row.skills?.length" class="flex flex-wrap gap-1">
          <NvStatusBadge
            v-for="skill in row.skills"
            :key="skill.skillCode"
            :label="skill.skillName ?? ''"
            tone="neutral"
          />
        </div>
        <span v-else class="text-muted-foreground">未登记</span>
      </template>
      <template #cell-employmentStatus="{ row }">
        <NvStatusBadge v-if="row.active === false" label="已停用" tone="neutral" />
        <NvStatusBadge
          v-else
          :label="statusLabel(row.employmentStatus)"
          :tone="statusTone(row.employmentStatus)"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <NvButton type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</NvButton>
          <NvButton
            v-if="row.active === false"
            type="button"
            variant="ghost"
            size="sm"
            :disabled="enablePending"
            @click="restore(row)"
            >恢复</NvButton
          >
          <NvButton v-else type="button" variant="ghost" size="sm" @click="openDisable(row)"
            >停用</NvButton
          >
        </div>
      </template>
    </NvDataTable>

    <NvAlertDialog v-model:open="disableOpen">
      <NvAlertDialogContent>
        <NvAlertDialogHeader>
          <NvAlertDialogTitle>停用员工</NvAlertDialogTitle>
          <NvAlertDialogDescription>
            停用后「{{ disableTarget?.displayName }}」不再出现在派工与班组候选中，历史记录保留。
          </NvAlertDialogDescription>
        </NvAlertDialogHeader>
        <NvAlertDialogFooter>
          <NvAlertDialogCancel>取消</NvAlertDialogCancel>
          <NvAlertDialogAction :disabled="disablePending" @click="confirmDisable">
            <Spinner v-if="disablePending" aria-hidden="true" />
            确认停用
          </NvAlertDialogAction>
        </NvAlertDialogFooter>
      </NvAlertDialogContent>
    </NvAlertDialog>
  </BusinessLayout>
</template>
