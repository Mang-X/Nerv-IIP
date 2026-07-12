# FilePreview 文件预览

纯前端文件预览容器。组件只接收 `src / fileName / contentType / sizeBytes`，不绑定 FileStorage、API Client 或业务附件模型。上层负责把任意文件系统、对象存储或业务接口转换成可访问的 `src`。

PDF、Word、PowerPoint 和 Excel 预览都在统一工具栏内提供快速定位下拉：PDF / Word 可选择页码，PowerPoint 可选择幻灯片，Excel 可选择工作表。

<script setup>
import { FilePreview } from '@nerv-iip/ui/file-preview'

const samples = {
  image: '/file-preview/quality-evidence.svg',
  pdf: '/file-preview/quality-evidence-report.pdf',
  docx: '/file-preview/quality-work-instruction.docx',
  xlsx: '/file-preview/inspection-metrics.xlsx',
  pptx: '/file-preview/preview-review-deck.pptx',
}
</script>

## 示例

### 图片预览

<Demo title="图片预览" block>
  <FilePreview
    :src="samples.image"
    file-name="quality-evidence.svg"
    content-type="image/svg+xml"
    :size-bytes="734003"
    :height="430"
  />
</Demo>

示例图片是文档用样张，不作为色板或业务图形规范。真实业务侧只需要传入浏览器可访问的 `src`。

```vue
<script setup lang="ts">
import { FilePreview } from '@nerv-iip/ui/file-preview'
</script>

<template>
  <FilePreview
    src="/file-preview/quality-evidence.svg"
    file-name="quality-evidence.svg"
    content-type="image/svg+xml"
    :size-bytes="734003"
    :height="430"
  />
</template>
```

### PDF 预览

<Demo title="PDF 预览" block>
  <FilePreview
    :src="samples.pdf"
    file-name="quality-evidence-report.pdf"
    content-type="application/pdf"
    :size-bytes="1129"
    :height="430"
  />
</Demo>

```vue
<script setup lang="ts">
import { FilePreview } from '@nerv-iip/ui/file-preview'
</script>

<template>
  <FilePreview
    src="/file-preview/quality-evidence-report.pdf"
    file-name="quality-evidence-report.pdf"
    content-type="application/pdf"
    :size-bytes="1129"
    :height="430"
  />
</template>
```

### Word / DOCX 预览

<Demo title="Word / DOCX 预览" block>
  <FilePreview
    :src="samples.docx"
    file-name="quality-work-instruction.docx"
    content-type="application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    :size-bytes="3062"
    :height="430"
  />
</Demo>

```vue
<script setup lang="ts">
import { FilePreview } from '@nerv-iip/ui/file-preview'
</script>

<template>
  <FilePreview
    src="/file-preview/quality-work-instruction.docx"
    file-name="quality-work-instruction.docx"
    content-type="application/vnd.openxmlformats-officedocument.wordprocessingml.document"
    :size-bytes="3062"
    :height="430"
  />
</template>
```

### Excel / XLSX 预览

Excel 示例使用一个包含 `Inspection / Trend / Summary` 3 个工作表的工作簿，用于验证表格预览的工作表切换体验。

<Demo title="Excel / XLSX 预览" block>
  <FilePreview
    :src="samples.xlsx"
    file-name="inspection-metrics.xlsx"
    content-type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    :size-bytes="6424"
    :height="430"
  />
</Demo>

```vue
<script setup lang="ts">
import { FilePreview } from '@nerv-iip/ui/file-preview'
</script>

<template>
  <FilePreview
    src="/file-preview/inspection-metrics.xlsx"
    file-name="inspection-metrics.xlsx"
    content-type="application/vnd.openxmlformats-officedocument.spreadsheetml.sheet"
    :size-bytes="6424"
    :height="430"
  />
</template>
```

### PowerPoint / PPTX 预览

<Demo title="PowerPoint / PPTX 预览" block>
  <FilePreview
    :src="samples.pptx"
    file-name="preview-review-deck.pptx"
    content-type="application/vnd.openxmlformats-officedocument.presentationml.presentation"
    :size-bytes="4353"
    :height="430"
  />
</Demo>

```vue
<script setup lang="ts">
import { FilePreview } from '@nerv-iip/ui/file-preview'
</script>

<template>
  <FilePreview
    src="/file-preview/preview-review-deck.pptx"
    file-name="preview-review-deck.pptx"
    content-type="application/vnd.openxmlformats-officedocument.presentationml.presentation"
    :size-bytes="4353"
    :height="430"
  />
</template>
```

### 状态示例

<Demo title="状态示例" block>
  <div class="grid gap-4 lg:grid-cols-3">
    <FilePreview
      file-name="待上传预览源.pdf"
      content-type="application/pdf"
      :height="260"
      :show-header="false"
    />
    <FilePreview
      :src="samples.image"
      file-name="图片加载失败.png"
      content-type="image/png"
      error="图片加载失败，请检查文件地址或访问权限。"
      :height="260"
      :show-header="false"
    />
    <FilePreview
      :src="samples.pdf"
      file-name="不支持的图纸格式.dwg"
      content-type="application/acad"
      :height="260"
      :show-header="false"
    />
  </div>
</Demo>

```vue
<script setup lang="ts">
import { FilePreview } from '@nerv-iip/ui/file-preview'
</script>

<template>
  <div class="grid gap-4 lg:grid-cols-3">
    <FilePreview
      file-name="待上传预览源.pdf"
      content-type="application/pdf"
      :height="260"
      :show-header="false"
    />
    <FilePreview
      src="/file-preview/quality-evidence.svg"
      file-name="图片加载失败.png"
      content-type="image/png"
      error="图片加载失败，请检查文件地址或访问权限。"
      :height="260"
      :show-header="false"
    />
    <FilePreview
      src="/file-preview/quality-evidence-report.pdf"
      file-name="不支持的图纸格式.dwg"
      content-type="application/acad"
      :height="260"
      :show-header="false"
    />
  </div>
</template>
```

## 支持格式

| 类型                                      | 渲染器                                             |
| ----------------------------------------- | -------------------------------------------------- |
| PDF                                       | `@embedpdf/core` headless 插件组合，自研统一工具栏 |
| DOCX / XLSX / PPTX                        | `@silurus/ooxml`                                   |
| PNG / JPG / WEBP / SVG / GIF / AVIF / BMP | 自研图片预览                                       |

## 接口边界

| 属性 / 事件       | 说明                                                   |
| ----------------- | ------------------------------------------------------ |
| `src`             | 浏览器可访问的文件地址或 data URL                      |
| `fileName`        | 用于类型推断、标题和可访问标签                         |
| `contentType`     | 优先参与类型推断                                       |
| `sizeBytes`       | 展示紧凑文件大小                                       |
| `height`          | 预览容器高度，默认 `520`                               |
| `loading / error` | 由上层控制的异步状态                                   |
| `ready(kind)`     | 当前预览器可用时触发                                   |
| `error(message)`  | 子预览器渲染失败时触发                                 |
| `openSource(src)` | 用户点击打开源文件时触发；组件不直接调用下载或业务接口 |

`src` 为空时组件显示独立空状态，不把缺少来源误判为不支持格式。图片预览会在原生 `load` 后触发 `ready`，在原生 `error` 后进入统一错误状态；PDF 预览使用 EmbedPDF 的 document / viewport / scroll / render / zoom 插件，文档加载完成后触发 `ready`，加载失败后进入统一错误状态；Office 预览在对应 OOXML viewer 完成 `load` 后触发 `ready`。

PDF 预览依赖 `@embedpdf/engines` 的 wasm 资源。消费 `FilePreview` PDF 路径的 Vite 应用需要启用 `vite-plugin-wasm`；design-system 文档和 `@nerv-iip/ui` 测试配置已经包含该插件。普通 UI primitives 导入不会主动加载 PDF / OOXML 渲染器，`FilePreview` 通过稳定子入口 `@nerv-iip/ui/file-preview` 暴露。

## 动效

本组件首次接入 `motion-v`。`filePreviewMotion` 采用 Windows Animation Values：功能切换使用 Fast Invoke，图片进入与预览体切换保持直接、短促；透明度使用 83ms linear。组件外层使用 `MotionConfig reduced-motion="user"`，遵从系统减少动态效果设置。

```ts
import { filePreviewMotion } from '@nerv-iip/ui'

filePreviewMotion.fastInvoke
filePreviewMotion.fade
```
