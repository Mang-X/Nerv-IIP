---
title: Spinner 加载
---

<script setup>
import { NvButton, NvSpinner } from '@nerv-iip/ui'
</script>

# Spinner 加载

`NvSpinner` 用于控制台请求等待反馈，默认读屏名称为「加载中」。原版 `Spinner` 保留不变；控制台使用品牌件。

<Demo>
  <NvSpinner />
  <NvSpinner class="size-6 text-muted-foreground" />
  <NvButton disabled><NvSpinner aria-hidden="true" />保存中</NvButton>
</Demo>

```vue
<NvSpinner />
<NvSpinner class="size-6" />
<NvButton :disabled="pending">
  <NvSpinner v-if="pending" aria-hidden="true" />保存
</NvButton>
```

## Props 与属性

| 名称    | 类型                      | 默认值 | 说明                                      |
| ------- | ------------------------- | ------ | ----------------------------------------- |
| `class` | `HTMLAttributes['class']` | —      | 与 `size-4 animate-spin` 合并，可覆盖尺寸 |

根节点为加载图标，带 `role="status"` 与 `aria-label="加载中"`；其余属性透传。按钮已有等待文案时可用 `aria-hidden="true"` 避免重复读屏，`data-icon` 等现有消费属性保持可用。
