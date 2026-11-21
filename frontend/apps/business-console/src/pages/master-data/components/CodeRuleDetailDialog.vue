<script setup lang="ts">
import FormSectionTitle from '@/components/masterData/FormSectionTitle.vue'
import { CODE_RULE_VERSION_LABELS, labelFor } from '@/data/businessLabels'
import {
  NvButton,
  NvDialog,
  NvDialogContent,
  NvDialogDescription,
  NvDialogHeader,
  NvDialogTitle,
  NvSpinner,
  NvStatusBadge,
} from '@nerv-iip/ui'
import type { ShallowUnwrapRef } from 'vue'
import type { useCodeRulesPage } from '../useCodeRulesPage'

type View = ShallowUnwrapRef<ReturnType<typeof useCodeRulesPage>>

defineProps<{
  previewViewSample: View['previewViewSample']
  ruleStatusLabel: View['ruleStatusLabel']
  ruleStatusTone: View['ruleStatusTone']
  scopeLabel: View['scopeLabel']
  segmentParams: View['segmentParams']
  segmentTypeLabel: View['segmentTypeLabel']
  versions: View['versions']
  versionsError: View['versionsError']
  versionsPending: View['versionsPending']
  viewPreview: View['viewPreview']
  viewPreviewPending: View['viewPreviewPending']
  viewSegments: View['viewSegments']
  viewTarget: View['viewTarget']
}>()

const viewOpen = defineModel<View['viewOpen']>('viewOpen', { required: true })
</script>

<template>
  <NvDialog v-model:open="viewOpen">
    <NvDialogContent class="sm:max-w-4xl">
      <NvDialogHeader>
        <NvDialogTitle>编码规则 · 明细</NvDialogTitle>
        <NvDialogDescription>
          {{ viewTarget ? `${viewTarget.ruleKey} · ${viewTarget.displayName ?? ''}` : '' }}
        </NvDialogDescription>
      </NvDialogHeader>
      <div v-if="viewTarget" class="grid max-h-[70vh] content-start gap-4 overflow-y-auto px-1">
        <div class="grid gap-2 text-sm">
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">规则键</span>
            <span class="font-medium">{{ viewTarget.ruleKey ?? '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">名称</span>
            <span class="font-medium">{{ viewTarget.displayName ?? '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">适用对象</span>
            <span class="font-medium">{{ viewTarget.appliesTo ?? '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">范围</span>
            <span class="font-medium">{{ scopeLabel(viewTarget.scope) }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">当前版本</span>
            <span class="font-medium tabular-nums">{{ viewTarget.version ?? '—' }}</span>
          </div>
          <div class="flex justify-between gap-3">
            <span class="text-muted-foreground">状态</span>
            <NvStatusBadge
              :label="ruleStatusLabel(viewTarget)"
              :tone="ruleStatusTone(viewTarget)"
            />
          </div>
        </div>

        <div>
          <FormSectionTitle>段定义</FormSectionTitle>
          <div v-if="viewSegments.length" class="mt-2 overflow-x-auto rounded-md border">
            <table class="w-full text-sm">
              <thead class="bg-muted/40 text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 text-right font-medium">序号</th>
                  <th class="px-3 py-2 text-left font-medium">类型</th>
                  <th class="px-3 py-2 text-left font-medium">关键参数</th>
                  <th class="px-3 py-2 text-left font-medium">必填</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(segment, i) in viewSegments" :key="i" class="border-t">
                  <td class="px-3 py-2 text-right tabular-nums">{{ i + 1 }}</td>
                  <td class="px-3 py-2">{{ segmentTypeLabel(segment.type) }}</td>
                  <td class="px-3 py-2 text-muted-foreground">{{ segmentParams(segment) }}</td>
                  <td class="px-3 py-2">{{ segment.required ? '是' : '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="mt-2 rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            该规则没有段定义。
          </p>
        </div>

        <div class="grid gap-2">
          <div class="flex items-center gap-2">
            <NvButton
              type="button"
              variant="outline"
              size="sm"
              :disabled="viewPreviewPending"
              @click="previewViewSample"
            >
              <NvSpinner v-if="viewPreviewPending" aria-hidden="true" />
              预览示例编码
            </NvButton>
            <code v-if="viewPreview" class="rounded bg-muted px-2 py-1 text-sm font-mono">{{
              viewPreview
            }}</code>
          </div>
        </div>

        <div>
          <FormSectionTitle>版本历史</FormSectionTitle>
          <div
            v-if="versionsPending"
            class="mt-2 flex items-center gap-2 py-2 text-sm text-muted-foreground"
          >
            <NvSpinner aria-hidden="true" />
            加载版本历史…
          </div>
          <p
            v-else-if="versionsError"
            class="mt-2 rounded-md border border-destructive/30 bg-destructive/10 p-3 text-sm text-destructive"
            role="alert"
          >
            {{ versionsError }}
          </p>
          <div v-else-if="versions.length" class="mt-2 overflow-x-auto rounded-md border">
            <table class="w-full text-sm">
              <thead class="bg-muted/40 text-muted-foreground">
                <tr>
                  <th class="px-3 py-2 text-right font-medium">版本</th>
                  <th class="px-3 py-2 text-left font-medium">状态</th>
                  <th class="px-3 py-2 text-left font-medium">生效时间</th>
                  <th class="px-3 py-2 text-left font-medium">创建人</th>
                  <th class="px-3 py-2 text-left font-medium">变更原因</th>
                  <th class="px-3 py-2 text-left font-medium">创建时间</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="(ver, i) in versions" :key="i" class="border-t">
                  <td class="px-3 py-2 text-right tabular-nums">{{ ver.version ?? '—' }}</td>
                  <td class="px-3 py-2">
                    {{ labelFor(CODE_RULE_VERSION_LABELS, ver.status, '—') }}
                  </td>
                  <td class="px-3 py-2">{{ ver.effectiveFromUtc ?? '即时' }}</td>
                  <td class="px-3 py-2">{{ ver.createdBy ?? '—' }}</td>
                  <td class="px-3 py-2 text-muted-foreground">{{ ver.changeReason ?? '—' }}</td>
                  <td class="px-3 py-2 text-muted-foreground">{{ ver.createdAtUtc ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
          <p v-else class="mt-2 rounded-md border bg-muted/30 p-3 text-sm text-muted-foreground">
            暂无版本历史
          </p>
        </div>
      </div>
    </NvDialogContent>
  </NvDialog>
</template>
