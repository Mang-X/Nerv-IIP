---
title: NvScreenPanel 面板
---

<script setup>
import { NvScreenPanel, NvOeeHero } from '@nerv-iip/ui'
</script>

# NvScreenPanel 面板

大屏的基础容器:半透明渐变底、发丝级描边、顶部一道微光与玻璃质斜向高光。其它所有大屏模块都坐落在它之上。可选 `title` 行带出一条标题,`accent` 顶边色条用作状态签名(青 / 绿 / 琥珀 / 红)。

## 基础用法

传 `title` 出一条标题行,默认插槽放真实内容 —— 这里嵌一个核心指标块。

<ScreenDemo>
  <NvScreenPanel title="焊接线 A · 设备综合效率" style="width: 420px">
    <NvOeeHero label="OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" />
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvScreenPanel title="焊接线 A · 设备综合效率">
  <NvOeeHero label="OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" />
</NvScreenPanel>
```

## 状态色条

`accent` 在顶边压一条发光色条,用来给整块面板一个状态签名 —— 运行绿、报警红。`extra` 插槽落在标题行右侧放辅助信息。

<ScreenDemo>
  <NvScreenPanel title="装配线 B · 实时产出" accent="green" style="width: 360px">
    <template #extra>WO-2406-0312</template>
    <div style="display:flex;flex-direction:column;gap:8px;font-size:14px">
      <div style="display:flex;justify-content:space-between"><span style="color:var(--nv-scr-muted)">当班计划</span><span>1 200 件</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--nv-scr-muted)">已完成</span><span>934 件</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--nv-scr-muted)">实时节拍</span><span>48.2 s / 件</span></div>
    </div>
  </NvScreenPanel>
  <NvScreenPanel title="CNC 线 C · 主轴温度" accent="red" style="width: 360px">
    <template #extra>已超阈值</template>
    <div style="display:flex;flex-direction:column;gap:8px;font-size:14px">
      <div style="display:flex;justify-content:space-between"><span style="color:var(--nv-scr-muted)">当前</span><span style="color:var(--nv-scr-red)">78.6 ℃</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--nv-scr-muted)">报警阈值</span><span>75.0 ℃</span></div>
      <div style="display:flex;justify-content:space-between"><span style="color:var(--nv-scr-muted)">持续</span><span>06 分 12 秒</span></div>
    </div>
  </NvScreenPanel>
</ScreenDemo>

```vue
<NvScreenPanel title="装配线 B · 实时产出" accent="green">
  <template #extra>WO-2406-0312</template>
  <!-- 产线明细 -->
</NvScreenPanel>
```

## 属性

| 属性     | 说明                        | 类型                                    | 默认 |
| -------- | --------------------------- | --------------------------------------- | ---- |
| `title`  | 标题行文本                  | `string`                                | —    |
| `accent` | 顶边状态色条                | `'cyan' \| 'green' \| 'amber' \| 'red'` | —    |
| `class`  | 透传到根 `<section>` 的类名 | `string`                                | —    |

## 插槽

| 插槽          | 说明                                     |
| ------------- | ---------------------------------------- |
| 默认          | 面板主体内容                             |
| `title-extra` | 紧跟标题文本之后的内联追加               |
| `extra`       | 标题行右侧的辅助信息(出现时才渲染标题行) |
