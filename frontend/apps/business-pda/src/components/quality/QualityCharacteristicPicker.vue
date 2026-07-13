<script setup lang="ts">
import type { BusinessConsoleInspectionPlanCharacteristicItem } from '@nerv-iip/api-client'
import { NvBottomSheet, NvListRow, NvMobileTag, NvSearchBar } from '@nerv-iip/ui-mobile'
import { computed, ref, watch } from 'vue'

type PlanCharacteristic = BusinessConsoleInspectionPlanCharacteristicItem

const props = defineProps<{
  /** 可选特性（已由调用方排除已添加项）。 */
  characteristics: PlanCharacteristic[]
}>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ pick: [characteristic: PlanCharacteristic] }>()

const search = ref('')
watch(open, (isOpen) => {
  if (isOpen) search.value = ''
})

const filtered = computed<PlanCharacteristic[]>(() => {
  const kw = search.value.trim().toLowerCase()
  if (!kw) return props.characteristics
  return props.characteristics.filter((c) => {
    const code = (c.characteristicCode ?? '').toLowerCase()
    return (c.name ?? '').toLowerCase().includes(kw) || code.includes(kw)
  })
})

function pick(c: PlanCharacteristic) {
  emit('pick', c)
  open.value = false
}
</script>

<template>
  <NvBottomSheet :open="open" title="选择检验特性" @update:open="open = $event">
    <div class="space-y-3 pb-2">
      <NvSearchBar v-model="search" placeholder="搜索特性名 / 编码" />
      <div
        v-if="filtered.length === 0"
        class="px-4 py-6 text-center text-sm text-muted-foreground"
      >
        无匹配的检验特性
      </div>
      <div v-else class="max-h-[50vh] overflow-y-auto rounded-lg border border-border">
        <NvListRow
          v-for="c in filtered"
          :key="c.characteristicCode"
          data-testid="char-option"
          :title="c.name || c.characteristicCode || ''"
          :subtitle="`${c.characteristicCode}${c.characteristicType === 'attribute' ? ' · 计数' : ' · 计量'}${c.unitCode ? ` · ${c.unitCode}` : ''}`"
          @select="pick(c)"
        >
          <template #trailing>
            <NvMobileTag :variant="c.characteristicType === 'attribute' ? 'warning' : 'default'">
              {{ c.characteristicType === 'attribute' ? '计数' : '计量' }}
            </NvMobileTag>
          </template>
        </NvListRow>
      </div>
    </div>
  </NvBottomSheet>
</template>
