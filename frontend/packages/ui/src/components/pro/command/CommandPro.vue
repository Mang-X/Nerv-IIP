<script setup lang="ts">
import type { Component, HTMLAttributes } from 'vue'
import { computed, onBeforeUnmount, onMounted, ref, watch } from 'vue'
import {
  DialogContent,
  DialogDescription,
  DialogOverlay,
  DialogPortal,
  DialogRoot,
  DialogTitle,
  VisuallyHidden,
} from 'reka-ui'
import { CornerDownLeftIcon, SearchIcon } from 'lucide-vue-next'
import { cn } from '../../../lib/utils'

/**
 * Pro — ⌘K command palette. A dialog-hosted, filterable command list with full
 * keyboard navigation (↑ ↓ to move, ↵ to run, esc to close). Built on reka
 * Dialog primitives; never edits原版 components.
 */
export interface CommandItem {
  id: string
  label: string
  hint?: string
  icon?: Component
  keywords?: string
}
export interface CommandGroup {
  label: string
  items: CommandItem[]
}

const props = withDefaults(
  defineProps<{
    open?: boolean
    groups: CommandGroup[]
    placeholder?: string
    hotkey?: boolean
    class?: HTMLAttributes['class']
  }>(),
  { open: false, placeholder: '搜索命令、工单、产线…', hotkey: true },
)

const emit = defineEmits<{
  (e: 'update:open', value: boolean): void
  (e: 'select', item: CommandItem): void
}>()

const query = ref('')
const activeId = ref<string>()

const filteredGroups = computed<CommandGroup[]>(() => {
  const q = query.value.trim().toLowerCase()
  if (!q) return props.groups
  return props.groups
    .map((g) => ({
      ...g,
      items: g.items.filter((it) => `${it.label} ${it.keywords ?? ''}`.toLowerCase().includes(q)),
    }))
    .filter((g) => g.items.length > 0)
})
const flatItems = computed(() => filteredGroups.value.flatMap((g) => g.items))

watch(
  flatItems,
  (items) => {
    if (!items.some((i) => i.id === activeId.value)) activeId.value = items[0]?.id
  },
  { immediate: true },
)

watch(
  () => props.open,
  (isOpen) => {
    if (isOpen) {
      query.value = ''
      activeId.value = props.groups[0]?.items[0]?.id
    }
  },
)

function setOpen(value: boolean) {
  emit('update:open', value)
}
function select(item: CommandItem) {
  emit('select', item)
  setOpen(false)
}

function onListKeydown(e: KeyboardEvent) {
  const items = flatItems.value
  if (!items.length) return
  const idx = items.findIndex((i) => i.id === activeId.value)
  if (e.key === 'ArrowDown') {
    e.preventDefault()
    activeId.value = items[(idx + 1) % items.length].id
  } else if (e.key === 'ArrowUp') {
    e.preventDefault()
    activeId.value = items[(idx - 1 + items.length) % items.length].id
  } else if (e.key === 'Enter') {
    e.preventDefault()
    const it = items.find((i) => i.id === activeId.value)
    if (it) select(it)
  }
}

function onGlobalKey(e: KeyboardEvent) {
  if (props.hotkey && (e.metaKey || e.ctrlKey) && e.key.toLowerCase() === 'k') {
    e.preventDefault()
    setOpen(!props.open)
  }
}
onMounted(() => window.addEventListener('keydown', onGlobalKey))
onBeforeUnmount(() => window.removeEventListener('keydown', onGlobalKey))
</script>

<template>
  <DialogRoot :open="open" @update:open="setOpen">
    <DialogPortal>
      <DialogOverlay
        class="data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 fixed inset-0 z-50 bg-black/40 backdrop-blur-sm"
      />
      <DialogContent
        :class="
          cn(
            'data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 fixed top-[14vh] left-1/2 z-50 w-[calc(100%-2rem)] max-w-lg -translate-x-1/2 overflow-hidden rounded-xl border border-border bg-popover text-popover-foreground shadow-lg duration-150 outline-none',
            props.class,
          )
        "
        @keydown="onListKeydown"
      >
        <VisuallyHidden>
          <DialogTitle>命令面板</DialogTitle>
          <DialogDescription>搜索并执行命令</DialogDescription>
        </VisuallyHidden>

        <div class="flex items-center gap-2.5 border-b border-border px-3.5">
          <SearchIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
          <input
            v-model="query"
            :placeholder="placeholder"
            autofocus
            class="h-12 w-full bg-transparent text-sm outline-none placeholder:text-muted-foreground"
          />
        </div>

        <div class="max-h-[min(60vh,22rem)] overflow-y-auto p-1.5">
          <template v-for="group in filteredGroups" :key="group.label">
            <div class="px-2 pt-2 pb-1 text-xs font-medium text-muted-foreground">
              {{ group.label }}
            </div>
            <button
              v-for="item in group.items"
              :key="item.id"
              type="button"
              :data-active="activeId === item.id || undefined"
              class="ds-cmd-item flex w-full items-center gap-2.5 rounded-md px-2.5 py-2 text-left text-sm outline-none"
              @click="select(item)"
              @mousemove="activeId = item.id"
            >
              <component
                :is="item.icon"
                v-if="item.icon"
                class="size-4 shrink-0 text-muted-foreground"
                aria-hidden="true"
              />
              <span class="flex-1 truncate">{{ item.label }}</span>
              <span v-if="item.hint" class="font-mono text-xs text-muted-foreground">{{
                item.hint
              }}</span>
            </button>
          </template>
          <div
            v-if="!flatItems.length"
            class="px-3 py-10 text-center text-sm text-muted-foreground"
          >
            无匹配结果
          </div>
        </div>

        <div
          class="flex items-center gap-3 border-t border-border px-3.5 py-2 text-xs text-muted-foreground"
        >
          <span class="flex items-center gap-1"
            ><kbd class="ds-cmd-kbd">↑</kbd><kbd class="ds-cmd-kbd">↓</kbd>选择</span
          >
          <span class="flex items-center gap-1"
            ><kbd class="ds-cmd-kbd"><CornerDownLeftIcon class="size-3" /></kbd>执行</span
          >
          <span class="flex items-center gap-1"><kbd class="ds-cmd-kbd">esc</kbd>关闭</span>
        </div>
      </DialogContent>
    </DialogPortal>
  </DialogRoot>
</template>

<style scoped>
.ds-cmd-item {
  transition: background-color 0.12s var(--ease-out-quart, ease-out);
}
.ds-cmd-item[data-active] {
  background: var(--accent);
}
.ds-cmd-kbd {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 1.25rem;
  height: 1.25rem;
  padding: 0 0.25rem;
  border: 1px solid var(--border);
  border-radius: 0.3rem;
  background: var(--muted);
  font-family: var(--font-sans);
  font-size: 0.7rem;
}
</style>
