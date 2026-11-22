<script setup lang="ts">
import CarriedContextSummary from '@/components/business/CarriedContextSummary.vue'
import DirectoryPicker from '@/components/business/DirectoryPicker.vue'
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
  departmentLabel: View['departmentLabel']
  departments: View['departments']
  isNonEmpty: View['isNonEmpty']
  submitTeam: View['submitTeam']
  teamActions: View['teamActions']
  teamDepartmentLocked: View['teamDepartmentLocked']
  teamEditLoading: View['teamEditLoading']
  teamEditingCode: View['teamEditingCode']
  teamForm: View['teamForm']
  teamFormValid: View['teamFormValid']
  teamShowErrors: View['teamShowErrors']
  teams: View['teams']
}>()

const teamOpen = defineModel<View['teamOpen']>('teamOpen', { required: true })
</script>

<template>
  <NvDialog v-model:open="teamOpen">
    <NvDialogContent class="sm:max-w-lg">
      <NvDialogHeader>
        <NvDialogTitle>{{
          teamEditingCode ? `编辑班组 · ${teamEditingCode}` : '新建班组'
        }}</NvDialogTitle>
        <NvDialogDescription class="sr-only">
          {{
            teamEditingCode
              ? `班组 ${teamEditingCode}`
              : `所属部门：${departmentLabel(teamForm.departmentCode)}`
          }}
        </NvDialogDescription>
      </NvDialogHeader>
      <form class="grid gap-4" @submit.prevent="submitTeam">
        <CarriedContextSummary
          v-if="teamEditingCode || teamDepartmentLocked"
          label="班组归属"
          :items="[
            { label: '班组编码', value: teamEditingCode ? teamForm.code : '' },
            {
              label: '所属部门',
              value:
                teamDepartmentLocked && !teamEditingCode
                  ? departmentLabel(teamForm.departmentCode)
                  : '',
            },
          ]"
        />
        <p v-if="teamShowErrors && !teamFormValid" class="text-sm text-destructive" role="alert">
          请完整填写带 * 的必填项（已标红）。
        </p>
        <NvFieldGroup class="grid gap-3 sm:grid-cols-2">
          <NvField :data-invalid="teamShowErrors && !isNonEmpty(teamForm.name)">
            <NvFieldLabel for="team-name"
              >班组名称 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvInput id="team-name" v-model="teamForm.name" autocomplete="off" required />
          </NvField>
          <NvField
            v-if="!teamDepartmentLocked || teamEditingCode"
            :data-invalid="teamShowErrors && !isNonEmpty(teamForm.departmentCode)"
          >
            <NvFieldLabel for="team-dept"
              >所属部门 <span class="text-destructive">*</span></NvFieldLabel
            >
            <NvSelect v-model="teamForm.departmentCode">
              <NvSelectTrigger id="team-dept"
                ><NvSelectValue placeholder="请选择部门"
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
          <NvField :data-invalid="teamShowErrors && !isNonEmpty(teamForm.shiftCode)">
            <NvFieldLabel for="team-shift"
              >所属班次 <span class="text-destructive">*</span></NvFieldLabel
            >
            <!-- 要挂的班次还没建时可以就地新增，建好自动选中。 -->
            <DirectoryPicker
              id="team-shift"
              v-model="teamForm.shiftCode"
              directory-type="shift"
              placeholder="请选择班次"
              creatable
            />
          </NvField>
          <NvField>
            <NvFieldLabel for="team-workshop">所属车间</NvFieldLabel>
            <DirectoryPicker
              id="team-workshop"
              v-model="teamForm.workshopCode"
              directory-type="workshop"
              placeholder="请选择车间"
              creatable
            />
            <NvFieldDescription
              >绑定后，该车间各工作中心的派工都能从本班组选人。</NvFieldDescription
            >
          </NvField>
        </NvFieldGroup>
        <NvDialogFooter>
          <NvButton type="button" variant="outline" @click="teamOpen = false">取消</NvButton>
          <NvButton
            type="submit"
            :disabled="
              teams.createPending.value || teamActions.updatePending.value || teamEditLoading
            "
          >
            <NvSpinner
              v-if="teams.createPending.value || teamActions.updatePending.value"
              aria-hidden="true"
            />{{ teamEditingCode ? '保存修改' : '保存班组' }}
          </NvButton>
        </NvDialogFooter>
      </form>
    </NvDialogContent>
  </NvDialog>
</template>
