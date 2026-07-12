# 介绍

**Nerv-IIP 设计系统**是数字工厂控制平面的统一设计语言与组件库。它把"为什么这样设计"（[设计哲学](/foundations/philosophy)）、"用什么设计"（[设计令牌](/foundations/tokens)）和"有哪些组件"（[组件](/components/overview)）收敛到一处。

## 技术栈

- **Vue 3.5** `<script setup>` + TypeScript
- **Tailwind CSS v4**（`@theme inline`、OKLCH 令牌）
- 上游原语仅作为内部基座，应用与文档示例统一使用 `@nerv-iip/ui`（Pro）与 `@nerv-iip/ui-mobile`
- 本文档站基于 **VitePress**，外壳已用本设计系统的令牌整体重塑

## 两个包

| 包                    | 用途                                                               |
| --------------------- | ------------------------------------------------------------------ |
| `@nerv-iip/ui`        | 桌面 / 一体机的 Pro 组件、图表、设计令牌（`theme.css` 是唯一真源） |
| `@nerv-iip/ui-mobile` | 移动 PDA 原生质感控件、手势、安全区基线                            |

> **使用边界**：永远从包的稳定导出引用组件，不要从上游原始包或组件深层路径导入。详见[设计哲学 §3](/foundations/philosophy#_3-原版零改动-定制靠复制重建)。

## 如何阅读

1. 先读 [设计哲学](/foundations/philosophy) —— 理解取舍的优先级。
2. 再看 [设计令牌](/foundations/tokens) 与 [色彩](/foundations/color) —— 视觉的原子。
3. 然后按表面浏览 [组件](/components/overview)。
