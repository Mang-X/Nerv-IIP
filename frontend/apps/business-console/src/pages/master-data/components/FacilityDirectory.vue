<script setup lang="ts">
import MasterDataTreeNode from '@/components/masterData/MasterDataTreeNode.vue'
import IncludeDisabledFilter from '@/components/masterData/IncludeDisabledFilter.vue'
import MasterDataRowActions from '@/components/masterData/MasterDataRowActions.vue'
import { NvButton, NvInput, ScrollArea, NvStatusBadge } from '@nerv-iip/ui'
import { FactoryIcon, PlusIcon, SearchIcon } from '@lucide/vue'
import { RouterLink } from 'vue-router'
import { useTemplateRef, watch, shallowRef, type ShallowUnwrapRef } from 'vue'
import type { useFacilitiesPage } from '../useFacilitiesPage'

type View = ShallowUnwrapRef<ReturnType<typeof useFacilitiesPage>>

const props = defineProps<{
  NODE_LABEL: View['NODE_LABEL']
  TREE_TAKE: View['TREE_TAKE']
  allExpanded: View['allExpanded']
  childCount: View['childCount']
  childLabelOf: View['childLabelOf']
  childTypesOfSelected: View['childTypesOfSelected']
  detailFields: View['detailFields']
  detailLoading: View['detailLoading']
  expandAllToggle: View['expandAllToggle']
  expanded: View['expanded']
  filteredForest: View['filteredForest']
  forest: View['forest']
  nodeKey: View['nodeKey']
  openCreateChild: View['openCreateChild']
  openCreateRoot: View['openCreateRoot']
  openRelatedStation: View['openRelatedStation']
  stationLine: View['stationLine']
  revealedNodeKey: View['revealedNodeKey']
  creationNotice: View['creationNotice']
  openEdit: View['openEdit']
  requestLifecycle: View['requestLifecycle']
  selectNode: View['selectNode']
  selectedActions: View['selectedActions']
  selectedKey: View['selectedKey']
  selectedNode: View['selectedNode']
  selectedPath: View['selectedPath']
  toggleExpand: View['toggleExpand']
  tree: View['tree']
  treeListError: View['treeListError']
  treePending: View['treePending']
  treeTruncated: View['treeTruncated']
}>()

const treeSearch = defineModel<View['treeSearch']>('treeSearch', { required: true })

const includeDisabled = defineModel<View['includeDisabled']>('includeDisabled', { required: true })
const nodeTree = useTemplateRef<HTMLElement>('nodeTree')
const highlightedKey = shallowRef<string>()
watch(
  () => props.revealedNodeKey,
  (key, _, onCleanup) => {
    highlightedKey.value = key
    if (!key) return
    const node = Array.from(
      nodeTree.value?.querySelectorAll<HTMLElement>('[data-node-key]') ?? [],
    ).find((element) => element.dataset.nodeKey === key)
    node?.scrollIntoView?.({ block: 'nearest' })
    const timer = setTimeout(() => {
      highlightedKey.value = undefined
    }, 2500)
    onCleanup(() => clearTimeout(timer))
  },
  { flush: 'post' },
)
</script>

<template>
  <div class="grid gap-4 md:grid-cols-[320px_minmax(0,1fr)]">
    <!-- 左：层级树 -->
    <section
      class="flex flex-col gap-3 rounded-lg border border-border bg-card p-3"
      aria-label="工厂结构树"
    >
      <div class="relative">
        <SearchIcon
          class="pointer-events-none absolute left-2.5 top-1/2 size-4 -translate-y-1/2 text-muted-foreground"
          aria-hidden="true"
        />
        <NvInput
          v-model="treeSearch"
          class="pl-8"
          placeholder="搜索工厂、车间、产线、工作中心、工位"
          aria-label="搜索树"
        />
      </div>
      <div class="flex items-center justify-between">
        <NvButton
          v-if="forest.length"
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
        <div v-if="treePending && !forest.length" class="px-1 py-2 text-sm text-muted-foreground">
          加载层级中…
        </div>
        <!-- 整树无数据 -->
        <div v-else-if="!tree.length" class="grid gap-2 px-1 py-6 text-center">
          <FactoryIcon class="mx-auto size-8 text-muted-foreground" aria-hidden="true" />
          <p class="text-sm text-muted-foreground">还没有工厂，点击创建第一条。</p>
          <NvButton size="sm" type="button" class="mx-auto" @click="openCreateRoot">
            <PlusIcon aria-hidden="true" />
            新建工厂
          </NvButton>
        </div>
        <!-- 搜索无命中 -->
        <div
          v-else-if="treeSearch.trim() && !filteredForest.length"
          class="grid gap-2 px-1 py-6 text-center"
        >
          <p class="text-sm text-muted-foreground">没有匹配「{{ treeSearch.trim() }}」的节点。</p>
          <NvButton
            size="sm"
            variant="outline"
            type="button"
            class="mx-auto"
            @click="treeSearch = ''"
            >清空搜索</NvButton
          >
        </div>
        <ul v-else class="grid gap-0.5" ref="nodeTree" role="tree">
          <MasterDataTreeNode
            v-for="node in filteredForest"
            :key="nodeKey(node)"
            :node="node"
            :depth="0"
            :expanded="expanded"
            :highlighted-key="highlightedKey"
            :selected-key="selectedKey"
            :child-label-of="childLabelOf"
            @select="selectNode"
            @toggle="toggleExpand"
            @create-child="openCreateChild"
          />
        </ul>
      </ScrollArea>

      <p v-if="treeTruncated" class="text-xs text-muted-foreground">
        节点较多，当前仅展示前 {{ TREE_TAKE }} 条；请使用搜索缩小范围。
      </p>
    </section>

    <!-- 右：详情 -->
    <section class="rounded-lg border border-border bg-card p-4" aria-label="节点详情">
      <p v-if="creationNotice" class="text-sm text-muted-foreground" role="status">
        {{ creationNotice }}
      </p>
      <div v-if="!selectedNode" class="grid place-items-center gap-2 py-16 text-center">
        <FactoryIcon class="size-8 text-muted-foreground" aria-hidden="true" />
        <p class="text-sm text-muted-foreground">从左侧选择一个节点查看详情。</p>
      </div>
      <div v-else class="grid gap-4">
        <!-- 面包屑：工厂 ▸ 车间 ▸ 产线 ▸ 工作中心 -->
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
            <span class="text-xs text-muted-foreground"
              >{{ NODE_LABEL[selectedNode.type] }} · {{ selectedNode.code }}</span
            >
            <NvStatusBadge :value="selectedNode.active ? 'active' : 'disabled'" />
          </div>
          <div class="flex items-center gap-2">
            <NvButton
              v-for="childType in childTypesOfSelected"
              :key="childType"
              size="sm"
              type="button"
              @click="openCreateChild(selectedNode, childType)"
            >
              <PlusIcon aria-hidden="true" />
              新建{{ NODE_LABEL[childType] }}
            </NvButton>
            <MasterDataRowActions
              v-if="selectedActions"
              :row="selectedNode.item"
              :entity-label="NODE_LABEL[selectedNode.type]"
              :detail-fields="detailFields"
              @toggle="requestLifecycle"
              @edit="openEdit(selectedNode)"
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

        <!-- 子级计数 / 空子级出路 -->
        <div
          v-for="childType in childTypesOfSelected"
          :key="childType"
          class="border-t border-border/60 pt-3"
        >
          <p v-if="childCount(childType)" class="text-sm text-muted-foreground">
            该{{ NODE_LABEL[selectedNode.type] }}下有 {{ childCount(childType) }} 个{{
              NODE_LABEL[childType]
            }}。
          </p>
          <div v-else class="flex flex-wrap items-center gap-2">
            <p class="text-sm text-muted-foreground">
              该{{ NODE_LABEL[selectedNode.type] }}下还没有{{ NODE_LABEL[childType] }}。
            </p>
            <NvButton
              size="sm"
              variant="outline"
              type="button"
              @click="openCreateChild(selectedNode, childType)"
            >
              <PlusIcon aria-hidden="true" />
              新建{{ NODE_LABEL[childType] }}
            </NvButton>
          </div>
        </div>

        <!-- 工作中心 → 设备下钻出口 -->
        <div
          v-if="selectedNode.type === 'work-center'"
          class="flex flex-wrap items-center gap-2 border-t border-border/60 pt-3"
        >
          <NvButton v-if="stationLine" type="button" size="sm" @click="openRelatedStation">
            <PlusIcon aria-hidden="true" />
            新建关联工位
          </NvButton>
          <p v-else class="text-sm text-muted-foreground">
            工作中心或所属产线不可用，暂不能新建关联工位。
          </p>
          <NvButton as-child size="sm" variant="outline">
            <RouterLink
              :to="{ path: '/master-data/devices', query: { workCenterCode: selectedNode.code } }"
            >
              查看该工作中心下设备 →
            </RouterLink>
          </NvButton>
        </div>
      </div>
    </section>
  </div>
</template>
