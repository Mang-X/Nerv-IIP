# 色彩与动态色

默认使用中性主题，主色随亮暗模式变化。彩色主题通过 `@nerv-iip/ui` 的预设同时更新主操作色、强调色和相关前景色。右上角的主题选择器与产品使用同一套预设。

<script setup>
import { THEME_PRESETS } from '@nerv-iip/ui'
</script>

<div class="my-6 flex flex-wrap gap-2">
  <span
    v-for="(preset, name) in THEME_PRESETS"
    :key="name"
    class="size-9 rounded-full border border-border"
    :style="{ backgroundColor: preset.primary }"
    :title="name"
    :aria-label="name"
    role="img"
  ></span>
</div>

## 使用语义色

- 用 `--background`、`--card`、`--popover` 和 `--muted` 区分页面、面板、浮层与次要表面。
- `--primary` 表达主题主色，`--nv-brand` 用于强调、选中和桌面页面级主操作。桌面页面或工具栏只设一个主操作。
- 成功、警告和危险色独立于主题。业务状态优先用 [NvStatusBadge 或 NvStatusDot](/components/desktop/status)，一并提供名称或图标。正文对比度至少为 4.5:1。
- 大屏采用固定深色场景。色彩语义见[大屏概览](/components/screen/)和[设计令牌](/foundations/tokens)。

具体属性取值由主题样式提供，彩色预设由 [useTheme.ts](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/composables/useTheme.ts) 提供。
