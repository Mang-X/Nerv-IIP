# FileUpload

`FileUpload` is the Calm Control Plane upload primitive for FileStorage-backed business attachments, quality evidence, maintenance photos and engineering documents.

## Exports

- `FileUpload`
- `uploadWithNativeFileStorageTransport`
- `useFileUpload`
- `FileUploadCreateSessionRequest`
- `FileUploadCompleteSessionRequest`
- `FileUploadExpose`
- `FileUploadMode`
- `FileUploadProvider`
- `FileUploadSession`
- `FileUploadRow`
- `FileUploadTransport`
- `FileUploadTransportContext`
- `FileUploadCompletedFile`
- `FileUploadRejectedFile`

## Contract

1. Props include `purpose`, `ownerService`, `ownerType`, `ownerId`, `organizationId`, `environmentId`, accepted content types, max file size, max file count, `autoUpload`, `virtualizeThreshold`, `virtualRowHeight` and `virtualListHeight`.
2. The component emits completed `fileId` values only; it never exposes bucket names, object keys or long-lived object-storage URLs.
3. The default native transport supports FileStorage `tus` `HEAD`/`PATCH` and `server-proxy` binary `PUT` instructions.
4. Rows show status, progress, retryable failure errors, pause/resume controls while uploading and readable file-family labels for Word, Excel, PowerPoint, PDF, image, audio and video files.
5. `autoUpload` defaults to `true`; when set to `false`, selected or dropped files remain queued until `uploadQueued()` is called through the exposed component API.
6. Exposed imperative methods are limited to upload workflow control: `browse`, `addFiles`, `uploadQueued`, `pauseAll`, `resumeAll`, `retryFailed` and `clear`.
7. Drag-and-drop and browse entry points share the same validation and FileStorage session flow.
8. Row entry/removal and drag-over feedback use Vue transition classes and Tailwind semantic tokens; no `motion-vue` dependency is required for the current primitive.
9. A future Uppy adapter may replace the transport for richer retry policy, source-provider workflows or broader tus protocol coverage without changing the visual contract.
10. Rejected size/type, expired session, checksum mismatch and interrupted upload errors are surfaced as row-level status.
11. Rejected and failed rows remain visible for feedback or retry, but they do not consume available upload slots.
12. Large queues switch from animated full rendering to a fixed-height virtualized scroll container after `virtualizeThreshold` rows; small queues keep row entry/removal transitions.

## Usage

```vue
<FileUpload
  purpose="quality-evidence"
  owner-service="Quality"
  owner-type="InspectionRecord"
  owner-id="inspection_1"
  organization-id="org_1"
  environment-id="env_1"
  :accepted-content-types="['image/*', 'application/pdf']"
  :create-upload-session="createUploadSession"
  :complete-upload-session="completeUploadSession"
  @completed="handleCompletedFiles"
/>
```

Manual queue mode for form submission flows:

```vue
<script setup lang="ts">
import type { FileUploadExpose } from '@nerv-iip/ui'
import { useTemplateRef } from 'vue'

const uploadRef = useTemplateRef<FileUploadExpose>('upload')

async function submitForm() {
  await uploadRef.value?.uploadQueued()
}
</script>

<template>
  <FileUpload
    ref="upload"
    purpose="quality-evidence"
    owner-service="Quality"
    owner-type="InspectionRecord"
    owner-id="inspection_1"
    organization-id="org_1"
    environment-id="env_1"
    :auto-upload="false"
    :create-upload-session="createUploadSession"
    :complete-upload-session="completeUploadSession"
  />
</template>
```
