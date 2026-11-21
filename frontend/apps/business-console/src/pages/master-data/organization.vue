<script setup lang="ts">
import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import TeamMembersDialog from '@/components/masterData/TeamMembersDialog.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvMetricStrip, NvPageHeader } from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import OrganizationDirectory from './components/OrganizationDirectory.vue'
import DepartmentCreateDialog from './components/DepartmentCreateDialog.vue'
import DepartmentEditDialog from './components/DepartmentEditDialog.vue'
import TeamFormDialog from './components/TeamFormDialog.vue'
import { useOrganizationPage } from './useOrganizationPage'

definePage({
  meta: {
    requiresAuth: true,
    title: '组织与班组',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const {
  NONE_PARENT,
  TREE_TAKE,
  allExpanded,
  canCreateDept,
  canEditDept,
  childLabelOf,
  departmentLabel,
  departments,
  deptActions,
  deptCreateOpen,
  deptEditCode,
  deptEditForm,
  deptEditLoading,
  deptEditOpen,
  deptEditParent,
  deptEditShowErrors,
  deptForm,
  deptParentLocked,
  deptParentName,
  deptParentOptions,
  deptShowErrors,
  detailFields,
  detailLoading,
  expandAllToggle,
  expanded,
  filteredForest,
  includeDisabled,
  isNonEmpty,
  lifecycle,
  membersOpen,
  membersTeam,
  nodeKey,
  openCreateChildDept,
  openCreateRootDept,
  openCreateTeam,
  openEditDept,
  openEditTeam,
  openMembers,
  refreshAll,
  selectNode,
  selectedKey,
  selectedNode,
  selectedPath,
  submitCreateDept,
  submitEditDept,
  submitTeam,
  teamActions,
  teamDepartmentLocked,
  teamEditLoading,
  teamEditingCode,
  teamForm,
  teamFormValid,
  teamListError,
  teamOpen,
  teamRows,
  teamShowErrors,
  teams,
  toggleExpand,
  totalDepartments,
  tree,
  treeListError,
  treePending,
  treeSearch,
  treeTruncated,
} = useOrganizationPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="组织与班组"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${totalDepartments} 个部门`"
    >
      <template #actions>
        <NvButton
          size="sm"
          variant="outline"
          type="button"
          :disabled="treePending"
          @click="refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </NvButton>
        <NvButton size="sm" type="button" @click="openCreateRootDept">
          <PlusIcon aria-hidden="true" />
          新建部门
        </NvButton>
      </template>
    </NvPageHeader>

    <!-- 部门与班组是层级归属关系，不是同一总量的构成，用并列 Strip 而非环形卡。 -->
    <NvMetricStrip
      :cells="[
        { key: 'department', label: '部门', value: departments.total.value, unit: '个' },
        {
          key: 'team',
          label: '班组',
          value: teams.total.value,
          unit: '个',
          meta: '挂靠部门与班次',
        },
      ]"
    />

    <OrganizationDirectory
      v-model:tree-search="treeSearch"
      v-model:include-disabled="includeDisabled"
      :-t-r-e-e_-t-a-k-e="TREE_TAKE"
      :all-expanded="allExpanded"
      :child-label-of="childLabelOf"
      :dept-actions="deptActions"
      :detail-fields="detailFields"
      :detail-loading="detailLoading"
      :expand-all-toggle="expandAllToggle"
      :expanded="expanded"
      :filtered-forest="filteredForest"
      :lifecycle="lifecycle"
      :node-key="nodeKey"
      :open-create-child-dept="openCreateChildDept"
      :open-create-root-dept="openCreateRootDept"
      :open-create-team="openCreateTeam"
      :open-edit-dept="openEditDept"
      :open-edit-team="openEditTeam"
      :open-members="openMembers"
      :select-node="selectNode"
      :selected-key="selectedKey"
      :selected-node="selectedNode"
      :selected-path="selectedPath"
      :team-actions="teamActions"
      :team-list-error="teamListError"
      :team-rows="teamRows"
      :teams="teams"
      :toggle-expand="toggleExpand"
      :tree="tree"
      :tree-list-error="treeListError"
      :tree-pending="treePending"
      :tree-truncated="treeTruncated"
    />

    <!-- 新建部门对话框（含就地建子部门，父归属只读） -->
    <DepartmentCreateDialog
      v-model:dept-create-open="deptCreateOpen"
      :-n-o-n-e_-p-a-r-e-n-t="NONE_PARENT"
      :can-create-dept="canCreateDept"
      :departments="departments"
      :dept-form="deptForm"
      :dept-parent-locked="deptParentLocked"
      :dept-parent-name="deptParentName"
      :dept-show-errors="deptShowErrors"
      :is-non-empty="isNonEmpty"
      :submit-create-dept="submitCreateDept"
    />

    <!-- 编辑部门对话框（编码只读；改名 + 改挂上级） -->
    <DepartmentEditDialog
      v-model:dept-edit-open="deptEditOpen"
      v-model:dept-edit-parent="deptEditParent"
      :-n-o-n-e_-p-a-r-e-n-t="NONE_PARENT"
      :can-edit-dept="canEditDept"
      :dept-actions="deptActions"
      :dept-edit-code="deptEditCode"
      :dept-edit-form="deptEditForm"
      :dept-edit-loading="deptEditLoading"
      :dept-edit-show-errors="deptEditShowErrors"
      :dept-parent-options="deptParentOptions"
      :is-non-empty="isNonEmpty"
      :submit-edit-dept="submitEditDept"
    />

    <!-- 班组对话框（新建：挂部门 + 班次；编辑：改名 + 改挂部门 / 班次，编码只读） -->
    <TeamFormDialog
      v-model:team-open="teamOpen"
      :-n-o-n-e_-p-a-r-e-n-t="NONE_PARENT"
      :department-label="departmentLabel"
      :departments="departments"
      :is-non-empty="isNonEmpty"
      :submit-team="submitTeam"
      :team-actions="teamActions"
      :team-department-locked="teamDepartmentLocked"
      :team-edit-loading="teamEditLoading"
      :team-editing-code="teamEditingCode"
      :team-form="teamForm"
      :team-form-valid="teamFormValid"
      :team-show-errors="teamShowErrors"
      :teams="teams"
    />

    <TeamMembersDialog
      v-model:open="membersOpen"
      :team-code="membersTeam.code"
      :team-name="membersTeam.name"
    />
    <MasterDataLifecycleDialog :controller="lifecycle" />
  </BusinessLayout>
</template>
