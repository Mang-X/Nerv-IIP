<script setup lang="ts">
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import { useBusinessWorkers, useTeamMembers } from '@/composables/useBusinessMasterData'
import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  Button,
  Checkbox,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Field,
  FieldLabel,
  Spinner,
  StatusBadge,
  toast,
} from '@nerv-iip/ui'
import { Trash2Icon } from 'lucide-vue-next'
import { computed, ref, toRef, watch } from 'vue'

const props = defineProps<{
  teamCode: string
  teamName: string
}>()

const open = defineModel<boolean>('open', { default: false })

const teamCodeRef = toRef(props, 'teamCode')
const { members, membersPending, memberError, addMember, addPending, removeMember, removePending, refresh }
  = useTeamMembers(teamCodeRef)

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

const canAdd = computed(() => Boolean(selectedUserId.value))
const errorText = computed(() => {
  const error = memberError.value
  if (!error) return ''
  return error instanceof Error ? error.message : '请求失败，请稍后重试。'
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
    toast.success('已添加成员。')
    selectedUserId.value = ''
    isLeader.value = false
    showErrors.value = false
  }
  catch (error) {
    toast.error(error instanceof Error ? error.message : '添加失败，请稍后重试。')
  }
}

async function confirmRemove() {
  const userId = removeTarget.value
  if (!userId) return
  try {
    await removeMember(userId)
    toast.success('已移除成员。')
    removeTarget.value = null
  }
  catch (error) {
    toast.error(error instanceof Error ? error.message : '移除失败，请稍后重试。')
  }
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-2xl">
      <DialogHeader>
        <DialogTitle>{{ teamName }} · 成员维护</DialogTitle>
        <DialogDescription>登记班组成员与组长，移除时仅解除归属、不影响人员档案。</DialogDescription>
      </DialogHeader>

      <p v-if="errorText" class="text-sm text-destructive" role="alert">{{ errorText }}</p>

      <form class="grid gap-3 sm:grid-cols-[1fr_auto_auto] sm:items-end" @submit.prevent="submitAdd">
        <Field :data-invalid="showErrors && !canAdd">
          <FieldLabel for="member-worker">工人 <span class="text-destructive">*</span></FieldLabel>
          <WorkerSelect id="member-worker" v-model="selectedUserId" placeholder="搜索并选择工人" />
        </Field>
        <Field class="flex flex-row items-center gap-2">
          <Checkbox id="member-leader" v-model="isLeader" />
          <FieldLabel for="member-leader" class="mb-0">设为组长</FieldLabel>
        </Field>
        <Button type="submit" size="sm" :disabled="addPending">
          <Spinner v-if="addPending" aria-hidden="true" />添加成员
        </Button>
      </form>

      <div class="rounded-md border">
        <ul class="divide-y">
          <li v-if="membersPending" class="px-3 py-3 text-sm text-muted-foreground">加载成员中…</li>
          <li v-else-if="members.length === 0" class="px-3 py-3 text-sm text-muted-foreground">暂无成员，使用上方表单添加。</li>
          <li
            v-for="member in members"
            v-else
            :key="member.userId"
            class="flex items-center justify-between gap-3 px-3 py-2"
          >
            <div class="flex items-center gap-2">
              <span class="text-sm">{{ memberLabel(member.userId) }}</span>
              <StatusBadge v-if="member.isLeader" value="active" />
              <span v-if="member.isLeader" class="text-xs text-muted-foreground">组长</span>
            </div>
            <Button
              type="button"
              variant="ghost"
              size="sm"
              :disabled="removePending"
              :aria-label="`移除成员 ${memberLabel(member.userId)}`"
              @click="removeTarget = member.userId ?? null"
            >
              <Trash2Icon aria-hidden="true" />移除
            </Button>
          </li>
        </ul>
      </div>

      <DialogFooter>
        <Button type="button" variant="outline" @click="open = false">关闭</Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>

  <AlertDialog :open="removeTarget !== null" @update:open="(value) => { if (!value) removeTarget = null }">
    <AlertDialogContent>
      <AlertDialogHeader>
        <AlertDialogTitle>确认移除该成员？</AlertDialogTitle>
        <AlertDialogDescription>移除后该工人将不再归属本班组，可随时重新添加。</AlertDialogDescription>
      </AlertDialogHeader>
      <AlertDialogFooter>
        <AlertDialogCancel>取消</AlertDialogCancel>
        <AlertDialogAction :disabled="removePending" @click="confirmRemove">确认移除</AlertDialogAction>
      </AlertDialogFooter>
    </AlertDialogContent>
  </AlertDialog>
</template>
