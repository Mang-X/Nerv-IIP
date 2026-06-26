<script setup lang="ts">
import type {
  CreateProductCategoryRequest,
  ProductCategoryItem,
} from '@/composables/usePromotedCatalogs'
import type { DataTableProColumn } from '@nerv-iip/ui'
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { useProductCategories } from '@/composables/usePromotedCatalogs'
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
  DataTablePaginationPro,
  DataTablePro,
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
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  StatusBadgePro,
  Toolbar,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, shallowRef, watch } from 'vue'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '产品分类' } })

const {
  archiveCategory,
  archivePending,
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

const columns: DataTableProColumn<ProductCategoryItem>[] = [
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
}

function blankForm(): ProductCategoryForm {
  return {
    categoryName: '',
    parentCode: '',
    description: '',
  }
}

const formOpen = shallowRef(false)
const showErrors = ref(false)
// null = 新建，否则为正在编辑分类的 categoryCode（编码即身份，编辑态只读）。
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
  editingCode.value = null
  Object.assign(form, blankForm())
  showErrors.value = false
  formOpen.value = true
}
function openEdit(row: ProductCategoryItem) {
  if (!row.categoryCode) return
  editingCode.value = row.categoryCode
  showErrors.value = false
  Object.assign(form, {
    categoryName: row.categoryName ?? '',
    parentCode: row.parentCode ?? '',
    description: row.description ?? '',
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
  }
  try {
    if (editingCode.value) {
      await updateCategory(editingCode.value, payload)
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
    editingCode.value = null
  }
  catch (error) {
    notifyError(error)
  }
}

// ── 停用 ────────────────────────────────────────────────────────
const archiveOpen = shallowRef(false)
const archiveTarget = shallowRef<ProductCategoryItem | null>(null)
function openArchive(row: ProductCategoryItem) {
  if (!row.categoryCode) return
  archiveTarget.value = row
  archiveOpen.value = true
}
async function confirmArchive() {
  const target = archiveTarget.value
  if (!target?.categoryCode) return
  try {
    await archiveCategory(target.categoryCode, '不再使用')
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
        <ButtonPro size="sm" variant="outline" type="button" :disabled="categoriesPending" @click="refresh">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <DialogPro v-model:open="formOpen">
          <DialogProTrigger as-child>
            <ButtonPro size="sm" type="button" @click="openCreate">
              <PlusIcon aria-hidden="true" />
              新建分类
            </ButtonPro>
          </DialogProTrigger>
          <DialogProContent class="sm:max-w-2xl">
            <DialogProHeader>
              <DialogProTitle>{{ editingCode ? '编辑产品分类' : '新建产品分类' }}</DialogProTitle>
              <DialogProDescription>
                产品分类是物料与产品的归类主数据：维护层级（上级分类）后，可在选型与统计中按分类树聚合。带 * 为必填项。
              </DialogProDescription>
            </DialogProHeader>
            <form class="grid gap-5" @submit.prevent="submitForm">
              <p v-if="showErrors && !canSubmit" class="text-sm text-destructive" role="alert">
                请填写分类名（已标红）。
              </p>

              <FormSectionTitle>基本信息</FormSectionTitle>
              <FieldProGroup class="grid gap-3 sm:grid-cols-2">
                <FieldPro :data-invalid="showErrors && !nameValid">
                  <FieldProLabel for="cat-name">分类名 <span class="text-destructive">*</span></FieldProLabel>
                  <InputPro id="cat-name" v-model="form.categoryName" placeholder="例如：结构件" />
                </FieldPro>
                <FieldPro v-if="editingCode">
                  <FieldProLabel>编码</FieldProLabel>
                  <InputPro :model-value="editingCode" readonly disabled />
                  <FieldProDescription>编码由系统自动生成，不可更改。</FieldProDescription>
                </FieldPro>
                <FieldPro>
                  <FieldProLabel for="cat-parent">上级分类</FieldProLabel>
                  <SelectPro v-model="form.parentCode">
                    <SelectProTrigger id="cat-parent"><SelectProValue placeholder="顶级分类（可空）" /></SelectProTrigger>
                    <SelectProContent>
                      <SelectProItem v-for="o in parentOptions" :key="o.value" :value="o.value">{{ o.label }}</SelectProItem>
                    </SelectProContent>
                  </SelectPro>
                  <FieldProDescription>选择上级以形成分类树；留空为顶级分类。</FieldProDescription>
                </FieldPro>
              </FieldProGroup>

              <FormSectionTitle>其它</FormSectionTitle>
              <FieldProGroup class="grid gap-3">
                <FieldPro>
                  <FieldProLabel for="cat-desc">说明</FieldProLabel>
                  <InputPro id="cat-desc" v-model="form.description" placeholder="可选，分类用途或范围" />
                </FieldPro>
              </FieldProGroup>

              <DialogProFooter>
                <ButtonPro type="button" variant="outline" @click="formOpen = false">取消</ButtonPro>
                <ButtonPro type="submit" :disabled="createPending || updatePending">
                  <Spinner v-if="createPending || updatePending" aria-hidden="true" />
                  {{ editingCode ? '保存修改' : '创建分类' }}
                </ButtonPro>
              </DialogProFooter>
            </form>
          </DialogProContent>
        </DialogPro>
      </template>
    </PageHeader>

    <Toolbar v-model:search="search" search-placeholder="按分类名或编码筛选" />

    <p v-if="listErrorMessage" class="text-sm text-destructive" role="alert">{{ listErrorMessage }}</p>

    <DataTablePro :pagination="false"
      :columns="columns"
      :rows="categories"
      row-key="categoryCode"
      :loading="categoriesPending"
      :searchable="false"
      :column-settings="false"
      empty-message="产品分类为空。新建分类（支持上级分类）以形成分类树，供选型与统计聚合。"
    >
      <template #cell-parent="{ row }">
        <span>{{ parentLabel(row) }}</span>
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

    <DataTablePaginationPro v-model:page="page" :page-size="pageSize" :total-items="categoriesTotal" @update:page-size="(v) => (pageSize = String(v))" />

    <AlertDialogPro v-model:open="archiveOpen">
      <AlertDialogProContent>
        <AlertDialogProHeader>
          <AlertDialogProTitle>停用产品分类</AlertDialogProTitle>
          <AlertDialogProDescription>
            停用后分类「{{ archiveTarget?.categoryName }}」将不可在新的选型中使用，已有引用不受影响。
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
