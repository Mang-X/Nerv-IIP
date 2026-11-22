<script setup lang="ts">
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import MasterDataTreeNode from '@/components/masterData/MasterDataTreeNode.vue'
import { NvButton, NvInput, ScrollArea, NvStatusBadge } from '@nerv-iip/ui'
import { PlusIcon, SearchIcon, UsersIcon, UsersRoundIcon } from '@lucide/vue'
import type { ShallowUnwrapRef } from 'vue'
import type { useOrganizationPage } from '../useOrganizationPage'

type View = ShallowUnwrapRef<ReturnType<typeof useOrganizationPage>>

defineProps<{
  TREE_TAKE: View['TREE_TAKE']
  allExpanded: View['allExpanded']
  childLabelOf: View['childLabelOf']
  deptActions: View['deptActions']
  detailFields: View['detailFields']
  detailLoading: View['detailLoading']
  expandAllToggle: View['expandAllToggle']
  expanded: View['expanded']
  filteredForest: View['filteredForest']
  lifecycle: View['lifecycle']
  nodeKey: View['nodeKey']
  openCreateChildDept: View['openCreateChildDept']
  openCreateRootDept: View['openCreateRootDept']
  openCreateTeam: View['openCreateTeam']
  openEditDept: View['openEditDept']
  openEditTeam: View['openEditTeam']
  openMembers: View['openMembers']
  selectNode: View['selectNode']
  selectedKey: View['selectedKey']
  selectedNode: View['selectedNode']
  selectedPath: View['selectedPath']
  teamActions: View['teamActions']
  teamListError: View['teamListError']
  teamRows: View['teamRows']
  teams: View['teams']
  toggleExpand: View['toggleExpand']
  tree: View['tree']
  treeListError: View['treeListError']
  treePending: View['treePending']
  treeTruncated: View['treeTruncated']
}>()

const treeSearch = defineModel<View['treeSearch']>('treeSearch', { required: true })

const includeDisabled = defineModel<View['includeDisabled']>('includeDisabled', { required: true })
</script>

<template>
  <div class="grid gap-4 md:grid-cols-[320px_minmax(0,1fr)]">
    <!-- 左：部门树 -->
    <section
      class="flex flex-col gap-3 rounded-lg border border-border bg-card p-3"
      aria-label="部门树"
    >
      <div class="relative">
        <SearchIcon
          class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <NvInput
          v-model="treeSearch"
          class="pl-8"
          placeholder="搜索部门编码、名称"
          aria-label="搜索部门树"
        />
      </div>
      <div class="flex items-center justify-between">
        <NvButton
          v-if="tree.length"
          size="sm"
          variant="ghost"
          type="button"
          class="h-7 px-2 text-xs"
          @click="expandAllToggle"
        >
          {{ allExpanded ? '全部折叠' : '全部展开' }}
        </NvButton>
        <IncludeDisabledFilter v-model="includeDisabled" class="ml-auto" />
      </div>

      <p v-if="treeListError" class="text-sm text-destructive" role="alert">
        {{ treeListError }}
      </p>

      <ScrollArea class="h-[28rem]">
        <div v-if="treePending && !tree.length" class="px-1 py-2 text-sm text-muted-foreground">
          加载部门中…
        </div>
        <div v-else-if="!tree.length" class="grid gap-2 px-1 py-6 text-center">
          <UsersRoundIcon class="mx-auto size-8 text-muted-foreground" aria-hidden="true" />
          <p class="text-sm text-muted-foreground">还没有部门，点击创建第一条。</p>
          <NvButton size="sm" type="button" class="mx-auto" @click="openCreateRootDept">
            <PlusIcon aria-hidden="true" />
            新建部门
          </NvButton>
        </div>
        <div
          v-else-if="treeSearch.trim() && !filteredForest.length"
          class="grid gap-2 px-1 py-6 text-center"
        >
          <p class="text-sm text-muted-foreground">没有匹配「{{ treeSearch.trim() }}」的部门。</p>
          <NvButton
            size="sm"
            variant="outline"
            type="button"
            class="mx-auto"
            @click="treeSearch = ''"
            >清空搜索</NvButton
          >
        </div>
        <ul v-else class="grid gap-0.5" role="tree">
          <MasterDataTreeNode
            v-for="node in filteredForest"
            :key="nodeKey(node)"
            :node="node"
            :depth="0"
            :expanded="expanded"
            :selected-key="selectedKey"
            :child-label-of="childLabelOf"
            @select="selectNode"
            @toggle="toggleExpand"
            @create-child="openCreateChildDept"
          />
        </ul>
      </ScrollArea>

      <p v-if="treeTruncated" class="text-xs text-muted-foreground">
        部门较多，当前仅展示前 {{ TREE_TAKE }} 条。
      </p>
    </section>

    <!-- 右：部门详情 + 该部门班组 -->
    <section class="rounded-lg border border-border bg-card p-4" aria-label="部门详情">
      <div v-if="!selectedNode" class="grid place-items-center gap-2 py-16 text-center">
        <UsersRoundIcon class="size-8 text-muted-foreground" aria-hidden="true" />
        <p class="text-sm text-muted-foreground">从左侧选择一个部门查看详情与班组。</p>
      </div>
      <div v-else class="grid gap-4">
        <nav
          class="flex flex-wrap items-center gap-1 text-sm text-muted-foreground"
          aria-label="选中路径"
        >
          <template v-for="(node, idx) in selectedPath" :key="nodeKey(node)">
            <span v-if="idx > 0" aria-hidden="true">▸</span>
            <button
              type="button"
              class="rounded px-1 hover:text-foreground hover:underline"
              :class="idx === selectedPath.length - 1 ? 'font-medium text-foreground' : ''"
              @click="selectNode(node)"
            >
              {{ node.displayName }}
            </button>
          </template>
        </nav>

        <div class="flex flex-wrap items-center justify-between gap-2">
          <div class="flex items-center gap-2">
            <h2 class="text-base font-semibold text-foreground">
              {{ selectedNode.displayName }}
            </h2>
            <span class="text-xs text-muted-foreground">部门 · {{ selectedNode.code }}</span>
            <NvStatusBadge :value="selectedNode.active ? 'active' : 'disabled'" />
          </div>
          <div class="flex items-center gap-2">
            <NvButton
              size="sm"
              variant="outline"
              type="button"
              @click="openCreateChildDept(selectedNode)"
            >
              <PlusIcon aria-hidden="true" />
              新建子部门
            </NvButton>
            <NvButton size="sm" type="button" @click="openCreateTeam(selectedNode.code)">
              <PlusIcon aria-hidden="true" />
              在此部门下新建班组
            </NvButton>
            <MasterDataRowActions
              :row="selectedNode.item"
              entity-label="部门"
              :detail-fields="detailFields"
              @toggle="(row) => lifecycle.request(row, deptActions, '部门')"
              @edit="openEditDept(selectedNode)"
            />
          </div>
        </div>

        <p v-if="detailLoading" class="text-sm text-muted-foreground">加载详情中…</p>
        <dl v-else class="grid gap-3 sm:grid-cols-2">
          <div v-for="field in detailFields" :key="field.label" class="grid gap-1">
            <dt class="text-xs text-muted-foreground">{{ field.label }}</dt>
            <dd class="text-sm text-foreground">{{ field.value || '无' }}</dd>
          </div>
        </dl>

        <!-- 该部门的班组 -->
        <div class="border-t border-border/60 pt-3">
          <div class="mb-2 flex items-center justify-between gap-2">
            <h3 class="text-sm font-medium text-foreground">班组</h3>
            <NvButton size="sm" type="button" @click="openCreateTeam(selectedNode.code)">
              <PlusIcon aria-hidden="true" />
              新建班组
            </NvButton>
          </div>
          <p v-if="teamListError" class="text-sm text-destructive" role="alert">
            {{ teamListError }}
          </p>
          <div
            v-if="teams.pending.value && !teamRows.length"
            class="px-1 py-2 text-sm text-muted-foreground"
          >
            加载班组中…
          </div>
          <div v-else-if="!teamRows.length" class="grid gap-2 px-1 py-6 text-center">
            <p class="text-sm text-muted-foreground">还没有班组，点击「新建班组」创建第一条。</p>
          </div>
          <ul v-else class="divide-y rounded-md border border-border">
            <li
              v-for="row in teamRows"
              :key="row.code ?? row.displayName"
              class="flex items-center justify-between gap-3 px-3 py-2"
            >
              <div class="flex min-w-0 items-center gap-2">
                <span
                  class="truncate text-sm"
                  :class="row.active === false ? 'text-muted-foreground line-through' : ''"
                >
                  {{ row.displayName ?? row.code ?? '无' }}
                </span>
                <span class="shrink-0 text-xs text-muted-foreground">{{ row.code }}</span>
                <NvStatusBadge v-if="row.active === false" value="disabled" />
              </div>
              <div class="flex shrink-0 items-center gap-1">
                <NvButton
                  type="button"
                  variant="ghost"
                  size="sm"
                  :disabled="!row.code"
                  @click="openMembers(row)"
                >
                  <UsersIcon aria-hidden="true" />管理成员
                </NvButton>
                <MasterDataRowActions
                  :row="row"
                  entity-label="班组"
                  :detail-fields="[
                    { label: '班组编码', value: row.code ?? '' },
                    { label: '班组名称', value: row.displayName ?? '' },
                  ]"
                  @toggle="(row) => lifecycle.request(row, teamActions, '班组')"
                  @edit="openEditTeam"
                />
              </div>
            </li>
          </ul>
        </div>
      </div>
    </section>
  </div>
</template>
