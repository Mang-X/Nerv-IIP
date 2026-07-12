<script setup lang="ts">
import type {
  BusinessConsoleApprovalChainItem,
  BusinessConsoleApprovalDecisionItem,
  BusinessConsoleApprovalDecisionListItem,
  BusinessConsoleApprovalStepItem,
  BusinessConsoleApprovalTemplateItem,
} from '@nerv-iip/api-client'
import { useBusinessApproval } from '@/composables/useBusinessApproval'
import { BUSINESS_PERMISSION_CODES as P } from '@/permissions'
import { useAuthStore } from '@/stores/auth'
import { notifyError, notifySuccess } from '@/utils/notify'
import {
  NvButton,
  NvField,
  NvFieldLabel,
  NvSelect,
  NvSelectContent,
  NvSelectItem,
  NvSelectTrigger,
  NvSelectValue,
  Spinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import { ExternalLinkIcon, RefreshCwIcon, SendIcon } from 'lucide-vue-next'
import { computed, shallowRef, watch } from 'vue'
import { RouterLink } from 'vue-router'

const props = withDefaults(
  defineProps<{
    modelValue?: string
    sourceService: string
    documentType: string
    documentId?: string
    title?: string
    allowStart?: boolean
  }>(),
  {
    allowStart: true,
  },
)

const emit = defineEmits<{
  'update:modelValue': [value: string]
}>()

const auth = useAuthStore()
const actorRef = computed(() => auth.principal?.principalId ?? auth.principal?.loginName ?? '')
const actor = computed(() => ({ actorType: 'user', actorRef: actorRef.value }))
const permissionCodes = computed(() => auth.principal?.permissionCodes ?? [])
const canManageApprovals = computed(() => permissionCodes.value.includes(P.approvalsManage))

const approval = useBusinessApproval(actor)
const selectedTemplateCode = shallowRef('')

const activeTemplates = computed(() =>
  approval.templates.value.filter((template) => template.isActive !== false),
)
const boundChainId = computed(() => props.modelValue?.trim() ?? '')
const hasDurableDocumentId = computed(() => !!props.documentId?.trim())
const autoMatchedChain = computed(() =>
  hasDurableDocumentId.value && approval.chains.value.length === 1
    ? approval.chains.value[0]
    : undefined,
)
const selectedChain = computed(() => {
  const chainId = boundChainId.value
  if (chainId) {
    return (
      approval.chains.value.find((chain) => chain.chainId === chainId) ?? chainFromDetail(chainId)
    )
  }
  return autoMatchedChain.value
})
const displayedChainId = computed(() => boundChainId.value || autoMatchedChain.value?.chainId || '')
const displayedDetail = computed(() => {
  const detail = approval.chainDetail.value
  return detail?.chainId === displayedChainId.value ? detail : undefined
})
const displayedSteps = computed(() => displayedDetail.value?.steps ?? [])
const displayedDecisions = computed(() => {
  if (!displayedChainId.value) return []
  const detailDecisions = displayedDetail.value?.decisions ?? []
  if (detailDecisions.length) return detailDecisions
  return approval.decisions.value
})
const legacyReferenceLabel = computed(() =>
  boundChainId.value && !selectedChain.value && !approval.chainDetailPending.value
    ? boundChainId.value
    : '',
)
const canAttachDisplayedChain = computed(
  () => !!autoMatchedChain.value?.chainId && autoMatchedChain.value.chainId !== boundChainId.value,
)
const startDisabled = computed(
  () =>
    !props.documentId?.trim() ||
    !selectedTemplateCode.value ||
    approval.startChainPending.value ||
    !canManageApprovals.value,
)
const approvalCenterTo = computed(() => ({
  path: '/approval',
  query: {
    sourceService: props.sourceService,
    documentType: props.documentType,
    ...(props.documentId ? { documentId: props.documentId } : {}),
  },
}))

watch(
  () => [props.sourceService, props.documentType, props.documentId] as const,
  ([sourceService, documentType, documentId]) => {
    approval.templateFilters.documentType = documentType
    approval.templateFilters.isActive = true
    approval.chainFilters.sourceService = sourceService
    approval.chainFilters.documentType = documentType
    approval.chainFilters.documentId = documentId?.trim() || undefined
    approval.decisionFilters.documentType = documentType
    approval.decisionFilters.documentId = documentId?.trim() || undefined
  },
  { immediate: true },
)

watch(
  activeTemplates,
  (templates) => {
    if (templates.some((template) => template.templateCode === selectedTemplateCode.value)) return
    selectedTemplateCode.value = templates[0]?.templateCode ?? ''
  },
  { immediate: true },
)

watch(
  displayedChainId,
  (chainId) => {
    approval.chainDetailSelection.chainId = chainId
    approval.decisionFilters.chainId = chainId || undefined
  },
  { immediate: true },
)

function chainFromDetail(chainId: string): BusinessConsoleApprovalChainItem | undefined {
  const detail = approval.chainDetail.value
  if (!detail || detail.chainId !== chainId) return undefined
  return {
    chainId: detail.chainId,
    organizationId: detail.organizationId ?? '',
    environmentId: detail.environmentId ?? '',
    templateCode: detail.templateCode ?? '',
    templateVersion: detail.templateVersion ?? 0,
    status: detail.status ?? '',
    sourceService: detail.sourceService ?? props.sourceService,
    documentType: detail.documentType ?? props.documentType,
    documentId: detail.documentId ?? props.documentId ?? '',
    documentLineId: detail.documentLineId,
    startedBy: '',
    startedAtUtc: '',
    completedAtUtc: undefined,
  }
}

async function startApprovalChain() {
  if (startDisabled.value || !props.documentId) return

  try {
    const result = await approval.startChain({
      templateCode: selectedTemplateCode.value,
      sourceService: props.sourceService,
      documentType: props.documentType,
      documentId: props.documentId,
    })
    const chainId = result.data?.chainId ?? ''
    if (chainId) {
      emit('update:modelValue', chainId)
      approval.chainDetailSelection.chainId = chainId
      notifySuccess('审批链已发起')
      return
    }
    notifyError(result, '审批链发起未成功，请确认模板与单据状态。')
  } catch (error) {
    notifyError(error, '审批链发起失败，请确认模板、权限和单据状态后重试。')
  }
}

function chooseChain(value: unknown) {
  if (typeof value !== 'string') return
  emit('update:modelValue', value)
}

function attachDisplayedChain() {
  const chainId = autoMatchedChain.value?.chainId
  if (!chainId) return
  emit('update:modelValue', chainId)
}

function stepTitle(step: BusinessConsoleApprovalStepItem) {
  return step.stepName ?? `第 ${step.stepNo} 步`
}

function actorLabel(step: BusinessConsoleApprovalStepItem) {
  const actorType = step.approverType || '处理人'
  const actorRef = step.approverRef || '未分配'
  return `${actorType} · ${actorRef}`
}

function decisionKey(
  decision: BusinessConsoleApprovalDecisionItem | BusinessConsoleApprovalDecisionListItem,
) {
  return decision.decisionId ?? `${decision.stepNo}-${decision.actorRef}-${decision.decision}`
}

function templateLabel(template: BusinessConsoleApprovalTemplateItem) {
  return template.templateCode ?? template.documentType ?? '审批模板'
}
</script>

<template>
  <section class="grid gap-3 rounded-md border bg-card p-3" aria-label="单据审批链">
    <div class="flex flex-wrap items-center justify-between gap-2">
      <div>
        <h2 class="text-sm font-semibold text-foreground">{{ title ?? '审批链' }}</h2>
        <p class="text-xs text-muted-foreground">
          {{ documentType }}<template v-if="documentId"> · {{ documentId }}</template>
        </p>
      </div>
      <NvButton size="sm" type="button" variant="outline" as-child>
        <RouterLink :to="approvalCenterTo">
          <ExternalLinkIcon aria-hidden="true" />
          审批中心
        </RouterLink>
      </NvButton>
    </div>

    <div class="grid gap-2 rounded-md border bg-muted/20 p-3">
      <div class="flex flex-wrap items-center justify-between gap-2">
        <div class="grid gap-1">
          <span class="text-xs text-muted-foreground">当前链路</span>
          <span class="text-sm font-medium break-all">{{
            displayedChainId || '尚未关联审批链'
          }}</span>
          <span v-if="legacyReferenceLabel" class="text-xs text-muted-foreground">
            历史登记：<span class="font-medium break-all text-foreground">{{
              legacyReferenceLabel
            }}</span>
          </span>
        </div>
        <NvStatusBadge v-if="selectedChain?.status" :value="selectedChain.status" />
      </div>

      <NvField v-if="approval.chains.value.length">
        <NvFieldLabel>关联已有审批链</NvFieldLabel>
        <NvSelect :model-value="displayedChainId" @update:model-value="chooseChain">
          <NvSelectTrigger><NvSelectValue placeholder="选择审批链" /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="chain in approval.chains.value"
              :key="chain.chainId"
              :value="chain.chainId ?? ''"
            >
              {{ chain.chainId }} · {{ chain.status }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </NvField>
      <div v-if="canAttachDisplayedChain" class="flex justify-end">
        <NvButton size="sm" type="button" variant="outline" @click="attachDisplayedChain"
          >关联此审批链</NvButton
        >
      </div>

      <div
        v-if="approval.chainsPending.value"
        class="flex items-center gap-2 text-sm text-muted-foreground"
      >
        <Spinner aria-hidden="true" />
        正在加载审批链…
      </div>
      <p v-else-if="approval.chainsError.value" class="text-sm text-destructive" role="alert">
        审批链加载失败，请稍后重试。
      </p>
      <p v-else-if="!selectedChain" class="text-sm text-muted-foreground">
        当前单据还没有审批链，可发起新审批或到审批中心关联已有链路。
      </p>
    </div>

    <div v-if="displayedSteps.length" class="grid gap-2">
      <div class="text-xs font-medium text-muted-foreground">当前步骤</div>
      <ol class="grid gap-2">
        <li
          v-for="step in displayedSteps"
          :key="step.stepNo"
          class="rounded-md border bg-background p-2"
        >
          <div class="flex items-center justify-between gap-2">
            <span class="text-sm font-medium">{{ stepTitle(step) }}</span>
            <NvStatusBadge :value="step.status" />
          </div>
          <p class="mt-1 text-xs text-muted-foreground">{{ actorLabel(step) }}</p>
        </li>
      </ol>
    </div>

    <div v-if="displayedDecisions.length" class="grid gap-2">
      <div class="text-xs font-medium text-muted-foreground">历史决策</div>
      <ul class="grid gap-2">
        <li
          v-for="decision in displayedDecisions"
          :key="decisionKey(decision)"
          class="rounded-md border bg-background p-2 text-sm"
        >
          <div class="flex items-center justify-between gap-2">
            <span>{{ decision.actorRef ?? '处理人' }}</span>
            <NvStatusBadge :value="decision.decision" />
          </div>
          <p v-if="decision.comment" class="mt-1 text-xs text-muted-foreground">
            {{ decision.comment }}
          </p>
        </li>
      </ul>
    </div>

    <div
      v-if="allowStart !== false"
      class="grid gap-2 rounded-md border border-dashed bg-muted/20 p-3"
    >
      <NvField>
        <NvFieldLabel>发起审批模板</NvFieldLabel>
        <NvSelect v-model="selectedTemplateCode" :disabled="!activeTemplates.length">
          <NvSelectTrigger><NvSelectValue placeholder="选择审批模板" /></NvSelectTrigger>
          <NvSelectContent>
            <NvSelectItem
              v-for="template in activeTemplates"
              :key="template.templateCode"
              :value="template.templateCode ?? ''"
            >
              {{ templateLabel(template) }}
            </NvSelectItem>
          </NvSelectContent>
        </NvSelect>
      </NvField>
      <p v-if="!activeTemplates.length" class="text-sm text-muted-foreground">
        没有可用审批模板，请到审批中心维护模板后再发起。
      </p>
      <p v-else-if="!documentId" class="text-sm text-muted-foreground">
        当前业务单据尚未形成可引用编号，不能直接发起审批。
      </p>
      <p v-else-if="!canManageApprovals" class="text-sm text-muted-foreground">
        当前账号没有发起审批权限。
      </p>
      <div class="flex justify-end gap-2">
        <NvButton
          size="sm"
          type="button"
          variant="outline"
          :disabled="approval.chainsPending.value"
          @click="approval.refreshAll"
        >
          <RefreshCwIcon aria-hidden="true" />
          刷新审批
        </NvButton>
        <NvButton size="sm" type="button" :disabled="startDisabled" @click="startApprovalChain">
          <Spinner v-if="approval.startChainPending.value" aria-hidden="true" />
          <SendIcon v-else aria-hidden="true" />
          发起审批
        </NvButton>
      </div>
    </div>
  </section>
</template>
