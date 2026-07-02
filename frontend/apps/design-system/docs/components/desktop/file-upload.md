---
title: FileUpload 文件上传
---

<script setup lang="ts">
import {
  Button,
  FileUpload,
  fileUploadMotion,
  type FileUploadCompleteSessionRequest,
  type FileUploadCreateSessionRequest,
  type FileUploadExpose,
  type FileUploadSession,
  type FileUploadTransportContext,
} from '@nerv-iip/ui'
import { computed, nextTick, onMounted, ref, shallowRef } from 'vue'

const manualUpload = ref<FileUploadExpose | null>(null)
const autoUpload = ref<FileUploadExpose | null>(null)
const avatarUpload = ref<FileUploadExpose | null>(null)
const compactUpload = ref<FileUploadExpose | null>(null)
const galleryUpload = ref<FileUploadExpose | null>(null)
const tableUpload = ref<FileUploadExpose | null>(null)
const imageUpload = ref<FileUploadExpose | null>(null)
const completedFiles = shallowRef<string[]>([])
const sessionFileIds = new Map<string, string>()
let sessionCounter = 0

const completedLabel = computed(() =>
  completedFiles.value.length > 0 ? completedFiles.value.join('、') : '暂无完成文件',
)
const autoHasRows = computed(() => autoUpload.value?.hasRows ?? false)
const manualHasRows = computed(() => manualUpload.value?.hasRows ?? false)
const manualHasQueuedRows = computed(() => manualUpload.value?.hasQueuedRows ?? false)

function nextSession(request: FileUploadCreateSessionRequest): FileUploadSession {
  sessionCounter += 1
  const uploadSessionId = `demo-session-${sessionCounter}`
  const fileId = `demo-file-${sessionCounter}`
  sessionFileIds.set(uploadSessionId, fileId)

  return {
    uploadSessionId,
    fileId,
    uploadMode: 'tus',
    provider: 'tus',
    expiresAtUtc: '2099-01-01T00:00:00Z',
    upload: {
      url: `/api/console/v1/files/tus/${uploadSessionId}`,
      headers: {
        'x-demo-purpose': request.filePurpose,
      },
    },
  }
}

async function createUploadSession(request: FileUploadCreateSessionRequest) {
  return nextSession(request)
}

async function completeUploadSession(
  uploadSessionId: string,
  request: FileUploadCompleteSessionRequest,
) {
  return {
    fileId: sessionFileIds.get(uploadSessionId) ?? `${request.filePurpose}-${uploadSessionId}`,
  }
}

async function demoTransport({ file, onProgress, signal }: FileUploadTransportContext) {
  for (const progress of [18, 42, 76, 100]) {
    if (signal?.aborted) {
      throw new DOMException('Upload paused.', 'AbortError')
    }

    await new Promise(resolve => setTimeout(resolve, 90))
    onProgress(progress)
  }

  if (file.name.includes('fail')) {
    throw new Error('网络中断，请重试上传。')
  }
}

function rememberCompleted(files: Array<{ fileName: string }>) {
  completedFiles.value = files.map(file => file.fileName)
}

function demoImageFile(fileName: string, label: string, fill: string) {
  return new File([
    `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 480 320">
      <rect width="480" height="320" fill="${fill}"/>
      <rect x="28" y="28" width="424" height="264" rx="28" fill="rgba(255,255,255,.18)" stroke="rgba(255,255,255,.36)"/>
      <text x="240" y="170" fill="white" font-family="Arial, sans-serif" font-size="34" font-weight="700" text-anchor="middle" dominant-baseline="middle">${label}</text>
    </svg>`,
  ], fileName, { type: 'image/svg+xml' })
}

async function addManualSamples() {
  await manualUpload.value?.addFiles([
    new File(['inspection evidence'], 'IPQC-2406-17-evidence.pdf', { type: 'application/pdf' }),
    new File(['temporary failure'], 'IPQC-2406-17-fail.pdf', { type: 'application/pdf' }),
  ])
}

async function replayAutoSamples() {
  clearAutoSamples()
  await autoUpload.value?.addFiles([
    demoImageFile('first-article-photo.svg', '首件照片', '#334155'),
    new File(['incoming inspection report'], 'incoming-inspection.pdf', { type: 'application/pdf' }),
  ])
}

function clearAutoSamples() {
  autoUpload.value?.clear()
  completedFiles.value = []
}

async function addAvatarSample() {
  avatarUpload.value?.clear()
  await avatarUpload.value?.addFiles([
    demoImageFile('operator-avatar.svg', 'OP', '#475569'),
  ])
}

async function addCompactSamples() {
  compactUpload.value?.clear()
  await compactUpload.value?.addFiles([
    new File(['fixture checklist'], 'fixture-checklist.pdf', { type: 'application/pdf' }),
    new File(['shift note'], 'shift-note.txt', { type: 'text/plain' }),
  ])
}

async function addGallerySamples() {
  galleryUpload.value?.clear()
  await galleryUpload.value?.addFiles([
    demoImageFile('defect-front.svg', '正面', '#1f2937'),
    demoImageFile('defect-side.svg', '侧面', '#374151'),
    demoImageFile('defect-detail.svg', '细节', '#4b5563'),
  ])
}

async function addTableSamples() {
  tableUpload.value?.clear()
  await tableUpload.value?.addFiles([
    new File(['control plan'], 'control-plan.pdf', { type: 'application/pdf' }),
    demoImageFile('first-piece.svg', '首件', '#334155'),
    new File(['inspection data'], 'measurement.xlsx', { type: 'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet' }),
  ])
}

async function addImageSamples() {
  imageUpload.value?.clear()
  await imageUpload.value?.addFiles([
    demoImageFile('machine-panel.svg', '设备面板', '#1e293b'),
    demoImageFile('equipment-nameplate.svg', '铭牌', '#0f172a'),
  ])
}

onMounted(async () => {
  await nextTick()
  await addAvatarSample()
  await addCompactSamples()
  await addGallerySamples()
  await addTableSamples()
  await addImageSamples()
})
</script>

# FileUpload 文件上传

面向 FileStorage 上传会话的桌面上传组件。组件负责文件选择、队列、暂停、恢复、失败重试和虚拟列表；`queue`、画廊、表格等变体提供拖拽入口，上层负责把业务归属、创建上传会话、完成上传会话和传输实现传入。

本页所有示例都优先使用 `@nerv-iip/ui` 稳定导出，例如 `Button`、`FileUpload` 和 `uploadWithNativeFileStorageTransport`；不要从原始上游包或组件深层路径导入。

## 自动上传

点击按钮选择文件后立即创建上传会话并执行传输。适合质检证据、设备点检照片、工艺附件等单步上传场景。

<Demo title="自动上传" block>
  <div class="mb-3 flex flex-wrap gap-2">
    <Button type="button" variant="secondary" @click="replayAutoSamples">
      重播上传动效
    </Button>
    <Button type="button" variant="ghost" :disabled="!autoHasRows" @click="clearAutoSamples">
      清空
    </Button>
  </div>
  <FileUpload
    ref="autoUpload"
    purpose="quality-evidence"
    owner-service="Quality"
    owner-type="InspectionRecord"
    owner-id="inspection-2406-17"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['application/pdf', 'image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="4"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
    @completed="rememberCompleted"
  />
  <p class="text-muted-foreground text-sm">完成文件：{{ completedLabel }}</p>
</Demo>

```vue
<script setup lang="ts">
import { FileUpload, uploadWithNativeFileStorageTransport } from '@nerv-iip/ui'
</script>

<template>
  <FileUpload
    purpose="quality-evidence"
    owner-service="Quality"
    owner-type="InspectionRecord"
    owner-id="inspection-2406-17"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['application/pdf', 'image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="4"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
    @completed="files => bindEvidence(files)"
  />
</template>
```

## 队列上传

`variant="queue"` 使用拖拽面板、文件统计和内置添加/清空操作；`autoUpload=false` 时组件只入队，不创建上传会话。父级可以通过 `ref` 暴露的 `addFiles()` 和 `uploadQueued()` 组合外部按钮、批量保存或表单提交。

<Demo title="队列上传" block>
  <div class="mb-3 flex flex-wrap gap-2">
    <Button type="button" variant="secondary" @click="addManualSamples">
      添加质检附件
    </Button>
    <Button type="button" :disabled="!manualHasQueuedRows" @click="manualUpload?.uploadQueued()">
      开始上传
    </Button>
    <Button type="button" variant="ghost" :disabled="!manualHasRows" @click="manualUpload?.clear()">
      清空队列
    </Button>
  </div>
  <FileUpload
    ref="manualUpload"
    variant="queue"
    purpose="quality-evidence"
    owner-service="Quality"
    owner-type="InspectionRecord"
    owner-id="inspection-2406-17"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['application/pdf']"
    :max-files="6"
    :auto-upload="false"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
  />
</Demo>

```vue
<script setup lang="ts">
import { Button, FileUpload, uploadWithNativeFileStorageTransport, type FileUploadExpose } from '@nerv-iip/ui'
import { ref } from 'vue'

const upload = ref<FileUploadExpose | null>(null)
</script>

<template>
  <Button type="button" :disabled="!upload?.hasQueuedRows" @click="upload?.uploadQueued()">
    开始上传
  </Button>
  <FileUpload
    ref="upload"
    variant="queue"
    purpose="quality-evidence"
    owner-service="Quality"
    owner-type="InspectionRecord"
    owner-id="inspection-2406-17"
    organization-id="org-demo"
    environment-id="env-prod"
    :auto-upload="false"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
  />
</template>
```

## 头像上传

`variant="avatar"` 适合用户头像、操作员照片、设备主图等单文件场景。图片文件会自动生成缩略图，非图片文件仍回退到文件类型图标。

<Demo title="头像上传" block>
  <FileUpload
    ref="avatarUpload"
    variant="avatar"
    purpose="operator-avatar"
    owner-service="Iam"
    owner-type="UserProfile"
    owner-id="operator-2406"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="1"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
  />
</Demo>

```vue
<script setup lang="ts">
import { FileUpload, uploadWithNativeFileStorageTransport } from '@nerv-iip/ui'
</script>

<template>
  <FileUpload
    variant="avatar"
    purpose="operator-avatar"
    owner-service="Iam"
    owner-type="UserProfile"
    owner-id="operator-2406"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="1"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
  />
</template>
```

## 紧凑上传

`variant="compact"` 压缩 dropzone 与行高，适合表单侧栏、抽屉和空间受限的配置面板。

<Demo title="紧凑上传" block>
  <FileUpload
    ref="compactUpload"
    variant="compact"
    purpose="work-instruction-attachment"
    owner-service="ProductEngineering"
    owner-type="StandardOperation"
    owner-id="op-assembly-10"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['application/pdf', 'text/plain']"
    :max-files="4"
    :auto-upload="false"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
  />
</Demo>

```vue
<script setup lang="ts">
import { FileUpload, uploadWithNativeFileStorageTransport } from '@nerv-iip/ui'
</script>

<template>
  <FileUpload
    variant="compact"
    purpose="work-instruction-attachment"
    owner-service="ProductEngineering"
    owner-type="StandardOperation"
    owner-id="op-assembly-10"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['application/pdf', 'text/plain']"
    :max-files="4"
    :auto-upload="false"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
  />
</template>
```

## 画廊上传

`variant="gallery"` 以网格卡片展示图片证据，适合质检缺陷照片、设备巡检照片和现场佐证图集。

<Demo title="画廊上传" block>
  <FileUpload
    ref="galleryUpload"
    variant="gallery"
    purpose="quality-gallery"
    owner-service="Quality"
    owner-type="NonconformanceReport"
    owner-id="ncr-2406-21"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="6"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
  />
</Demo>

```vue
<script setup lang="ts">
import { FileUpload, uploadWithNativeFileStorageTransport } from '@nerv-iip/ui'
</script>

<template>
  <FileUpload
    variant="gallery"
    purpose="quality-gallery"
    owner-service="Quality"
    owner-type="NonconformanceReport"
    owner-id="ncr-2406-21"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="6"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
  />
</template>
```

## 表格上传

`variant="table"` 用连续行承载混合文件，适合批量附件清单、工程文档、检验记录和导入文件复核。

<Demo title="表格上传" block>
  <FileUpload
    ref="tableUpload"
    variant="table"
    purpose="engineering-documents"
    owner-service="ProductEngineering"
    owner-type="EngineeringDocument"
    owner-id="doc-pack-2406"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="[
      'application/pdf',
      'image/png',
      'image/svg+xml',
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    ]"
    :max-files="8"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
  />
</Demo>

```vue
<script setup lang="ts">
import { FileUpload, uploadWithNativeFileStorageTransport } from '@nerv-iip/ui'
</script>

<template>
  <FileUpload
    variant="table"
    purpose="engineering-documents"
    owner-service="ProductEngineering"
    owner-type="EngineeringDocument"
    owner-id="doc-pack-2406"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="[
      'application/pdf',
      'image/png',
      'image/svg+xml',
      'application/vnd.openxmlformats-officedocument.spreadsheetml.sheet',
    ]"
    :max-files="8"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
  />
</template>
```

## 图片上传

`variant="image"` 使用更宽的图片预览卡，适合设备铭牌、工位照片、首件照片等需要检查画面的场景。

<Demo title="图片上传" block>
  <FileUpload
    ref="imageUpload"
    variant="image"
    purpose="equipment-photo"
    owner-service="Maintenance"
    owner-type="EquipmentAsset"
    owner-id="eqp-press-07"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="4"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="demoTransport"
  />
</Demo>

```vue
<script setup lang="ts">
import { FileUpload, uploadWithNativeFileStorageTransport } from '@nerv-iip/ui'
</script>

<template>
  <FileUpload
    variant="image"
    purpose="equipment-photo"
    owner-service="Maintenance"
    owner-type="EquipmentAsset"
    owner-id="eqp-press-07"
    organization-id="org-demo"
    environment-id="env-prod"
    :accepted-content-types="['image/png', 'image/jpeg', 'image/svg+xml']"
    :max-files="4"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
    :transport="uploadWithNativeFileStorageTransport"
  />
</template>
```

## 接口边界

| 属性 / 事件 | 说明 |
| --- | --- |
| `purpose` | FileStorage `filePurpose`，例如 `quality-evidence` |
| `ownerService / ownerType / ownerId` | 业务归属，只作为上传会话请求载荷，不由组件解释业务语义 |
| `organizationId / environmentId` | 租户和环境范围，会传入创建与完成上传会话请求 |
| `acceptedContentTypes` | 前端选择和入队校验，组件会在上传入口显示推导后的可接受扩展名；传空数组表示不限制 |
| `maxFileSizeBytes / maxFiles` | 单文件大小和当前队列槽位限制，会进入上传入口的限制摘要；有队列时组件会显示文件计数和总大小 |
| `autoUpload` | 是否入队后自动创建会话并上传，默认 `true` |
| `variant` | 展示变体：`default / queue / compact / avatar / gallery / table / image`；默认是基础按钮，`queue` 是拖拽队列上传 |
| `virtualizeThreshold` | 超过该行数后启用虚拟列表，默认 `40` |
| `virtualRowHeight / virtualListHeight` | 虚拟列表行高和最大滚动高度 |
| `disabled` | 禁用文件选择；使用 dropzone 变体时同时禁用拖拽入队 |
| `class` | 透传到根容器，供页面做宽度或栅格控制 |
| `createUploadSession(request)` | 创建 FileStorage 上传会话 |
| `completeUploadSession(uploadSessionId, request)` | 传输完成后提交完成请求 |
| `transport(context)` | 真实传输实现，默认 `uploadWithNativeFileStorageTransport` |
| `completed(files)` | 当前队列内已完成文件快照；单个文件完成或移除已完成行后都会重新发出 |
| `rejected(files)` | 入队前被拒绝的文件与原因 |
| `failed(row)` | 上传失败的行；`default / queue / compact / table` 行内提供重试，`avatar / gallery / image` 可移除后重新选择，父级也可调用 `retryFailed()` 统一重试 |

## 暴露方法

| 方法 | 用途 |
| --- | --- |
| `addFiles(files)` | 从外部文件选择器、粘贴或业务模板加入队列 |
| `uploadQueued()` | 上传所有 queued 行 |
| `pauseAll() / resumeAll()` | 暂停或恢复上传中的行 |
| `retryFailed()` | 重试失败行，过期会话会重新创建 |
| `clear()` | 清空队列，不发出 synthetic completed 事件 |
| `browse()` | 打开原生文件选择器 |

## 暴露状态

| 状态 | 用途 |
| --- | --- |
| `hasRows` | 队列内是否存在任意文件行，可用于禁用清空按钮 |
| `hasQueuedRows` | 队列内是否存在待上传文件，可用于禁用开始上传按钮 |

## 动效

组件使用 `motion-v` 的 `MotionConfig reduced-motion="user"`、`AnimatePresence` 和 `motion.*`。`fileUploadMotion` 与 `filePreviewMotion` 共享 UI 包的 JS motion token 层：dropzone 使用 `fastInvoke`，行进入、移除和位置迁移使用 `pointToPointShort`。该 JS 层由契约测试与 `theme.css` 的 `--ease-* / --duration-*` 配对，避免 Motion for Vue 曲线和 CSS token 漂移。

```ts
import { fileUploadMotion } from '@nerv-iip/ui'

fileUploadMotion.fastInvoke
fileUploadMotion.pointToPointShort
```
