<script setup lang="ts">
import { Badge, Button, Card, CardContent, CardHeader, CardTitle } from '@nerv-iip/ui'
import { shallowRef } from 'vue'

import type {
  SchedulingPreviewCommand,
  SchedulingPreviewWindow,
  SchedulingWorkspaceSelection,
} from '../src'
import { SchedulingWorkspace, createLargeMockScheduleFixture } from '../src'

const lastSelection = shallowRef<SchedulingWorkspaceSelection>()
const lastCommand = shallowRef<SchedulingPreviewCommand>()
const committedPreview = shallowRef<Record<string, SchedulingPreviewWindow>>({})
const useLargeFixture = shallowRef(false)
const largeScheduleFixture = createLargeMockScheduleFixture({
  resourceCount: 1200,
  days: 730,
  operationsPerResource: 2,
})

function recordSelection(selection: SchedulingWorkspaceSelection | undefined) {
  lastSelection.value = selection
}

function recordCommand(command: SchedulingPreviewCommand) {
  lastCommand.value = command
}

function recordCommit(previewById: Record<string, SchedulingPreviewWindow>) {
  committedPreview.value = previewById
}
</script>

<template>
  <main class="preview-shell">
    <Card size="sm" class="preview-header">
      <CardHeader class="preview-header__content">
        <div class="preview-header__title">
          <p class="preview-eyebrow">Nerv-IIP package preview</p>
          <CardTitle class="preview-title">Scheduling Visualization</CardTitle>
        </div>
        <div class="preview-state" data-test="preview-state">
          <Button
            size="sm"
            :variant="useLargeFixture ? 'secondary' : 'outline'"
            data-test="preview-large-toggle"
            @click="useLargeFixture = !useLargeFixture"
          >
            {{ useLargeFixture ? 'Large data' : 'Base data' }}
          </Button>
          <Badge variant="outline">Selection: {{ lastSelection?.source ?? 'none' }}</Badge>
          <Badge variant="outline">Preview: {{ lastCommand?.targetId ?? 'none' }}</Badge>
          <Badge variant="secondary">Committed: {{ Object.keys(committedPreview).length }}</Badge>
        </div>
      </CardHeader>
    </Card>

    <Card size="sm" class="preview-workspace-card">
      <CardContent class="preview-workspace-card__content">
        <SchedulingWorkspace
          :schedule-fixture="useLargeFixture ? largeScheduleFixture : undefined"
          @selection-change="recordSelection"
          @preview-command="recordCommand"
          @commit-preview="recordCommit"
          @reset-preview="committedPreview = {}"
        />
      </CardContent>
    </Card>
  </main>
</template>

<style scoped>
.preview-shell {
  display: grid;
  align-content: start;
  gap: 16px;
  width: min(1440px, 100%);
  margin: 0 auto;
  padding: 16px;
}

.preview-header {
  overflow: visible;
}

.preview-header__content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.preview-header__title {
  display: grid;
  gap: 2px;
  min-width: 0;
}

.preview-eyebrow {
  margin: 0;
  color: hsl(var(--muted-foreground));
  font-size: 12px;
  font-weight: 750;
}

.preview-title {
  margin: 0;
  font-size: 24px;
  font-weight: 750;
  letter-spacing: 0;
}

.preview-state {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
}

.preview-workspace-card {
  overflow: visible;
}

.preview-workspace-card__content {
  min-width: 0;
}

@media (max-width: 720px) {
  .preview-shell {
    padding: 10px;
  }

  .preview-header__content {
    align-items: start;
    flex-direction: column;
  }

  .preview-state {
    justify-content: flex-start;
  }
}
</style>
