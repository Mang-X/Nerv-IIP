---
title: BorderPanel 描边面板
---

<script setup>
import { NvBorderPanel } from '@nerv-iip/ui'
</script>

# BorderPanel 描边面板

带装饰描边的容器:四角各一道发光短角线,顶边正中一个亮色缺口。克制胜于霓虹 —— 角标都是单层短线。给一块内容一点「仪表盘外壳」的分量,又不喧宾夺主。可选 `title` 与顶边缺口同处一行。

## 基础用法

传 `title` 在顶边缺口处带出居中标题,默认插槽放内容。

<ScreenDemo>
  <NvBorderPanel title="焊接线 A · 班次概览" style="width: 380px">
    <div style="display:flex;flex-direction:column;gap:8px;font-size:14px">
      <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">当前工单</span><span>WO-2406-0312</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">稼动率</span><span style="color:var(--sb-cyan)">86.5%</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">节拍</span><span>42.0 s / 件</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">良率</span><span>99.2%</span></div>
    </div>
  </NvBorderPanel>
</ScreenDemo>

```vue
<NvBorderPanel title="焊接线 A · 班次概览">
  <!-- 产线明细 -->
</NvBorderPanel>
```

## 无标题

不传 `title` 时只剩纯描边外壳,角标与顶边缺口仍在,适合包一段不需要抬头的内容。

<ScreenDemo>
  <NvBorderPanel style="width: 320px">
    <div style="text-align:center">
      <div style="font-size:13px;color:var(--sb-muted)">CNC 线 C · 在制数量</div>
      <div style="font-size:40px;font-weight:600;color:var(--sb-cyan);margin-top:6px">128</div>
      <div style="font-size:12px;color:var(--sb-muted);margin-top:4px">截至 2024-06-12 10:24</div>
    </div>
  </NvBorderPanel>
</ScreenDemo>

```vue
<NvBorderPanel>
  <!-- 居中大数字 -->
</NvBorderPanel>
```

## 属性

| 属性    | 说明         | 类型     | 默认 |
| ------- | ------------ | -------- | ---- |
| `title` | 顶边居中标题 | `string` | —    |

## 插槽

| 插槽    | 说明                            |
| ------- | ------------------------------- |
| 默认    | 面板主体内容                    |
| `title` | 自定义标题区(覆盖 `title` 属性) |
