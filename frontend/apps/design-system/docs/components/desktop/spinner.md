---
title: Spinner 加载
---

<script setup>
import { NvButton, NvSpinner } from '@nerv-iip/ui'
</script>

<!-- 决策正文嵌入 DESIGN 唯一事实源，修改使用规则请改源文件。 -->
<!--@include: ../../../../../DESIGN/components/skeleton-spinner.md-->

## 实时演示

<Demo>
  <NvSpinner />
  <NvSpinner class="size-6 text-muted-foreground" />
  <NvButton loading disabled>保存中</NvButton>
</Demo>

## Props 与属性

| 名称    | 类型                      | 默认值 | 说明                                      |
| ------- | ------------------------- | ------ | ----------------------------------------- |
| `class` | `HTMLAttributes['class']` | —      | 与 `size-4 animate-spin` 合并，可覆盖尺寸 |

根节点为加载图标，带 `role="status"` 与 `aria-label="加载中"`；其余属性透传。行内指示器已有等待文案时可用 `aria-hidden="true"` 避免重复读屏，`data-icon` 等现有消费属性保持可用。
