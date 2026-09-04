<script setup lang="ts">
import type {
  DowntimeReasonDirectoryState,
  DowntimeReasonOption,
} from '@/composables/useMaintenanceDowntimeReasonDirectory'
import { NvBottomSheet, NvListRow, NvMobileButton, NvSearchBar } from '@nerv-iip/ui-mobile'
import { computed, ref, watch } from 'vue'

const open = defineModel<boolean>('open', { default: false })
/**
 * 纯呈现件：目录状态由报修页持有的 `useMaintenanceDowntimeReasonDirectory` 唯一提供，
 * 组件自己不再开第二份查询——否则页面上的归因与抽屉里的归因会各说各话。
 */
const props = defineProps<{
  selectedCode?: string | null
  options: DowntimeReasonOption[]
  state: DowntimeReasonDirectoryState
  stateMessage: string
  canSelect: boolean
  truncated: boolean
  total: number
}>()
/** `null` = 用户明确选择「不登记设备不可用」——该路径提交 null，不是空串、不是伪默认码。 */
const emit = defineEmits<{
  select: [reason: DowntimeReasonOption | null]
  search: [keyword: string]
  retry: []
}>()

const searchKeyword = ref('')
const directoryBroken = computed(
  () => props.state === 'forbidden' || props.state === 'failed' || props.state === 'unavailable',
)

watch(open, (isOpen) => {
  if (isOpen) {
    searchKeyword.value = ''
    emit('search', '')
  }
})

// `NvSearchBar` 的清除按钮只置空 v-model、**不 emit `search`**（见 SearchBar.vue 的 `clear()`），
// 光靠 `@search` 会留下"输入框空了、列表还按旧关键字过滤"的错位。这条错位不只是界面矛盾：
// 空结果会显示"没有匹配的停机原因"，工人据此以为本组织没配这个码，转而选"不登记设备不可用"，
// 本该记录的停机原因就丢了——正是本页要防的事。所以清空即重查全量。
watch(searchKeyword, (value) => {
  if (value.trim().length === 0) emit('search', '')
})

function selectReason(reason: DowntimeReasonOption | null) {
  emit('select', reason)
  open.value = false
}

function clearSearch() {
  searchKeyword.value = ''
  emit('search', '')
}
</script>

<template>
  <NvBottomSheet
    :open="open"
    title="选择设备占用原因"
    description="只能选择本组织/环境已配置的停机原因；设备仍可用时选择「不登记设备不可用」"
    @update:open="open = $event"
  >
    <div class="space-y-3 pb-2">
      <div class="overflow-hidden rounded-lg border border-border">
        <NvListRow
          data-testid="reason-option-none"
          title="不登记设备不可用"
          subtitle="只提交报修，不登记设备停机"
          :class="props.selectedCode ? 'border-b-0' : 'border-b-0 bg-accent'"
          @select="selectReason(null)"
        />
      </div>

      <NvSearchBar
        v-model="searchKeyword"
        cancelable
        aria-label="停机原因关键字"
        placeholder="搜索停机原因"
        @search="emit('search', $event)"
        @cancel="clearSearch"
      />

      <!--
        目录读不出来时**只给错误态**：这里没有任何自由文本输入，也不塞默认码。
        写面唯一还能走的路是上面那条「不登记设备不可用」（提交 null）。
      -->
      <div
        v-if="directoryBroken"
        role="alert"
        data-testid="reason-directory-error"
        class="space-y-3 rounded-lg border border-destructive/40 bg-destructive/10 px-4 py-3 text-sm"
      >
        <p class="text-destructive">{{ props.stateMessage }}</p>
        <p class="text-muted-foreground">
          停机原因不可选时只能提交不登记设备停机的报修，不能手工填写原因。
        </p>
        <NvMobileButton
          v-if="props.state !== 'forbidden'"
          data-testid="reason-retry"
          variant="outline"
          size="lg"
          block
          @click="emit('retry')"
        >
          重试
        </NvMobileButton>
      </div>
      <div
        v-else-if="!props.canSelect"
        data-testid="reason-directory-state"
        class="rounded-lg border border-dashed border-border px-4 py-8 text-center text-sm text-muted-foreground"
      >
        {{ props.stateMessage }}
      </div>
      <template v-else>
        <!--
          一次只取一页且不翻页：超量组织必然被截断。不说出来的话，"翻不到的码"和
          "本组织没配这个码"在界面上完全一样，工人会改选"不登记设备不可用"。
        -->
        <p
          v-if="props.truncated"
          data-testid="reason-directory-truncated"
          class="text-xs text-muted-foreground"
        >
          共 {{ props.total }} 条停机原因，当前只显示前 {{ props.options.length }}
          条；没找到就用上面的搜索框按名称或码查找。
        </p>
        <div class="max-h-[48vh] overflow-y-auto rounded-lg border border-border">
          <NvListRow
            v-for="reason in props.options"
            :key="reason.code"
            :data-testid="`reason-option-${reason.code}`"
            :title="reason.name"
            :subtitle="reason.code"
            :class="reason.code === props.selectedCode ? 'bg-accent' : undefined"
            @select="selectReason(reason)"
          />
        </div>
      </template>
    </div>
  </NvBottomSheet>
</template>
