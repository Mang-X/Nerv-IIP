<script setup lang="ts">
import type {
  CreateSkillCatalogRequest,
  SkillCatalogItem,
} from '@/composables/usePromotedCatalogs'
import type { DataTableProColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useSkillCatalog } from '@/composables/usePromotedCatalogs'
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
  ButtonPro,
  CheckboxPro,
  DataTablePro,
  DataTablePaginationPro,
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
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '技能目录' } })

const {
  archiveSkill,
  archivePending,
  createSkill,
  createPending,
  filters,
  refresh,
  skills,
  skillsError,
  skillsPending,
  skillsTotal,
  updateSkill,
  updatePending,
} = useSkillCatalog()

// Toolbar 搜索绑定到 search 筛选（空串不污染查询）。
const search = computed({
  get: () => filters.search ?? '',
  set: (value: string) => { filters.search = value.trim() ? value : undefined },
})

const page = ref(1)
const pageSize = ref('10')
const pageSizeNumber = computed(() => Number(pageSize.value) || 10)
watch([page, pageSize], () => {
  filters.skip = (page.value - 1) * pageSizeNumber.value
  filters.take = pageSizeNumber.value
}, { immediate: true })

const listErrorMessage = computed(() =>
  skillsError.value instanceof Error ? skillsError.value.message : '',
)

const columns: DataTableProColumn<SkillCatalogItem>[] = [
  { key: 'skillCode', header: '编码', width: 'w-32' },
  { key: 'skillName', header: '技能', cellClass: 'font-medium' },
  { key: 'groupName', header: '技能组' },
  { key: 'cert', header: '证书', width: 'w-40' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

// ── 新建 / 编辑表单 ─────────────────────────────────────────────
interface SkillCatalogForm {
  skillName: string
  groupName: string
  requiresCertification: boolean
  validityMonths: string
  description: string
}

function blankForm(): SkillCatalogForm {
  return {
    skillName: '',
    groupName: '',
    requiresCertification: false,
    validityMonths: '',
    description: '',
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑技能的 skillCode（编码即身份，编辑态只读）。
const editingCode = shallowRef<string | null>(null)
const form = reactive<SkillCatalogForm>(blankForm())

function parseNumber(value: string): number | undefined {
  const trimmed = value.trim()
  if (!trimmed) return undefined
  const parsed = Number(trimmed)
  return Number.isFinite(parsed) ? parsed : undefined
}
const validityMonths = computed(() => parseNumber(form.validityMonths))

const nameValid = computed(() => form.skillName.trim().length > 0)
const groupValid = computed(() => form.groupName.trim().length > 0)
const validityValid = computed(() => form.validityMonths.trim() === '' || (validityMonths.value != null && validityMonths.value >= 0))
const canSubmit = computed(() => nameValid.value && groupValid.value && validityValid.value)

function openCreate() {
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: SkillCatalogItem) {
  if (!row.skillCode) return
  editingCode.value = row.skillCode
  showErrors.value = false
  Object.assign(form, {
    skillName: row.skillName ?? '',
    groupName: row.groupName ?? '',
    requiresCertification: row.requiresCertification ?? false,
    validityMonths: row.validityMonths == null ? '' : String(row.validityMonths),
    description: row.description ?? '',
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const shared = {
    skillName: form.skillName.trim(),
    groupName: form.groupName.trim(),
    requiresCertification: form.requiresCertification,
    validityMonths: validityMonths.value ?? null,
    description: form.description.trim() || null,
  }
  try {
    if (editingCode.value) {
      await updateSkill(editingCode.value, shared)
      notifySuccess(`技能「${shared.skillName}」已更新。`)
    }
    else {
      const body: CreateSkillCatalogRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...shared,
      }
      await createSkill(body)
      notifySuccess(`已创建技能「${shared.skillName}」。`)
    }
    showErrors.value = false
    formOpen.value = false
    editingCode.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 停用 ────────────────────────────────────────────────────────
const archiveOpen = shallowRef(false)
const archiveTarget = shallowRef<SkillCatalogItem | null>(null)
function openArchive(row: SkillCatalogItem) {
  if (!row.skillCode) return
  archiveTarget.value = row
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.skillCode) return
  try {
    await archiveSkill(target.skillCode, '不再使用')
    notifySuccess(`技能「${target.skillName}」已停用。`)
    archiveOpen.value = false
    archiveTarget.value = null
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader
      title="技能目录"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${skillsTotal} 项技能`"
    >
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="skillsPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建技能
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>{{ editingCode ? '编辑技能' : '新建技能' }}</DialogProTitle>
              <DialogProDescription>
                技能目录维护技能定义（技能组 + 证书有效期），是可复用的基础数据；与「人员技能」矩阵（人员-技能登记）是两件事。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写技能名与技能组，并确保证书有效期为非负数（已标红）。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !nameValid">
                  <FieldProLabel for="skill-name">技能名 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="skill-name" v-model="form.skillName" placeholder="例如：CNC 编程" />
                </FieldPro>
                <FieldPro v-if="editingCode">
                  <FieldProLabel>编码</FieldProLabel>
                  <InputPro :model-value="editingCode" readonly disabled />
                  <FieldProDescription>编码由系统自动生成。</FieldProDescription>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !groupValid">
                  <FieldProLabel for="skill-group">技能组 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="skill-group" v-model="form.groupName" placeholder="例如：机加工" />
                </FieldPro>
              </FieldProGroup>

              <FormSectionTitle>证书</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro class="self-start">
                  <FieldProLabel>需要证书</FieldProLabel>
                  <label for="skill-cert" class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm">
                    <span>该技能需持证上岗</span>
                    <CheckboxPro id="skill-cert" v-model:checked="form.requiresCertification" />
                  </label>
                </FieldPro>
                <FieldPro :data-invalid="showErrors && !validityValid">
                  <FieldProLabel for="skill-validity">证书有效期（月）</FieldProLabel>
                  <InputPro id="skill-validity" v-model="form.validityMonths" type="number" min="0" placeholder="0" />
                  <FieldProDescription>需证书时填写；到期需复评。</FieldProDescription>
                </FieldPro>
              </FieldProGroup>

              <FormSectionTitle>其它</FormSectionTitle>
              <FieldProGroup class="grid gap-3">
                <FieldPro>
                  <FieldProLabel for="skill-desc">说明</FieldProLabel>
                  <InputPro id="skill-desc" v-model="form.description" placeholder="可选，技能用途或评定标准" />
                </FieldPro>
              </FieldProGroup>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '创建技能' }}
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="search" search-placeholder="按技能名或编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro
      :columns="columns"
      :rows="skills"
      row-key="skillCode"
      :loading="skillsPending"
      empty-message="技能目录为空。在此维护技能定义（技能组 + 证书有效期），人员技能登记即可选用。"
      :searchable="false"
      :column-settings="false"
    >
      <template #cell-groupName="{ row }">
        <span>{{ row.groupName || '—' }}</span>
      </template>
      <template #cell-cert="{ row }">
        <StatusBadgePro
          v-if="row.requiresCertification"
          :label="row.validityMonths != null ? `需证书 · ${row.validityMonths}个月` : '需证书'"
          tone="warning"
        />
        <span v-else class="text-muted-foreground">免证</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadgePro
          :label="row.enabled === false ? '停用' : '启用'"
          :tone="row.enabled === false ? 'neutral' : 'success'"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <ButtonPro type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</ButtonPro>
          <ButtonPro type="button" variant="ghost" size="sm" :disabled="row.enabled === false" @click="openArchive(row)">停用</ButtonPro>
        </div>
      </template>
    </DataTablePro>

    <DataTablePaginationPro v-model:page="page" :page-size="pageSize" :total-items="skillsTotal" @update:page-size="(v) => (pageSize = String(v))" />

    <AlertDialogPro v-model:open="archiveOpen">
      <AlertDialogProContent>
        <AlertDialogProHeader>
          <AlertDialogProTitle>停用技能</AlertDialogProTitle>
          <AlertDialogProDescription>
            停用后技能「{{ archiveTarget?.skillName }}」将不可在人员技能登记中选用，已登记记录不受影响。
          </AlertDialogProDescription>
        </AlertDialogProHeader>
        <AlertDialogProFooter>
          <AlertDialogProCancel>取消</AlertDialogProCancel>
          <AlertDialogProAction :disabled="archivePending" @click="confirmArchive">
            <Spinner v-if="archivePending" aria-hidden="true" />
            确认停用
          </AlertDialogProAction>
        </AlertDialogProFooter>
      </AlertDialogProContent>
    </AlertDialogPro>
  </BusinessLayout>
</template>
