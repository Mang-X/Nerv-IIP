<script setup lang="ts">
import type { HTMLAttributes } from 'vue'
import { PaletteIcon } from '@lucide/vue'
import { Button } from '../../ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '../../ui/dropdown-menu'
import { NEUTRAL_THEME, useTheme } from '../../../composables/useTheme'

const props = defineProps<{ class?: HTMLAttributes['class'] }>()

const { theme, setTheme, reset, presets } = useTheme()
const entries = Object.entries(presets) as [string, { primary: string; foreground: string }][]
</script>

<template>
  <!-- non-modal: this accent picker is a popover; it must not lock body scroll /
       set body pointer-events:none (reka's modal default). Uses原版 DropdownMenu,
       so pass modal at the call site rather than editing the primitive. -->
  <DropdownMenu :modal="false">
    <DropdownMenuTrigger as-child>
      <Button
        type="button"
        variant="ghost"
        size="icon"
        aria-label="选择主题"
        title="选择主题"
        :class="props.class"
      >
        <PaletteIcon class="size-4" aria-hidden="true" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-56">
      <DropdownMenuLabel>主题</DropdownMenuLabel>
      <div class="grid grid-cols-6 gap-2 p-2">
        <!-- 中性（黑白）：跟随亮/暗模式，构建期不固定颜色 -->
        <button
          type="button"
          class="size-6 rounded-full border border-border bg-foreground outline-none ring-offset-2 ring-offset-popover transition-transform hover:scale-110 focus-visible:ring-2 focus-visible:ring-ring"
          :class="theme === NEUTRAL_THEME ? 'ring-2 ring-ring' : ''"
          aria-label="主题 中性"
          :aria-pressed="theme === NEUTRAL_THEME"
          title="中性（黑白）"
          @click="setTheme(NEUTRAL_THEME)"
        />
        <button
          v-for="[name, preset] in entries"
          :key="name"
          type="button"
          class="size-6 rounded-full border border-border outline-none ring-offset-2 ring-offset-popover transition-transform hover:scale-110 focus-visible:ring-2 focus-visible:ring-ring"
          :class="theme === name ? 'ring-2 ring-ring' : ''"
          :style="{ backgroundColor: preset.primary }"
          :aria-label="`主题 ${name}`"
          :aria-pressed="theme === name"
          @click="setTheme(name)"
        />
      </div>
      <DropdownMenuSeparator />
      <div class="p-1">
        <Button type="button" variant="ghost" size="sm" class="w-full justify-start" @click="reset">
          恢复默认（中性）
        </Button>
      </div>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
