<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { computed, ref } from 'vue'
import {
  DialogDescription,
  DialogRoot,
  DialogTitle,
  DialogTrigger,
  PopoverContent,
  PopoverPortal,
  PopoverRoot,
  PopoverTrigger,
} from 'reka-ui'
import { ChevronsUpDownIcon, XIcon } from '@lucide/vue'
import { cn } from '../../../lib/utils'
import NvDialogContent from '../../pc/dialog/NvDialogContent.vue'
import EntityPickerPanel from './EntityPickerPanel.vue'
import type { EntityPickerOption, EntityPickerVariant } from './types'

/**
 * Blocks — 实体选择器：从主数据目录（物料 / SKU / 设备 / 工厂 / 质量特性…）里挑一个实体，
 * **只能选、不能自由录入**。选项行给出「名称 + 业务编码 + 辅助信息」三段，
 * 底部 `sourceText` 注明数据来源，空态不留悬念。
 *
 * ## 两种形态，什么时候用哪种
 *
 * - **`variant="dropdown"`（默认）** —— 点一下直接在原地展开下拉，下拉内自带搜索框。
 *   **绝大多数场景都用它**：筛选条、表单字段、抽屉/弹窗内的字段。
 *   浮层走 `PopoverPortal`，能逃出 Sheet/Dialog 的 `overflow` 裁剪。
 * - **`variant="dialog"`** —— 先开一个居中对话框再选。只在确实需要更大展示空间时用：
 *   一行放不下的多列信息、需要分页的上百条目录、选之前得先读一段说明。
 *   给一个「重」场景付一次额外点击是值得的，给一个筛选条付就不值得。
 *
 * 需要选的是**枚举/字典值**（技师、停机原因、维护结果，没有业务编码）而不是主数据实体时，
 * 用更轻的 `NvSearchSelect`，别用这个 —— 那边不会硬塞一列编码和数据来源注脚。
 */
const props = withDefaults(
  defineProps<{
    modelValue?: string
    options: EntityPickerOption[]
    /** 标题，如「选择物料」。`dialog` 形态显示在对话框顶部；`dropdown` 形态用于派生可访问名称。 */
    title: string
    /** 呈现形态，默认 `dropdown`（点一下直接下拉）。 */
    variant?: EntityPickerVariant
    placeholder?: string
    searchPlaceholder?: string
    emptyText?: string
    /** 底部数据来源说明，如「数据来自物料主数据」。 */
    sourceText?: string
    loading?: boolean
    disabled?: boolean
    /** 允许清除已选值（触发按钮右侧出现清除叉）。 */
    clearable?: boolean
    id?: string
    ariaLabel?: string
    class?: HTMLAttributes['class']
  }>(),
  {
    variant: 'dropdown',
    placeholder: '请选择',
    searchPlaceholder: '搜索名称 / 编码…',
    emptyText: '无匹配实体',
    loading: false,
    disabled: false,
    clearable: false,
  },
)

const emit = defineEmits<{ (e: 'update:modelValue', value: string): void }>()

const open = ref(false)

const selected = computed(() => props.options.find((o) => o.value === props.modelValue))
const searchAriaLabel = computed(() => `搜索${props.ariaLabel ?? props.title}`)

/**
 * 开关闸门 —— 修「下拉一闪就没」。
 *
 * 症状是点一下触发器，浮层出现后立刻自己关掉。成因是同一次交互产生了**两次**开关：
 * 常见来源有祖先 `<label for>` 把点击又转发一次到被标记的控件、`as-child` 触发器上
 * 点击与指针事件各触发一次、以及浮层挂载瞬间把这次点击当成了「层外点击」。
 * 无论哪一条，表现都一样：`open` 被连着置 true 再置 false。
 *
 * 这里只堵一件很窄的事：**跟打开动作处在同一轮事件循环里的关闭，一律忽略**。
 * 用户后续真正的关闭操作（Esc、点外面、选中一项、再点一次触发器）都发生在之后的
 * 事件轮次，不受影响；打开动作永远放行，所以不会出现「点了没反应」。
 */
let justOpened = false

function setOpen(next: boolean) {
  // 只吞「跟打开动作同一轮事件循环」里发出的关闭。用户之后真的再点一次触发器
  // 仍然正常收起（切换语义不变），所以这不是把关闭按钮焊死。
  if (!next && justOpened) return
  if (next && !open.value) {
    justOpened = true
    setTimeout(() => {
      justOpened = false
    }, 0)
  }
  open.value = next
}

function pick(option: EntityPickerOption) {
  emit('update:modelValue', option.value)
  open.value = false
}

function clear() {
  emit('update:modelValue', '')
}
</script>

<template>
  <component
    :is="variant === 'dialog' ? DialogRoot : PopoverRoot"
    :open="open"
    @update:open="setOpen"
  >
    <div :class="cn('relative flex w-full items-center', props.class)" data-slot="nv-entity-picker">
      <component :is="variant === 'dialog' ? DialogTrigger : PopoverTrigger" as-child>
        <button
          :id="id"
          type="button"
          :aria-label="ariaLabel"
          :aria-haspopup="variant === 'dialog' ? 'dialog' : 'listbox'"
          :aria-expanded="open"
          :disabled="disabled"
          :class="
            cn(
              'flex h-9 w-full items-center justify-between gap-2 rounded-md border border-input bg-card px-3 text-sm outline-none focus-visible:ring-2 focus-visible:ring-ring/50 disabled:cursor-not-allowed disabled:opacity-50 dark:bg-input/30',
              // 有清除叉时给右侧让位，否则长文案会钻到叉底下。
              // 叉在 right-8（2rem）起、size-5（1.25rem）宽，收边到 3.25rem，再留一点余量。
              clearable && selected && !disabled && 'pr-14',
            )
          "
        >
          <span
            :class="cn('line-clamp-1 text-left', !selected && 'text-muted-foreground')"
            :title="selected ? `${selected.label}（${selected.value}）` : undefined"
          >
            <template v-if="selected">
              {{ selected.label }}
              <span class="text-muted-foreground">（{{ selected.value }}）</span>
            </template>
            <template v-else>{{ loading ? '加载中…' : placeholder }}</template>
          </span>
          <ChevronsUpDownIcon class="size-4 shrink-0 text-muted-foreground" aria-hidden="true" />
        </button>
      </component>
      <!-- 清除叉压在触发器上方，必须把指针事件彻底截断：只要有一个 pointerdown /
           mousedown 漏到下面的触发器，点「清除」就会顺带把浮层打开。 -->
      <button
        v-if="clearable && selected && !disabled"
        type="button"
        class="absolute right-8 flex size-5 items-center justify-center rounded-sm text-muted-foreground hover:bg-muted hover:text-foreground focus-visible:ring-2 focus-visible:ring-ring/50 focus-visible:outline-none"
        :aria-label="`清除${ariaLabel ?? '所选实体'}`"
        @pointerdown.stop.prevent
        @mousedown.stop.prevent
        @click.stop.prevent="clear"
      >
        <XIcon class="size-3.5" aria-hidden="true" />
      </button>
    </div>

    <!-- 下拉形态：原地展开，浮层 portal 到 body，不被 Sheet/Dialog 的 overflow 裁剪。 -->
    <PopoverPortal v-if="variant !== 'dialog'">
      <PopoverContent
        align="start"
        :side-offset="4"
        class="z-50 w-(--reka-popover-trigger-width) min-w-72 overflow-hidden rounded-lg border border-border bg-popover text-popover-foreground shadow-md outline-none data-open:animate-in data-open:fade-in-0 data-closed:animate-out data-closed:fade-out-0"
        @open-auto-focus.prevent
      >
        <EntityPickerPanel
          :options="options"
          :model-value="modelValue"
          :search-placeholder="searchPlaceholder"
          :empty-text="emptyText"
          :source-text="sourceText"
          :loading="loading"
          :search-aria-label="searchAriaLabel"
          dense
          @pick="pick"
        />
      </PopoverContent>
    </PopoverPortal>

    <!-- 弹窗形态：留给多列 / 分页的重场景。 -->
    <NvDialogContent v-else class="max-w-lg gap-0 p-0" @open-auto-focus.prevent>
      <div class="border-b border-border px-6 py-4">
        <DialogTitle class="text-base leading-none font-semibold">{{ title }}</DialogTitle>
        <DialogDescription class="sr-only">
          {{ sourceText ?? `搜索并选择${title.replace(/^选择/, '')}` }}
        </DialogDescription>
      </div>
      <EntityPickerPanel
        :options="options"
        :model-value="modelValue"
        :search-placeholder="searchPlaceholder"
        :empty-text="emptyText"
        :source-text="sourceText"
        :loading="loading"
        :search-aria-label="searchAriaLabel"
        :dense="false"
        @pick="pick"
      />
    </NvDialogContent>
  </component>
</template>
