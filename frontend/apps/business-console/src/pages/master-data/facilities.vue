<script setup lang="ts">
import { usePermission } from '@/composables/usePermission'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import MasterDataLifecycleDialog from '@/components/masterData/MasterDataLifecycleDialog.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import { NvButton, NvMetricStrip, NvPageHeader } from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from '@lucide/vue'
import FacilityDirectory from './components/FacilityDirectory.vue'
import FacilityEditDialog from './components/FacilityEditDialog.vue'
import { useFacilitiesPage } from './useFacilitiesPage'

const { can } = usePermission()

definePage({
  meta: {
    requiresAuth: true,
    title: '工厂结构',
    requiredPermissions: ['business.masterdata.resources.read'],
  },
})

const {
  CREATE_DIALOGS,
  NODE_LABEL,
  NONE_OPTION,
  TREE_TAKE,
  allExpanded,
  canEdit,
  childCount,
  childLabelOf,
  childTypesOfSelected,
  createOpen,
  creating,
  detailFields,
  detailLoading,
  editCode,
  editForm,
  editCalendars,
  editLoading,
  editOpen,
  editPending,
  editShowErrors,
  editTimeZoneOptions,
  editTitle,
  editType,
  expandAllToggle,
  expanded,
  filteredForest,
  forest,
  includeDisabled,
  isNonEmpty,
  lifecycle,
  lines,
  nodeKey,
  openCreateChild,
  openCreateRoot,
  openRelatedStation,
  stationLine,
  revealedNodeKey,
  creationNotice,
  revealCreatedNode,
  openEdit,
  refreshAll,
  requestLifecycle,
  selectNode,
  selectedActions,
  selectedKey,
  selectedNode,
  selectedPath,
  sites,
  stations,
  submitEdit,
  toggleExpand,
  totalNodes,
  tree,
  treeListError,
  treePending,
  treeSearch,
  treeTruncated,
  workCenters,
  workshops,
} = useFacilitiesPage()
</script>

<template>
  <BusinessLayout>
    <NvPageHeader
      title="工厂结构"
      :breadcrumbs="[{ label: '基础数据' }]"
      :count="`${totalNodes} 个节点`"
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
        <NvButton
          v-if="can(P.masterDataResourcesManage)"
          size="sm"
          type="button"
          @click="openCreateRoot"
        >
          <PlusIcon aria-hidden="true" />
          新建工厂
        </NvButton>
      </template>
    </NvPageHeader>

    <!-- 工厂▸车间▸产线▸工作中心是逐级包含的层级计数，不是同一总量的构成，
         所以用一条并列的 Strip，不用环形卡。 -->
    <NvMetricStrip
      :cells="[
        { key: 'site', label: '工厂', value: sites.total.value, unit: '个' },
        { key: 'workshop', label: '车间', value: workshops.workshopsTotal.value, unit: '个' },
        { key: 'line', label: '产线', value: lines.total.value, unit: '条' },
        {
          key: 'workCenter',
          label: '工作中心',
          value: workCenters.total.value,
          unit: '个',
          meta: '排产与报工的产能单元',
        },
        { key: 'station', label: '工位', value: stations.total.value, unit: '个' },
      ]"
    />

    <FacilityDirectory
      v-model:tree-search="treeSearch"
      v-model:include-disabled="includeDisabled"
      :-n-o-d-e_-l-a-b-e-l="NODE_LABEL"
      :-t-r-e-e_-t-a-k-e="TREE_TAKE"
      :all-expanded="allExpanded"
      :child-count="childCount"
      :child-label-of="childLabelOf"
      :child-types-of-selected="childTypesOfSelected"
      :detail-fields="detailFields"
      :detail-loading="detailLoading"
      :expand-all-toggle="expandAllToggle"
      :expanded="expanded"
      :filtered-forest="filteredForest"
      :forest="forest"
      :node-key="nodeKey"
      :open-create-child="openCreateChild"
      :open-create-root="openCreateRoot"
      :open-related-station="openRelatedStation"
      :station-line="stationLine"
      :revealed-node-key="revealedNodeKey"
      :creation-notice="creationNotice"
      :open-edit="openEdit"
      :request-lifecycle="requestLifecycle"
      :select-node="selectNode"
      :selected-actions="selectedActions"
      :selected-key="selectedKey"
      :selected-node="selectedNode"
      :selected-path="selectedPath"
      :toggle-expand="toggleExpand"
      :tree="tree"
      :tree-list-error="treeListError"
      :tree-pending="treePending"
      :tree-truncated="treeTruncated"
    />

    <!-- 编辑对话框（编码只读；改名 + 改挂上级） -->
    <FacilityEditDialog
      v-model:edit-open="editOpen"
      :-n-o-d-e_-l-a-b-e-l="NODE_LABEL"
      :-n-o-n-e_-o-p-t-i-o-n="NONE_OPTION"
      :can-edit="canEdit"
      :edit-code="editCode"
      :edit-form="editForm"
      :edit-calendars="editCalendars"
      :edit-loading="editLoading"
      :edit-pending="editPending"
      :edit-show-errors="editShowErrors"
      :edit-time-zone-options="editTimeZoneOptions"
      :edit-title="editTitle"
      :edit-type="editType"
      :is-non-empty="isNonEmpty"
      :sites="sites"
      :submit-edit="submitEdit"
    />
    <component
      :is="CREATE_DIALOGS[creating.type]"
      v-if="creating"
      :key="creating.session"
      v-model:open="createOpen"
      v-bind="creating.bindings"
      @created="revealCreatedNode(creating.type, $event)"
    />
    <MasterDataLifecycleDialog :controller="lifecycle" />
  </BusinessLayout>
</template>
