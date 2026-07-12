# 内部缺口记录：核心业务流程图

该页面是内部缺口记录，不作为官网 Docs 对外营销文案。

## 证据页面

- `/processes/`
- `docs/architecture/frontend-structure.md`
- `docs/architecture/implementation-readiness.md`

## 建议 issue 标题

- `[Docs] 核心流程图拆分为可点击页面入口和能力状态矩阵`

## 缺口记录

### 能力缺失

- 流程图是 Mermaid 静态图，节点下方虽有 Business Console 路由和 BusinessGateway facade 映射，但图本身不能直接跳转到页面，也不能显示权限状态。

### 操作不连贯

- 用户需要在六张流程图、节点映射表、路径页和 Business Console 页面之间手动对照对象流。

### 手填 ID

- 流程图没有承载业务对象号或单据号示例，也不能从对象号定位到对应页面。

### 术语不清

- 质量审批、设备维护和条码追溯流程覆盖的是当前已暴露链路，完整质量处置工作台、跨域工作流、CMMS 体验、移动扫码解释和跨域追溯图谱仍需要后续产品化，页面需要继续避免把目标链路写成已完整交付。

### 反馈不足

- 后续可从路由表和 readiness 事实生成页面入口矩阵，减少文档漂移，并在构建时提示入口缺失。
