# FileUpload

`FileUpload` 是 Calm Control Plane 的上传 primitive，用于 FileStorage 支持的业务附件、
质量证据、维护照片和工程文档。

> NvUI 状态：`FileUpload` 保持无前缀名称；它是 Nerv-IIP 自定义 primitive
> （不是 shadcn 原版透传），也是当前从 `@nerv-iip/ui` 导出的规范
> 应用侧名称；ADR 0020 Appendix A 未为其分配 `Nv*` 重命名。文件预览辅助项位于
> `@nerv-iip/ui/file-preview` 子入口（唯一允许的子入口）。

## 导出

- `FileUpload`
- `fileUploadMotion`
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

## 契约

1. 属性包括 `purpose`、`ownerService`、`ownerType`、`ownerId`、`organizationId`、`environmentId`、接受的内容类型、最大文件大小、最大文件数量、`autoUpload`、`virtualizeThreshold`、`virtualRowHeight` 和 `virtualListHeight`。
2. 组件仅发出已完成的 `fileId` 值；绝不暴露 bucket 名称、对象键或长期有效的对象存储 URL。
3. 默认原生传输支持 FileStorage `tus` `HEAD`/`PATCH` 和 `server-proxy` 二进制 `PUT` 指令。
4. 行展示状态、语义化状态徽标、进度、可重试的失败错误、上传期间的暂停/恢复控件、Word、Excel、PowerPoint、PDF、图像、音频和视频文件的可读文件类别标签，以及最大到 GB 的人类可读大小标签。
5. `autoUpload` 默认为 `true`；设为 `false` 时，选中或拖放的文件保持排队，直到通过暴露的组件 API 调用 `uploadQueued()`。
6. 暴露的命令式方法仅限上传工作流控制：`browse`、`addFiles`、`uploadQueued`、`pauseAll`、`resumeAll`、`retryFailed` 和 `clear`。
7. 拖放和浏览入口共用相同的验证和 FileStorage 会话流程。
8. 行进入/移除和拖放悬停反馈使用 Vue 过渡类和 Tailwind 语义化 tokens；当前 primitive 不需要 `motion-vue` 依赖。
9. 未来可用 Uppy 适配器替换传输，以支持更丰富的重试策略、来源提供方工作流或更广泛的 tus 协议覆盖范围，且不改变视觉契约。
10. 拒绝的大小/类型、过期会话、校验和不匹配和中断上传错误均作为行级状态呈现；重试已过期的失败会话时，会在传输启动前创建新的上传会话。
11. 被拒绝和失败的行保持可见，以便反馈或重试，但不占用可用上传槽位。
12. 大型队列在超过 `virtualizeThreshold` 行后，从带动画的完整渲染切换为固定高度的虚拟化滚动容器；小型队列保留行进入/移除过渡。

## 用法

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

表单提交流程的手动队列模式：

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
