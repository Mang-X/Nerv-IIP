# 使用指南

NvUI 是 Nerv-IIP 的设计系统。这里统一查阅视觉规范、令牌、交互模式和可运行的组件示例。

## 从场景开始

| 场景 | 设计与组件 |
| --- | --- |
| 桌面 PC | [排版与布局](/foundations/desktop-layout) · [桌面组件](/components/desktop/) · [完整示例](/components/desktop/gallery) |
| 移动 PDA | [移动组件](/components/mobile/) · [完整示例](/components/mobile/gallery) |
| 车间一体机 | [工位组件](/components/touch/) · [工位看板](/components/board) |
| 挂墙大屏 | [大屏组件](/components/screen/) · [完整示例](/components/screen/gallery) |

## 设计与实现

1. 阅读[设计哲学](/foundations/philosophy)和[界面文案](/guide/writing)，确认页面要支持的任务。
2. 按场景选择[设计令牌](/foundations/tokens)，再用[色彩](/foundations/color)和[动效](/foundations/motion)检查各状态。
3. 从[交互模式](/patterns/interaction-patterns)选择表单、列表、反馈或确认流程。更多页型和区块见[模式索引](/patterns/overview)。
4. 在相应组件页查看真实示例和属性。优先组合现有组件，跨包使用公共导出。命名、导入、冻结源码和新增组件要求见下方工程约束。

## 工程约束

- [NvUI 工程治理](https://github.com/Mang-X/Nerv-IIP/blob/main/frontend/DESIGN/governance.md)
- [前端设计系统治理](https://github.com/Mang-X/Nerv-IIP/blob/main/docs/governance/frontend/design-system.md)
- [ADR 0020 · 命名、令牌命名空间与样式隔离](https://github.com/Mang-X/Nerv-IIP/blob/main/docs/adr/0020-nvui-naming-token-namespaces-and-style-isolation.md)
