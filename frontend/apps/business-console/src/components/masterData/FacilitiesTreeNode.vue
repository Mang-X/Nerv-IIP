<script setup lang="ts">
import type { BusinessConsoleResourceItem } from '@nerv-iip/api-client'
import { Button, StatusBadge } from '@nerv-iip/ui'
import { ChevronDownIcon, ChevronRightIcon, PlusIcon } from 'lucide-vue-next'
import { computed } from 'vue'

// 工厂结构树的递归节点。状态（展开/选中）由父页通过 Set/ref 注入，
// 节点只读其状态并通过回调上抛交互，保持可测、无内部数据层。
export interface FacilitiesTreeNodeData {
  type: 'site' | 'workshop' | 'production-line' | 'work-center'
  code: string
  displayName: string
  active: boolean
  item: BusinessConsoleResourceItem
  children: FacilitiesTreeNodeData[]
}

const props = defineProps<{
  node: FacilitiesTreeNodeData
  depth: number
  /** 已展开节点 key 集合（`type:code`）。 */
  expanded: Set<string>
  /** 当前选中节点 key。 */
  selectedKey: string | null
  /** 该节点可就地新建的子级中文名（无则不显示「+ 子级」）。 */
  childLabelOf: (type: FacilitiesTreeNodeData['type']) => string | undefined
}>()

const emit = defineEmits<{
  select: [node: FacilitiesTreeNodeData]
  toggle: [node: FacilitiesTreeNodeData]
  createChild: [node: FacilitiesTreeNodeData]
}>()

const nodeKey = computed(() => `${props.node.type}:${props.node.code}`)
const hasChildren = computed(() => props.node.children.length > 0)
const isExpanded = computed(() => props.expanded.has(nodeKey.value))
const isSelected = computed(() => props.selectedKey === nodeKey.value)
const childLabel = computed(() => props.childLabelOf(props.node.type))
</script>

<template>
  <li role="treeitem" :aria-expanded="hasChildren ? isExpanded : undefined" :aria-selected="isSelected">
    <div
      class="group flex items-center gap-1 rounded-md pr-1 text-sm"
      :class="isSelected ? 'bg-accent text-accent-foreground' : 'hover:bg-accent/50'"
      :style="{ paddingLeft: `${depth * 1}rem` }"
    >
      <button
        type="button"
        class="grid size-5 shrink-0 place-items-center rounded text-muted-foreground hover:text-foreground"
        :class="hasChildren ? '' : 'invisible'"
        :aria-label="isExpanded ? '折叠' : '展开'"
        @click="emit('toggle', node)"
      >
        <ChevronDownIcon v-if="isExpanded" class="size-4" aria-hidden="true" />
        <ChevronRightIcon v-else class="size-4" aria-hidden="true" />
      </button>
      <button
        type="button"
        class="flex min-w-0 flex-1 items-center gap-2 py-1.5 text-left"
        @click="emit('select', node)"
      >
        <span class="truncate" :class="node.active ? '' : 'text-muted-foreground line-through'">{{ node.displayName }}</span>
        <span class="shrink-0 text-xs text-muted-foreground">{{ node.code }}</span>
        <StatusBadge v-if="!node.active" value="disabled" />
      </button>
      <Button
        v-if="childLabel"
        size="icon"
        variant="ghost"
        type="button"
        class="size-6 shrink-0 opacity-0 group-hover:opacity-100 focus-visible:opacity-100"
        :aria-label="`新建${childLabel}`"
        @click="emit('createChild', node)"
      >
        <PlusIcon class="size-3.5" aria-hidden="true" />
      </Button>
    </div>
    <ul v-if="hasChildren && isExpanded" class="grid gap-0.5" role="group">
      <FacilitiesTreeNode
        v-for="child in node.children"
        :key="`${child.type}:${child.code}`"
        :node="child"
        :depth="depth + 1"
        :expanded="expanded"
        :selected-key="selectedKey"
        :child-label-of="childLabelOf"
        @select="emit('select', $event)"
        @toggle="emit('toggle', $event)"
        @create-child="emit('createChild', $event)"
      />
    </ul>
  </li>
</template>
