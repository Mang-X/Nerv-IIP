<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldDescription,
  NvFieldGroup,
  NvFieldLabel,
  NvInput,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  NvSpinner,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useOrganizationPage } from '../useOrganizationPage'

type View = ShallowUnwrapRef<ReturnType<typeof useOrganizationPage>>

defineProps<{
  NONE_PARENT: View['NONE_PARENT']
  canEditDept: View['canEditDept']
  deptActions: View['deptActions']
  deptEditCode: View['deptEditCode']
  deptEditForm: View['deptEditForm']
  deptEditLoading: View['deptEditLoading']
  deptEditShowErrors: View['deptEditShowErrors']
  deptParentOptions: View['deptParentOptions']
  isNonEmpty: View['isNonEmpty']
  submitEditDept: View['submitEditDept']
}>()

const deptEditOpen = defineModel<View['deptEditOpen']>('deptEditOpen', { required: true })

const deptEditParent = defineModel<View['deptEditParent']>('deptEditParent', { required: true })
</script>

<template>
  <NvDialog v-model:open="deptEditOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>编辑部门 · {{ deptEditCode }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">部门 {{ deptEditCode }}</NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitEditDept">
        <CarriedContextSummary
          label="部门标识"
          :items="[{ label: '部门编码', value: deptEditCode }]"
        />
        <p v-if="deptEditShowErrors && !canEditDept" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="deptEditShowErrors && !isNonEmpty(deptEditForm.name)">
            <NvFieldLabel for="dept-edit-name"
              >部门名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="dept-edit-name" v-model="deptEditForm.name" autocomplete="off" required />
          </NvField>
          <NvField>
            <NvFieldLabel for="dept-edit-parent">上级部门</NvFieldLabel>
            <NvSelect v-model="deptEditParent">
              <NvSelectTrigger id="dept-edit-parent"
                ><NvSelectValue placeholder="无（顶级部门）"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem :value="NONE_PARENT">无（顶级部门）</NvSelectItem>
                <NvSelectItem
                  v-for="d in deptParentOptions"
                  :key="d.code"
                  :value="d.code ?? NONE_PARENT"
                >
                  {{ d.displayName ?? d.code }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
            <!-- 层级归属约束（非显而易见），保留一行。 -->
            <NvFieldDescription>不能挂到自己或其下级部门下。</NvFieldDescription>
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="deptEditOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="deptActions.updatePending.value || deptEditLoading">
            <NvSpinner v-if="deptActions.updatePending.value" aria-hidden="true" />保存修改
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
