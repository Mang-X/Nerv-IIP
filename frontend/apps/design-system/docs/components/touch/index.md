---
title: 一体机 / 工位触控组件
---

# 一体机 / 工位触控组件

`@nerv-iip/ui` 的 `touch/` 层 —— 车间一体机与工位看板的**大触控**控件。为"戴手套、远一点、少点几下完成报工"而设计：触控目标 **56–72px**（远大于 PC 的 36–40px 与移动的 44px），动作语义强、按压反馈明显、操作路径最短。

与桌面共享同一套语义令牌（`--nv-*`）与亮暗两态，但尺寸、间距、字号全部按大触控放大。**不要**把工位大件直接用到手机（会"肿大"），也不要把 PC 紧凑件塞进一体机（点不准）。

- **操作**：[NvTouchButton 触控按钮](/components/touch/touch-button)、[NvTouchSegmented 分段切换](/components/touch/touch-segmented)、[NvQtyStepper 数量步进](/components/touch/qty-stepper)
- **信息**：[NvStatTile 指标块](/components/touch/stat-tile)、[NvStationBar 工位栏](/components/touch/station-bar)
- **复用 PC 件**：一体机布局大量复用桌面 `@nerv-iip/ui` 的 [NvCard](/components/desktop/card)、[图表](/components/desktop/chart)、`Progress` 等——它们在大屏距离下依旧可读，只需放大字号与间距。

> 想看这些组件如何组成一整块工位看板？查看 [工位看板完整示例](/components/board)（报工、节拍、队列的沉浸式实时演示）。
