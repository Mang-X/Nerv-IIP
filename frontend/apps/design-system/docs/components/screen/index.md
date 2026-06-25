---
title: 大屏 / 控制室
---

<script setup>
import { OeeHero, ScreenPanel, RingGauge, StatusTag, StatusLight } from '@nerv-iip/ui'
</script>

# 大屏 / 控制室组件

面向中央控制室、车间指挥大屏(LED / 拼接屏)的组件层。与桌面 PC、PDA 移动两层**完全解耦**:独立的 `--sb-*` 深色工业蓝令牌(无亮色模式),只遵循统一的设计哲学 —— 克制、去装饰、动效减速无回弹。

<ScreenDemo>
  <ScreenPanel style="width: 360px">
    <OeeHero label="设备综合效率 OEE" :value="92.4" unit="%" delta="较昨日 +2.7%" />
  </ScreenPanel>
  <RingGauge :value="78" label="稼动率" />
  <div style="display:flex;flex-direction:column;gap:12px">
    <StatusTag tone="run">运行中</StatusTag>
    <StatusTag tone="idle">待机</StatusTag>
    <StatusTag tone="alarm">报警</StatusTag>
  </div>
</ScreenDemo>

## 为什么独立一层

大屏的观看距离(数米外)、环境(暗光车间)、硬件(高亮 LED)都和近场的 PC / PDA 不同。强行复用浅色令牌会过曝、对比不足。因此这一层:

- **固定深色**:深藏青底 + 青色主辉光,不跟随亮/暗切换;
- **更大的字号与发光**:远距离可读,关键数字带 `text-shadow` 辉光;
- **独立令牌前缀 `--sb-*`**:`--sb-bg / --sb-cyan / --sb-green / --sb-amber / --sb-red / --sb-panel-* …`,与共享层的 `--*` 不冲突;
- **数据驱动**:每个组件零 props 即可渲染示例,接真实数据只需传值。

## 组件分类

| 分类 | 组件 |
|---|---|
| 容器 / 外壳 | `ScreenPanel` · `BorderPanel` · `TechFrame` · `TitleBar` · `ScreenHeader` · `GlowDivider` |
| 指标 / 图表 | `OeeHero` · `RingGauge` · `CapsuleBar` · `DigitalFlop` · `Sparkline` · `TrendChart` · `TaktGantt` |
| 数据 / 状态 | `StatusCard` · `KpiBar` · `AlarmTable` · `StatusLight` · `StatusTag` |
| 控件(大屏化) | `ScreenButton` · `ScreenInput` · `ScreenSelect` · `ScreenSearch` · `ScreenTable` · `ScreenTabs` · `ScreenSegmented` · `ScreenSwitch` |

从左侧目录进入任一组件查看用法与属性。
