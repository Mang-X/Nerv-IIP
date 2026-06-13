<script setup lang="ts">
import type {
  CreateProductCategoryRequest,
  ProductCategoryItem,
} from '@/composables/usePromotedCatalogs'
import type { DataTableColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useProductCategories } from '@/composables/usePromotedCatalogs'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  Button,
  Checkbox,
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
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Spinner,
  StatusBadge,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '产品分类' } })

const {
  archiveCategory,
  archivePending,
  backendReady,
  categories,
  categoriesError,
  categoriesPending,
  categoriesTotal,
  createCategory,
  createPending,
  filters,
  refresh,
  updateCategory,
  updatePending,
} = useProductCategories()

// 上级分类名称解析：编码 → 分类名，用于列表与表单显名。
const categoryNameByCode = computed(() => {
  const map = new Map<string, string>()
  for (const item of categories.value) {
    if (item.categoryCode) map.set(item.categoryCode, item.categoryName ?? item.categoryCode)
  }
  return map
})
function parentLabel(row: ProductCategoryItem) {
  if (row.parentName) return row.parentName
  if (row.parentCode) return categoryNameByCode.value.get(row.parentCode) ?? row.parentCode
  return '顶级'
}

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
  categoriesError.value instanceof Error ? categoriesError.value.message : '',
)

const columns: DataTableColumn<ProductCategoryItem>[] = [
  { key: 'categoryCode', header: '编码', width: 'w-32' },
  { key: 'categoryName', header: '分类名', cellClass: 'font-medium' },
  { key: 'parent', header: '上级分类' },
  { key: 'description', header: '说明' },
  { key: 'status', header: '状态', width: 'w-24' },
  { key: 'actions', header: '操作', align: 'end', width: 'w-24' },
]

// ── 新建 / 编辑表单 ─────────────────────────────────────────────
interface ProductCategoryForm {
  categoryName: string
  parentCode: string
  description: string
  enabled: boolean
}

function blankForm(): ProductCategoryForm {
  return {
    categoryName: '',
    parentCode: '',
    description: '',
    enabled: true,
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑分类的记录标识。
const editingId = shallowRef<string | null>(null)
const editingCode = shallowRef<string | null>(null)
const form = reactive<ProductCategoryForm>(blankForm())

const nameValid = computed(() => form.categoryName.trim().length > 0)
const canSubmit = computed(() => nameValid.value)

// 上级分类候选：仅取有编码的分类，编辑时排除自身避免自引用。
const parentOptions = computed(() =>
  categories.value
    .filter((item) => item.categoryCode && item.categoryCode !== editingCode.value)
    .map((item) => ({
      value: item.categoryCode as string,
      label: `${item.categoryName ?? item.categoryCode} · ${item.categoryCode}`,
    })),
)

function openCreate() {
  editingId.value = null
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: ProductCategoryItem) {
  if (!row.id) return
  editingId.value = row.id
  editingCode.value = row.categoryCode ?? null
  showErrors.value = false
  Object.assign(form, {
    categoryName: row.categoryName ?? '',
    parentCode: row.parentCode ?? '',
    description: row.description ?? '',
    enabled: row.enabled ?? true,
  })
  formOpen.value = true
}

async function submitForm() {
  if (!canSubmit.value) {
    showErrors.value = true
    return
  }
  const payload = {
    categoryName: form.categoryName.trim(),
    parentCode: form.parentCode.trim() || null,
    description: form.description.trim() || null,
    enabled: form.enabled,
  }
  try {
    if (editingId.value) {
      await updateCategory(editingId.value, payload)
      notifySuccess(`分类「${payload.categoryName}」已更新。`)
    }
    else {
      const body: CreateProductCategoryRequest = {
        organizationId: filters.organizationId,
        environmentId: filters.environmentId,
        ...payload,
      }
      await createCategory(body)
      notifySuccess(`已创建产品分类「${payload.categoryName}」。`)
    }
    showErrors.value = false
    formOpen.value = false
    editingId.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 停用 ────────────────────────────────────────────────────────
const archiveOpen = shallowRef(false)
const archiveTarget = shallowRef<ProductCategoryItem | null>(null)
function openArchive(row: ProductCategoryItem) {
  if (!row.id) return
  archiveTarget.value = row
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.id) return
  try {
    await archiveCategory(target.id, '不再使用')
    notifySuccess(`分类「${target.categoryName}」已停用。`)
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
      title="产品分类"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${categoriesTotal} 个分类`"
    >
      <template #actions>
        <Button size="sm" variant="outline" type="button" :disabled="categoriesPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </Button>
        <Dialog v-model:open="formOpen">
          <DialogTrigger as-child>
            <Button size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建分类
            </Button>
          </DialogTrigger>
          <DialogContent class="sm:max-w-2xl">
            <DialogHeader>
              <DialogTitle>{{ editingId ? '编辑产品分类' : '新建产品分类' }}</DialogTitle>
              <DialogDescription>
                产品分类是物料与产品的归类主数据：维护层级（上级分类）后，可在选型与统计中按分类树聚合。带 * 为必填项。
              </DialogDescription>
            </DialogHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写分类名（已标红）。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <FieldGroup class="grid gap-3 sm:grid-cols-2">
                <Field :data-invalid="showErrors && !nameValid">
                  <FieldLabel for="cat-name">分类名 <span class="text-destructive">*</span></FieldLabel>
                  <Input id="cat-name" v-model="form.categoryName" placeholder="例如：结构件" />
                </Field>
                <Field v-if="editingCode">
                  <FieldLabel>编码</FieldLabel>
                  <Input :model-value="editingCode" readonly disabled />
                  <FieldDescription>编码由系统自动生成，不可更改。</FieldDescription>
                </Field>
                <Field>
                  <FieldLabel for="cat-parent">上级分类</FieldLabel>
                  <Select v-model="form.parentCode">
                    <SelectTrigger id="cat-parent"><SelectValue placeholder="顶级分类（可空）" /></SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="o in parentOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectItem>
                    </SelectContent>
                  </Select>
                  <FieldDescription>选择上级以形成分类树；留空为顶级分类。</FieldDescription>
                </Field>
              </FieldGroup>

              <FormSectionTitle>其它</FormSectionTitle>
              <FieldGroup class="grid gap-3">
                <Field>
                  <FieldLabel for="cat-desc">说明</FieldLabel>
                  <Input id="cat-desc" v-model="form.description" placeholder="可选，分类用途或范围" />
                </Field>
                <Field class="self-start">
                  <FieldLabel>启用</FieldLabel>
                  <label for="cat-enabled" class="flex h-9 cursor-pointer select-none items-center justify-between rounded-md border bg-background px-3 text-sm">
                    <span>停用后不可在选型中使用</span>
                    <Checkbox id="cat-enabled" v-model:checked="form.enabled" />
                  </label>
                </Field>
              </FieldGroup>

              <DialogFooter>
                <Button type="button" variant="outline" @click="formOpen = false">取消</Button>
                <Button type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingId ? '保存修改' : '创建分类' }}
                </Button>
              </DialogFooter>
            </form>
          </DialogContent>
        </Dialog>
      </template>
    </PageHeader>

    <p
      v-if="!backendReady"
      class="rounded-md border border-warning/30 bg-warning/10 p-3 text-sm text-warning"
      role="status"
    >
      页面建设中：产品分类主数据正在后端实现（#397）。当前为 IA / 表单预览，列表为空、保存暂不可用；完整层级树视图随后端交付完善。
    </p>

    <Toolbar v-model:search="search" search-placeholder="按分类名或编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTable
      :columns="columns"
      :rows="categories"
      row-key="id"
      :loading="categoriesPending"
      empty-message="产品分类为空。后端 #397 交付后，在此维护分类树（支持上级分类）。"
    >
      <template #cell-parent="{ row }">
        <span>{{ parentLabel(row) }}</span>
      </template>
      <template #cell-status="{ row }">
        <StatusBadge
          :label="row.enabled === false ? '停用' : '启用'"
          :tone="row.enabled === false ? 'neutral' : 'success'"
        />
      </template>
      <template #cell-actions="{ row }">
        <div class="flex justify-end gap-1">
          <Button type="button" variant="ghost" size="sm" @click="openEdit(row)">编辑</Button>
          <Button type="button" variant="ghost" size="sm" :disabled="row.enabled === false" @click="openArchive(row)">停用</Button>
        </div>
      </template>
    </DataTable>

    <DataTablePagination v-model:page="page" v-model:page-size="pageSize" :total-items="categoriesTotal" />

    <AlertDialog v-model:open="archiveOpen">
      <AlertDialogContent>
        <AlertDialogHeader>
          <AlertDialogTitle>停用产品分类</AlertDialogTitle>
          <AlertDialogDescription>
            停用后分类「{{ archiveTarget?.categoryName }}」将不可在新的选型中使用，已有引用不受影响。
          </AlertDialogDescription>
        </AlertDialogHeader>
        <AlertDialogFooter>
          <AlertDialogCancel>取消</AlertDialogCancel>
          <AlertDialogAction :disabled="archivePending" @click="confirmArchive">
            <Spinner v-if="archivePending" aria-hidden="true" />
            确认停用
          </AlertDialogAction>
        </AlertDialogFooter>
      </AlertDialogContent>
    </AlertDialog>
  </BusinessLayout>
</template>
