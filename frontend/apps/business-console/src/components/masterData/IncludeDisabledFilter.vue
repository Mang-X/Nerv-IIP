<script setup lang="ts">
import { NvCheckbox } from '@nerv-iip/ui'

/**
 * 主数据列表页的「包含停用」过滤开关（#1594）。
 *
 * 收成组件而不是每页抄一段 label + checkbox：措辞与形态只有一处，
 * 下次要改成 Switch 或加提示也只改这里。
 */
const includeDisabled = defineModel<boolean>({ required: true })
</script>

<template>
  <label class="flex items-center gap-2 text-sm text-muted-foreground">
    <NvCheckbox v-model="includeDisabled" data-testid="include-disabled" />
    包含停用
  </label>
</template>
