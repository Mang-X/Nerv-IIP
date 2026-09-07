# 前端导航产品 IA

本文只描述 Nerv-IIP 跨端导航的**产品语义与信息架构**：用户从哪里进入、一级能力区如何组织、对象如何被搜索与穿透，以及 PC / PDA 应采用什么任务范式。当前路由、权限码、facade 和页面是否已经存在，不在本文维护。

## 产品原则

1. PC 端采用顶部一级能力区 + 左侧域内导航的 T 型结构。顶部只表达经过权限裁剪的能力区，左侧只显示当前能力区内的模块与页面。
2. 普通用户不应看到完整平台能力树；导航按角色、组织/环境上下文、权限与 feature flag 裁剪。
3. 跨域用户优先依赖“近期使用”“星标收藏”和全局搜索，而不是暴露完整菜单作为唯一入口。
4. 页面内动作（创建、编辑、审批、确认、打印、报工、入库、排程运行等）不是菜单项；对象详情页与页内 Tabs 也不进入菜单树。
5. 后端 bounded context 不是前端用户操作边界。页面可围绕真实任务聚合多个域的事实，但不得因此复制服务内部结构到导航。
6. WMS 与 Inventory 在用户心智上应融合：仓储作业页就地展示物料、库位、批次、可用量、冻结与预留；Inventory 保留台账、余额、批次与分析等事实视角。
7. PDA / mobile 是独立任务范式，不复用 PC 的复杂菜单树。首页优先“我的任务”、快捷入口和扫码直达，扫码结果直接进入报工、收货、拣货、盘点、巡检、报修等任务。
8. 页面必须支持上下文穿透：关联对象通过链接、Drawer、Sheet、关联侧栏或对象详情进入，避免回主菜单手工切域。

## 能力区与角色入口

业务 Console 的长期能力区按用户任务组织，可包含基础数据、产品工程、需求与计划、制造执行、质量、库存、仓储、经营管理、设备、审批、条码等。**具体当前可见项不在本文列举**；以代码生产者为准，见 [`../reference/frontend/navigation-map.md`](../reference/frontend/navigation-map.md)。

平台 Console 只承载 IAM、AppHub、Ops、FileStorage、Notification 等通用控制面；业务 CRUD 与业务工作流进入 Business Console / PDA。

## 全局搜索与对象穿透

长期导航能力应支持按菜单名和业务对象进入目标页面或详情，包括物料、工单、采购单、销售单、设备、批次等。搜索是导航能力，不替代后端授权；对象是否可见和可操作仍以 Gateway 每请求授权为准。

## 与实现文档的关系

- 当前壳层与导航组件边界：[`../architecture/frontend/navigation.md`](../architecture/frontend/navigation.md)
- 当前工程约束：[`../governance/frontend/navigation.md`](../governance/frontend/navigation.md)
- 当前 route/page/facade/permission 事实入口：[`../reference/frontend/navigation-map.md`](../reference/frontend/navigation-map.md)
- M2-N 前的混合正文冻结快照：[`../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md`](../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md)

## 更新纪律

产品 IA 变化时更新本文；页面是否“已落地”、当前端口、route 数量、facade 覆盖、阶段完成度与 CI 结果不得写回本文。实现状态直接回到代码、GitHub/Linear 与相应 Reference producer。