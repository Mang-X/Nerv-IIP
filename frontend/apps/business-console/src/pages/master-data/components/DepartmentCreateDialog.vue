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
  canCreateDept: View['canCreateDept']
  departments: View['departments']
  deptForm: View['deptForm']
  deptParentLocked: View['deptParentLocked']
  deptParentName: View['deptParentName']
  deptShowErrors: View['deptShowErrors']
  isNonEmpty: View['isNonEmpty']
  submitCreateDept: View['submitCreateDept']
}>()

const deptCreateOpen = defineModel<View['deptCreateOpen']>('deptCreateOpen', { required: true })
</script>

<template>
  <NvDialog v-model:open="deptCreateOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{ deptParentLocked ? '新建子部门' : '新建部门' }}</NvDialogTitle>
        <!-- 上级归属已在只读上下文区呈现；此处仅供读屏播报。 -->
        <NvDialogDescription class="sr-only">
          {{ deptParentLocked ? `上级部门：${deptParentName}` : '新建顶级部门' }}
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitCreateDept">
        <CarriedContextSummary
          v-if="deptParentLocked"
          label="上级归属"
          :items="[{ label: '上级部门', value: deptParentName }]"
        />
        <p v-if="deptShowErrors && !canCreateDept" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="deptShowErrors && !isNonEmpty(deptForm.name)">
            <NvFieldLabel for="dept-name"
              >部门名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="dept-name" v-model="deptForm.name" autocomplete="off" required />
          </NvField>
          <NvField v-if="!deptParentLocked">
            <NvFieldLabel for="dept-parent">上级部门</NvFieldLabel>
            <NvSelect v-model="deptForm.parentDepartmentCode">
              <NvSelectTrigger id="dept-parent"
                ><NvSelectValue placeholder="无（顶级部门）"
              /></NvSelectTrigger>
              <NvSelectContent>
                <NvSelectItem
                  v-for="d in departments.items.value"
                  :key="d.code"
                  :value="d.code ?? NONE_PARENT"
                >
                  {{ d.displayName ?? d.code }}
                </NvSelectItem>
              </NvSelectContent>
            </NvSelect>
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="deptCreateOpen = false">取消</NvButton>
          <NvButton type="submit" :disabled="departments.createPending.value">
            <NvSpinner v-if="departments.createPending.value" aria-hidden="true" />保存部门
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
