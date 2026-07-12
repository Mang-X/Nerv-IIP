<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { nextTick, onMounted, ref, useSlots, watch } from 'vue'
import {
  DownloadIcon,
  MoreHorizontalIcon,
  PrinterIcon,
  RefreshCwIcon,
  Rows2Icon,
  Rows3Icon,
  SearchIcon,
  XIcon,
} from 'lucide-vue-next'
import { cn } from '../../../lib/utils'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../ui/dropdown-menu'
import ButtonPro from '../button/ButtonPro.vue'
import InputPro from '../input/InputPro.vue'
import type { DataTableProDensity } from './types'

/**
 * Pro — a standalone, polished operations bar (does NOT touch原版 components).
 * Composes a title + live count, optional quick-filter segmented tabs, a search
 * box, density + refresh + overflow-menu built-ins, and open slots for
 * field-filter / column / primary-action triggers. Usable on its own or fed to
 * DataTablePro. `surface="plain"` drops the card chrome for embedding.
 */
interface ToolbarTab {
  label: string
  value: string
  count?: number
}

const props = withDefaults(
  defineProps<{
    title?: string
    count?: number | string
    description?: string
    /** Quick-filter segmented tabs (v-model:tab). */
    tabs?: ToolbarTab[]
    searchable?: boolean
    searchPlaceholder?: string
    /** Built-in density toggle (v-model:density). */
    showDensity?: boolean
    /** Built-in refresh button → emits `refresh`; spins while `loading`. */
    refreshable?: boolean
    loading?: boolean
    /** Built-in overflow menu (导出 / 打印) → emits `export` / `print`. */
    showMore?: boolean
    surface?: 'card' | 'plain'
    class?: HTMLAttributes['class']
  }>(),
  {
    searchable: false,
    searchPlaceholder: '搜索…',
    showDensity: false,
    refreshable: false,
    loading: false,
    showMore: false,
    surface: 'card',
  },
)

defineEmits<{ refresh: []; export: []; print: [] }>()

const search = defineModel<string>('search', { default: '' })
const tab = defineModel<string>('tab', { default: '' })
const density = defineModel<DataTableProDensity>('density', { default: 'comfortable' })

const slots = useSlots()

// Sliding pill behind the quick-filter segments — measured from the active button.
const segEl = ref<HTMLElement>()
const segInd = ref({ left: 0, width: 0, ready: false })
function measureSeg() {
  const active = segEl.value?.querySelector<HTMLElement>('[data-active]')
  if (!active) {
    segInd.value = { ...segInd.value, ready: false }
    return
  }
  segInd.value = { left: active.offsetLeft, width: active.offsetWidth, ready: true }
}
watch([tab, () => props.tabs], () => nextTick(measureSeg), { deep: true })
onMounted(() => nextTick(measureSeg))
</script>

<template>
  <div
    :class="
      cn(
        'ds-tb flex flex-col gap-3 px-3 py-3 sm:px-4',
        surface === 'card' && 'rounded-xl border bg-card shadow-sm',
        props.class,
      )
    "
  >
    <!-- Controls row -->
    <div class="flex flex-wrap items-center gap-2">
      <div v-if="title || description || count != null" class="mr-auto flex min-w-0 flex-col">
        <div v-if="title || count != null" class="flex items-center gap-2">
          <h3 v-if="title" class="truncate text-sm font-semibold">{{ title }}</h3>
          <span v-if="count != null" class="ds-tb-count">{{ count }}</span>
        </div>
        <p v-if="description" class="truncate text-xs text-muted-foreground">{{ description }}</p>
      </div>
      <div v-else class="mr-auto" />

      <slot name="start" />

      <InputPro
        v-if="searchable"
        v-model="search"
        :placeholder="searchPlaceholder"
        class="h-8 w-full max-w-[14rem] sm:w-56"
        aria-label="搜索"
      >
        <template #leading><SearchIcon /></template>
        <template v-if="search" #trailing>
          <button type="button" class="ds-tb-clear" aria-label="清除搜索" @click="search = ''">
            <XIcon class="size-3.5" />
          </button>
        </template>
      </InputPro>

      <slot name="filters" />
      <slot name="columns" />

      <ButtonPro
        v-if="showDensity"
        variant="outline"
        size="icon-sm"
        :title="density === 'compact' ? '切换为舒适密度' : '切换为紧凑密度'"
        :aria-label="density === 'compact' ? '切换为舒适密度' : '切换为紧凑密度'"
        @click="density = density === 'compact' ? 'comfortable' : 'compact'"
      >
        <Rows2Icon v-if="density === 'comfortable'" aria-hidden="true" />
        <Rows3Icon v-else aria-hidden="true" />
      </ButtonPro>

      <ButtonPro
        v-if="refreshable"
        variant="outline"
        size="icon-sm"
        aria-label="刷新"
        :disabled="loading"
        @click="$emit('refresh')"
      >
        <RefreshCwIcon :class="loading ? 'animate-spin' : ''" aria-hidden="true" />
      </ButtonPro>

      <DropdownMenu v-if="showMore || slots.menu">
        <DropdownMenuTrigger as-child>
          <ButtonPro variant="outline" size="icon-sm" aria-label="更多操作">
            <MoreHorizontalIcon aria-hidden="true" />
          </ButtonPro>
        </DropdownMenuTrigger>
        <DropdownMenuContent align="end" class="w-44">
          <slot name="menu">
            <DropdownMenuItem @select="$emit('export')">
              <DownloadIcon aria-hidden="true" />
              导出 CSV
            </DropdownMenuItem>
            <DropdownMenuItem @select="$emit('print')">
              <PrinterIcon aria-hidden="true" />
              打印
            </DropdownMenuItem>
          </slot>
        </DropdownMenuContent>
      </DropdownMenu>

      <slot name="actions" />
    </div>

    <!-- Quick-filter segmented tabs -->
    <div v-if="tabs && tabs.length" class="flex flex-wrap items-center gap-2">
      <div ref="segEl" class="ds-tb-seg" role="tablist">
        <span
          v-show="segInd.ready"
          class="ds-tb-seg-ind"
          :style="{ left: `${segInd.left}px`, width: `${segInd.width}px` }"
          aria-hidden="true"
        />
        <button
          v-for="t in tabs"
          :key="t.value"
          type="button"
          role="tab"
          :aria-selected="t.value === tab"
          class="ds-tb-seg-btn"
          :data-active="t.value === tab || undefined"
          @click="tab = t.value"
        >
          {{ t.label }}
          <span v-if="t.count != null" class="ds-tb-seg-count">{{ t.count }}</span>
        </button>
      </div>
      <slot name="tabs-end" />
    </div>

    <slot name="below" />
  </div>
</template>

<style scoped>
@layer nv-components {
  /* Count chip beside the title. */
  .ds-tb-count {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 1.375rem;
    height: 1.375rem;
    padding-inline: 0.4rem;
    border-radius: 6px;
    background-color: var(--muted);
    color: var(--muted-foreground);
    font-size: 0.75rem;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }

  .ds-tb-clear {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    border-radius: 9999px;
    color: var(--muted-foreground);
    transition:
      color 0.15s ease,
      background-color 0.15s ease;
  }
  .ds-tb-clear:hover {
    color: var(--foreground);
    background-color: var(--muted);
  }

  /* Segmented quick-filter control. */
  .ds-tb-seg {
    position: relative;
    display: inline-flex;
    align-items: center;
    gap: 0.125rem;
    padding: 0.1875rem;
    border-radius: 9px;
    background-color: color-mix(in oklch, var(--muted) 70%, var(--card));
    border: 1px solid var(--border);
  }
  /* Sliding active pill (measured from the active button). */
  .ds-tb-seg-ind {
    position: absolute;
    top: 0.1875rem;
    bottom: 0.1875rem;
    left: 0;
    z-index: 0;
    border-radius: 6px;
    background-color: var(--card);
    box-shadow:
      0 1px 2px 0 color-mix(in oklch, black 12%, transparent),
      inset 0 0 0 1px color-mix(in oklch, var(--foreground) 8%, transparent);
    transition:
      left 0.26s var(--nv-ease-out-quart, ease-out),
      width 0.26s var(--nv-ease-out-quart, ease-out);
  }
  .ds-tb-seg-btn {
    position: relative;
    z-index: 1;
    display: inline-flex;
    align-items: center;
    gap: 0.375rem;
    height: 1.75rem;
    padding-inline: 0.625rem;
    border-radius: 6px;
    font-size: 0.8125rem;
    font-weight: 500;
    color: var(--muted-foreground);
    outline: none;
    transition: color 0.15s var(--nv-ease-out-quart, ease-out);
  }
  .ds-tb-seg-btn:hover:not([data-active]) {
    color: var(--foreground);
  }
  .ds-tb-seg-btn:focus-visible {
    box-shadow: 0 0 0 3px color-mix(in oklch, var(--ring) 45%, transparent);
  }
  .ds-tb-seg-btn[data-active] {
    color: var(--foreground);
  }
  @media (prefers-reduced-motion: reduce) {
    .ds-tb-seg-ind {
      transition: none;
    }
  }
  .ds-tb-seg-count {
    display: inline-flex;
    align-items: center;
    justify-content: center;
    min-width: 1.125rem;
    height: 1.125rem;
    padding-inline: 0.25rem;
    border-radius: 9999px;
    background-color: color-mix(in oklch, var(--muted-foreground) 18%, transparent);
    color: var(--muted-foreground);
    font-size: 0.6875rem;
    font-weight: 600;
    font-variant-numeric: tabular-nums;
  }
  .ds-tb-seg-btn[data-active] .ds-tb-seg-count {
    background-color: color-mix(in oklch, var(--nv-brand) 16%, transparent);
    color: var(--nv-brand-strong);
  }

  @media (prefers-reduced-motion: reduce) {
    .ds-tb-seg-btn {
      transition: none;
    }
  }
}
</style>
