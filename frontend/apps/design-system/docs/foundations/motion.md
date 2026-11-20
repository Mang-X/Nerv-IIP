# 动效

动效服务于**反馈**与**连续性**，不喧宾夺主。统一的缓动令牌让全站节奏一致。

## 缓动与时长

- 通用进入或悬停用 `--nv-ease-out-quart`。滑移、吸附与抽屉用 `--nv-ease-out-expo`。
- 标签页指示器用 `--nv-ease-in-out-quart`，功能型弹层用 `--nv-ease-fast-invoke` 和 `--nv-duration-fast-invoke`。
- 淡入淡出用 `--nv-duration-fade`，常规状态过渡从 `--nv-duration-fast`、`--nv-duration-base` 和 `--nv-duration-slow` 中选择。

[theme.css](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/styles/theme.css) 定义 CSS 参数，[motion.ts](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui/src/lib/motion.ts) 提供 JavaScript 动效预设。需要大屏动效时用对应场景令牌。

## 拖拽手感：橡皮筋

移动端的侧滑、抽屉和下拉刷新使用共享的[边界阻尼函数](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/packages/ui-mobile/src/lib/utils.ts)。

- 范围内 **1:1 跟手**；
- 拖过边界时**阻尼回弹**（越拉阻力越大）；
- 松手按距离 / 速度决定吸附或弹回，过渡用 `ease-out-expo`。

应用于：移动端**侧滑操作**、**抽屉上拉**、**下拉刷新**。

## 微交互原则

- 根据组件已有变体提供即时按压反馈。触控列表优先用背景或透明度反馈。
- 退出动画要和进入同样讲究——避免"看起来没有动画"的突兀消失。
- 覆盖层用半透明 + `backdrop-blur` 的玻璃质感，克制不过分。
- 使用 `prefers-reduced-motion` 降级位移和关键帧动效，同时保留操作结果反馈。

## 交互状态

- 为交互组件核对默认、悬停、键盘焦点、按压、禁用、加载和选中等适用状态。触控区保证按压反馈；横滑控件保留纵向滚动。
- 步骤条和时间线的节点应遮住连接线，进度由轨道和填充共同表达。移动列表分隔线缩进并省略最后一行下方的分隔线。
- 搜索栏或标签指示器的位置变化要连续。减少动效开启时，内容和焦点仍应正确切换。
