# FileUpload

`FileUpload` is the Calm Control Plane upload primitive for FileStorage-backed business attachments, quality evidence, maintenance photos and engineering documents.

## Exports

- `FileUpload`
- `uploadWithNativeFileStorageTransport`
- `useFileUpload`
- `FileUploadCreateSessionRequest`
- `FileUploadCompleteSessionRequest`
- `FileUploadSession`
- `FileUploadRow`
- `FileUploadTransport`
- `FileUploadTransportContext`
- `FileUploadCompletedFile`
- `FileUploadRejectedFile`

## Contract

1. Props include `purpose`, `ownerService`, `ownerType`, `ownerId`, `organizationId`, `environmentId`, accepted content types, max file size and max file count.
2. The component emits completed `fileId` values only; it never exposes bucket names, object keys or long-lived object-storage URLs.
3. The default native transport supports FileStorage `tus` `HEAD`/`PATCH` and `server-proxy` binary `PUT` instructions.
4. A future Uppy adapter may replace the transport for richer retry, pause/resume and source-provider workflows without changing the visual contract.
5. Rejected size/type, expired session, checksum mismatch and interrupted upload errors are surfaced as row-level status.

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
