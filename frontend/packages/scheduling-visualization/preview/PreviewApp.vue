<script setup lang="ts">
import { shallowRef } from 'vue'

import type {
  SchedulingPreviewCommand,
  SchedulingPreviewWindow,
  SchedulingWorkspaceSelection,
} from '../src'
import { SchedulingWorkspace } from '../src'

const lastSelection = shallowRef<SchedulingWorkspaceSelection>()
const lastCommand = shallowRef<SchedulingPreviewCommand>()
const committedPreview = shallowRef<Record<string, SchedulingPreviewWindow>>({})

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
    <header class="preview-header">
      <div>
        <p class="preview-eyebrow">Nerv-IIP package preview</p>
        <h1>Scheduling Visualization</h1>
      </div>
      <div class="preview-state" data-test="preview-state">
        <span>Selection: {{ lastSelection?.source ?? 'none' }}</span>
        <span>Preview: {{ lastCommand?.targetId ?? 'none' }}</span>
        <span>Committed: {{ Object.keys(committedPreview).length }}</span>
      </div>
    </header>

    <SchedulingWorkspace
      @selection-change="recordSelection"
      @preview-command="recordCommand"
      @commit-preview="recordCommit"
      @reset-preview="committedPreview = {}"
    />
  </main>
</template>

<style>
:root {
  --background: 0 0% 100%;
  --foreground: 222 47% 11%;
  --border: 214 32% 91%;
  --primary: 217 91% 60%;
  --primary-foreground: 0 0% 100%;
  --secondary: 210 40% 96%;
  --secondary-foreground: 222 47% 11%;
  --muted: 210 40% 96%;
  --muted-foreground: 215 16% 47%;
  --destructive: 0 84% 60%;
  --ring: 217 91% 60%;
  --input: 214 32% 91%;
  --popover: 0 0% 100%;
  --popover-foreground: 222 47% 11%;
}

* {
  box-sizing: border-box;
}

body {
  min-width: 320px;
  margin: 0;
  background: #eef2f7;
  color: #0f172a;
  font-family: "Segoe UI", system-ui, sans-serif;
}

.preview-shell {
  display: grid;
  gap: 16px;
  width: min(1440px, 100%);
  min-height: 100vh;
  margin: 0 auto;
  padding: 18px;
}

.preview-header {
  display: flex;
  align-items: end;
  justify-content: space-between;
  gap: 16px;
  padding: 14px 16px;
  border: 1px solid hsl(var(--border));
  border-radius: 8px;
  background: hsl(var(--background));
}

.preview-eyebrow {
  margin: 0;
  color: #475569;
  font-size: 12px;
  font-weight: 750;
}

.preview-header h1 {
  margin: 2px 0 0;
  font-size: 24px;
  letter-spacing: 0;
}

.preview-state {
  display: flex;
  flex-wrap: wrap;
  justify-content: flex-end;
  gap: 8px;
  color: #334155;
  font-size: 12px;
  font-weight: 650;
}

.preview-state span {
  padding: 5px 8px;
  border: 1px solid hsl(var(--border));
  border-radius: 7px;
  background: #f8fafc;
}

@media (max-width: 720px) {
  .preview-shell {
    padding: 10px;
  }

  .preview-header {
    align-items: start;
    flex-direction: column;
  }

  .preview-state {
    justify-content: flex-start;
  }
}
</style>

