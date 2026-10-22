# Business Architecture Agent 路由

- 先读本目录 [`README.md`](README.md)，再只加载与任务直接相关的 1–2 个 current architecture 页面。
- 不因“业务任务”默认加载整个 `docs/architecture/business/`，也不把 Product/Reference/Governance/Runbook/Report 当作架构正文。
- 领域 owner/依赖变化先核对 `domain-architecture.md`；MasterData、现场 scope、Scheduling retention、设备事件、ERP/MES/WMS/Planning 等问题分别读取对应专题页。
- 精确 endpoint、字段、migration、配置、完成状态最终回到代码、契约、迁移、测试和 tracker；Architecture 不维护第二份实现总账。
