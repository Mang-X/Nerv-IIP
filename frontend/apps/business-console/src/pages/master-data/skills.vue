<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import type { DataTableColumn } from '@nerv-iip/ui'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import { useBusinessMasterDataResources, useBusinessWorkers, useMasterDataResourceActions, usePersonnelSkillAssignment } from '@/composables/useBusinessMasterData'
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
  Toolbar,
} from '@nerv-iip/ui'
import { LayoutGridIcon, PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { formatDateTime } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '人员技能' } })

// 人员技能：列表只读 + 登记可写（工人选择器 + 技能编码 + 等级）。
const skills = useBusinessMasterDataResources('personnel-skill')
const skillAssignment = usePersonnelSkillAssignment()
const skillActions = useMasterDataResourceActions('personnel-skill')
// 工人目录用于「在岗工人数」概览（与登记技能的工人选择器同源）。
const workers = useBusinessWorkers()
const workersTotal = computed(() => workers.workersTotal.value)

const columns: DataTableColumn<BusinessConsoleResourceItem>[] = [
  { key: 'code', header: '编码', cellClass: 'font-medium', accessor: (r) => r.code ?? '无' },
  { key: 'displayName', header: '名称', accessor: (r) => r.displayName ?? '无' },
  { key: 'active', header: '状态', width: 'w-24' },
  { key: 'snapshotVersion', header: '更新时间', width: 'w-40', accessor: (r) => formatDateTime(r.snapshotVersion) },
  { key: 'actions', header: '操作', align: 'end', width: 'w-16' },
]
function baseDetailFields(row: BusinessConsoleResourceItem) {
  return [
    { label: '技能编码', value: row.code ?? '' },
    { label: '技能名称', value: row.displayName ?? '' },
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

const skillKeyword = ref('')
const skillPage = ref(1)
const skillPageSize = ref('10')
const skillRows = computed(() => filterRows(skills.resources.value, skillKeyword.value))
const skillListError = computed(() => formatError(skills.resourcesError.value))
watch([skillKeyword, skillPageSize], () => { skillPage.value = 1 })
watch([skillPage, skillPageSize], () => {
  skills.filters.skip = (skillPage.value - 1) * (Number(skillPageSize.value) || 10)
  skills.filters.take = Number(skillPageSize.value) || 10
}, { immediate: true })

// 技能等级取自字典 `skill-level`（系统枚举），Phase 1 用前端常量兜底中文。
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
watch(skillOpen, (open) => {
  if (open) {
    skillShowErrors.value = false
    Object.assign(skillForm, { userId: '', skillCode: '', level: '', effectiveFrom: '' })
  }
})
function refreshAll() {
  void skills.refreshResources()
}
async function submitSkill() {
  if (!canAssignSkill.value) {
    skillShowErrors.value = true
    return
  }
  try {
    await skillAssignment.assign({
      userId: skillForm.userId,
      skillCode: skillForm.skillCode.trim(),
      level: skillForm.level,
      effectiveFrom: skillForm.effectiveFrom.trim() || undefined,
    })
    notifySuccess('已登记人员技能。')
    skillShowErrors.value = false
    skillOpen.value = false
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="人员技能" :breadcrumbs="[{ label: '基础数据' }]" :count="`${skills.resourcesTotal.value} 项技能`">
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="skills.resourcesPending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      为工人登记技能与等级，用于派工上岗资格校验。工人来自系统用户，选择时按姓名 / 工号检索。
    </p>

    <SectionCards :columns="2">
      <SectionCard description="技能项数" :value="skills.resourcesTotal.value" hint="可登记的技能字典项" />
      <SectionCard description="在岗工人数" :value="workersTotal" hint="可被登记技能的系统用户" />
    </SectionCards>

    <div class="flex flex-wrap items-center justify-between gap-2 rounded-md border border-dashed border-border bg-muted/30 px-3 py-2">
      <p class="text-sm text-muted-foreground">技能矩阵视图（工人 × 技能，等级 / 有效期一屏可读）</p>
      <Button size="sm" variant="outline" type="button" disabled>
        <LayoutGridIcon aria-hidden="true" />
        矩阵视图 · 建设中
      </Button>
    </div>
    <p class="text-xs text-muted-foreground">矩阵视图待后端按工人聚合的技能明细上线后开放；当前以平表维护技能项并登记。</p>

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
              <p v-if="skillShowErrors && !canAssignSkill" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
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
        <MasterDataRowActions :row="row" entity-label="人员技能" :detail-fields="baseDetailFields(row)" :actions="skillActions" />
      </template>
    </DataTable>
    <DataTablePagination v-model:page="skillPage" v-model:page-size="skillPageSize" :total-items="skills.resourcesTotal.value" />
  </BusinessLayout>
</template>
