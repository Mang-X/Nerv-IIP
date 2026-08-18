<script setup lang="ts">
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import { useBusinessWorkers, useTeamMembers } from '@/composables/useBusinessMasterData'
import {
  NvAlertDialog,
  NvAlertDialogCancel,
  NvAlertDialogContent,
  NvAlertDialogDescription,
  NvAlertDialogFooter,
  NvAlertDialogHeader,
  NvAlertDialogTitle,
  NvButton,
  NvCheckbox,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogFooter,
  NvDialogHeader,
  NvDialogTitle,
  NvField,
  NvFieldDescription,
  NvFieldError,
  NvFieldLabel,
  NvInput,
  Spinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { Trash2Icon } from '@lucide/vue'
import { computed, ref, toRef, watch } from 'vue'
import { notifyError, notifyOperationFailure, notifySuccess } from '@/utils/notify'

const props = defineProps<{
  teamCode: string
  teamName: string
}>()

const open = defineModel<boolean>('open', { default: false })

const teamCodeRef = toRef(props, 'teamCode')
const {
  members,
  membersPending,
  membersError,
  addMember,
  addPending,
  removeMember,
  removePending,
  refresh,
} = useTeamMembers(teamCodeRef)

// 成员列表只携带 userId；经工人目录解析成姓名（工号）展示，绝不向用户暴露 userId。
const { workers } = useBusinessWorkers()
const workerLabelByUserId = computed(() => {
  const map = new Map<string, string>()
  for (const worker of workers.value) {
    if (!worker.userId) continue
    const suffix = worker.employeeNo ? `（${worker.employeeNo}）` : ''
    // displayName 缺失时降级为「未命名工人」，绝不把内部 userId 当展示名暴露（与 WorkerSelect 口径一致）。
    map.set(worker.userId, `${worker.displayName || '未命名工人'}${suffix}`)
  }
  return map
})
function memberLabel(userId: string | undefined) {
  if (!userId) return '未知工人'
  return workerLabelByUserId.value.get(userId) ?? '未知工人'
}

const selectedUserId = ref('')
const isLeader = ref(false)
const showErrors = ref(false)
const removeTarget = ref<string | null>(null)
const removeReason = ref('')
const removeReasonTouched = ref(false)

// 上限与 Gateway / MasterData 两层 validator 一致；原因按用户实际输入计数，提交时才 trim。
const REMOVE_REASON_MAX_LENGTH = 500
const removeReasonError = computed(() => {
  if (removeReason.value.length > REMOVE_REASON_MAX_LENGTH) {
    return `移除原因不能超过 ${REMOVE_REASON_MAX_LENGTH} 个字符。`
  }
  if (removeReason.value.length > 0 && removeReason.value.trim().length === 0) {
    return '移除原因不能只包含空白字符。'
  }
  if (removeReasonTouched.value && removeReason.value.trim().length === 0) {
    return '请输入移除原因。'
  }
  return ''
})
const canRemove = computed(
  () =>
    removeReason.value.trim().length > 0 &&
    removeReason.value.length <= REMOVE_REASON_MAX_LENGTH &&
    !removePending.value,
)

const canAdd = computed(() => Boolean(selectedUserId.value))

// 成员加载失败一律 toast，不在弹窗里留常驻错误条。
watch(membersError, (error) => {
  if (error) notifyError(error, '成员加载失败，请稍后重试。')
})

watch(open, (isOpen) => {
  if (isOpen) {
    showErrors.value = false
    selectedUserId.value = ''
    isLeader.value = false
    void refresh()
  }
})

async function submitAdd() {
  if (!canAdd.value) {
    showErrors.value = true
    return
  }
  try {
    await addMember({ userId: selectedUserId.value, isLeader: isLeader.value })
    notifySuccess('已添加成员。')
    selectedUserId.value = ''
    isLeader.value = false
    showErrors.value = false
  } catch (error) {
    notifyOperationFailure('添加成员失败', error, '添加成员失败，请稍后重试。')
  }
}

async function confirmRemove() {
  const userId = removeTarget.value
  const reason = removeReason.value.trim()
  removeReasonTouched.value = true
  if (!userId || !reason || removeReason.value.length > REMOVE_REASON_MAX_LENGTH) return
  try {
    await removeMember(userId, reason)
    notifySuccess('已移除成员。')
    resetRemove()
  } catch (error) {
    // 失败保留原因与当前成员，便于原地重试，不让真实审计描述丢失。
    notifyOperationFailure('移除成员失败', error, '移除成员失败，请稍后重试。')
  }
}

function requestRemove(userId: string | undefined) {
  if (!userId) return
  removeTarget.value = userId
  removeReason.value = ''
  removeReasonTouched.value = false
}

function resetRemove() {
  removeTarget.value = null
  removeReason.value = ''
  removeReasonTouched.value = false
}

function onRemoveOpenChange(value: boolean) {
  if (!value && !removePending.value) resetRemove()
}
</script>

<template>
  <NvDialog v-model:open="open">
    <NvDialogContent class="sm:max-w-2xl">
      <NvDialogHeader>
        <NvDialogTitle>{{ teamName }} · 成员维护</NvDialogTitle>
        <NvDialogDescription class="sr-only">班组 {{ teamCode }} 的成员</NvDialogDescription>
      </NvDialogHeader>

      <form
        class="grid gap-3 sm:grid-cols-[1fr_auto_auto] sm:items-end"
        @submit.prevent="submitAdd"
      >
        <NvField :data-invalid="showErrors && !canAdd">
          <NvFieldLabel for="member-worker"
            >工人 <span class="text-destructive">*</span></NvFieldLabel
          >
          <WorkerSelect id="member-worker" v-model="selectedUserId" placeholder="搜索并选择工人" />
        </NvField>
        <NvField orientation="horizontal">
          <NvCheckbox id="member-leader" v-model="isLeader" />
          <NvFieldLabel for="member-leader" class="mb-0">设为组长</NvFieldLabel>
        </NvField>
        <NvButton type="submit" size="sm" :disabled="addPending">
          <Spinner v-if="addPending" aria-hidden="true" />添加成员
        </NvButton>
      </form>

      <div class="rounded-md border">
        <ul class="divide-y">
          <li v-if="membersPending" class="px-3 py-3 text-sm text-muted-foreground">加载成员中…</li>
          <li v-else-if="members.length === 0" class="px-3 py-3 text-sm text-muted-foreground">
            暂无成员。
          </li>
          <li
            v-for="member in members"
            v-else
            :key="member.userId"
            class="flex items-center justify-between gap-3 px-3 py-2"
          >
            <div class="flex items-center gap-2">
              <span class="text-sm">{{ memberLabel(member.userId) }}</span>
              <NvStatusBadge v-if="member.isLeader" value="active" />
              <span v-if="member.isLeader" class="text-xs text-muted-foreground">组长</span>
            </div>
            <NvButton
              type="button"
              variant="ghost"
              size="sm"
              :disabled="removePending"
              :aria-label="`移除成员 ${memberLabel(member.userId)}`"
              @click="requestRemove(member.userId)"
            >
              <Trash2Icon aria-hidden="true" />移除
            </NvButton>
          </li>
        </ul>
      </div>

      <NvDialogFooter>
        <NvButton type="button" variant="outline" @click="open = false">关闭</NvButton>
      </NvDialogFooter>
    </NvDialogContent>
  </NvDialog>

  <NvAlertDialog :open="removeTarget !== null" @update:open="onRemoveOpenChange">
    <NvAlertDialogContent>
      <NvAlertDialogHeader>
        <NvAlertDialogTitle
          >确认移除成员「{{ memberLabel(removeTarget ?? undefined) }}」？</NvAlertDialogTitle
        >
        <NvAlertDialogDescription>
          移除后该工人不再归属本班组；本次原因会写入审计记录。
        </NvAlertDialogDescription>
      </NvAlertDialogHeader>
      <NvField :data-invalid="Boolean(removeReasonError)">
        <NvFieldLabel for="team-member-remove-reason">
          移除原因 <span class="text-destructive">*</span>
        </NvFieldLabel>
        <NvInput
          id="team-member-remove-reason"
          v-model="removeReason"
          required
          :maxlength="REMOVE_REASON_MAX_LENGTH"
          placeholder="说明调整依据，如调入其他班组、岗位变更"
          :invalid="Boolean(removeReasonError)"
          :aria-invalid="removeReasonError ? 'true' : undefined"
          :aria-describedby="
            removeReasonError ? 'team-member-remove-reason-error' : 'team-member-remove-reason-help'
          "
          @blur="removeReasonTouched = true"
        />
        <div class="flex items-start justify-between gap-3">
          <NvFieldError
            v-if="removeReasonError"
            id="team-member-remove-reason-error"
            :errors="[removeReasonError]"
          />
          <NvFieldDescription v-else id="team-member-remove-reason-help">
            请填写可供事后追溯的业务依据。
          </NvFieldDescription>
          <span class="ml-auto shrink-0 text-xs text-muted-foreground" aria-live="polite">
            {{ removeReason.length }} / {{ REMOVE_REASON_MAX_LENGTH }}
          </span>
        </div>
      </NvField>
      <NvAlertDialogFooter>
        <NvAlertDialogCancel :disabled="removePending">取消</NvAlertDialogCancel>
        <!-- 普通 NvButton，不用 NvAlertDialogAction：后者点击即无条件关框（confirm-destroy 规则 3）。 -->
        <NvButton type="button" variant="destructive" :disabled="!canRemove" @click="confirmRemove">
          <Spinner v-if="removePending" aria-hidden="true" />
          确认移除
        </NvButton>
      </NvAlertDialogFooter>
    </NvAlertDialogContent>
  </NvAlertDialog>
</template>
