---
title: NvTechFrame 科技边框
---

<script setup>
import { NvTechFrame } from '@nerv-iip/ui'
</script>

# NvTechFrame 科技边框

全包围的科技边框:一圈微微发光的细边,四角各一个极简 L 形折角。「现代感 vs 廉价感」的分界就在这里 —— 短折角、不堆叠霓虹。`accent` 给边与折角定色,默认是实时数据的青色。它把内容整块框起来,折角画在内容之上。

## 基础用法

边框无内置背景,直接包住一块自带尺寸的内容。默认青色。

<ScreenDemo>
  <NvTechFrame style="width: 380px">
    <div style="padding:20px">
      <div style="font-size:14px;color:var(--sb-text-2);margin-bottom:10px">焊接线 A · 实时状态</div>
      <div style="display:flex;flex-direction:column;gap:8px;font-size:14px">
        <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">工单</span><span>WO-2406-0312</span></div>
        <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">OEE</span><span style="color:var(--sb-cyan)">92.4%</span></div>
        <div style="display:flex;justify-content:space-between"><span style="color:var(--sb-muted)">节拍</span><span>48.2 s / 件</span></div>
      </div>
    </div>
  </NvTechFrame>
</ScreenDemo>

```vue
<NvTechFrame>
  <!-- 自带尺寸的内容 -->
</NvTechFrame>
```

## 状态配色

`accent` 同时着色边与折角,用来表达整框状态 —— 运行绿、报警红。

<ScreenDemo>
  <NvTechFrame accent="green" style="width: 300px">
    <div style="padding:18px;text-align:center">
      <div style="font-size:13px;color:var(--sb-muted)">装配线 B</div>
      <div style="font-size:18px;font-weight:600;color:var(--sb-green);margin-top:6px">运行中</div>
      <div style="font-size:12px;color:var(--sb-muted);margin-top:4px">已稳产 4 时 12 分</div>
    </div>
  </NvTechFrame>
  <NvTechFrame accent="red" style="width: 300px">
    <div style="padding:18px;text-align:center">
      <div style="font-size:13px;color:var(--sb-muted)">CNC 线 C</div>
      <div style="font-size:18px;font-weight:600;color:var(--sb-red);margin-top:6px">主轴超温</div>
      <div style="font-size:12px;color:var(--sb-muted);margin-top:4px">78.6 ℃ · 阈值 75.0 ℃</div>
    </div>
  </NvTechFrame>
</ScreenDemo>

```vue
<NvTechFrame accent="green"><!-- 运行中 --></NvTechFrame>
<NvTechFrame accent="red"><!-- 主轴超温 --></NvTechFrame>
```

## 属性

| 属性     | 说明         | 类型                                    | 默认     |
| -------- | ------------ | --------------------------------------- | -------- |
| `accent` | 边与折角配色 | `'cyan' \| 'green' \| 'amber' \| 'red'` | `'cyan'` |

## 插槽

| 插槽 | 说明                           |
| ---- | ------------------------------ |
| 默认 | 被框住的内容(边框绘制在其之上) |
