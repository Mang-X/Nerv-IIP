# 前端导航产品信息架构

本文定义 Nerv-IIP 跨端导航的**当前产品语义**：不同角色从哪里进入、如何在能力域之间切换、如何找到业务对象，以及 PC 与 PDA 各自采用什么任务范式。它不记录页面是否已交付、Gateway 覆盖率、固定端口、Issue/Phase 或 CI 状态。

实现清单与当前代码入口见 [`../reference/frontend/navigation-map.md`](../reference/frontend/navigation-map.md)；当前工程约束见 [`../governance/frontend/navigation.md`](../governance/frontend/navigation.md)；壳层与应用边界见 [`../architecture/frontend/navigation.md`](../architecture/frontend/navigation.md)。

## 1. 三类入口

### Platform Console

面向平台管理员和运维角色，承载 IAM、应用/实例、运维、通知、文件与平台监控等通用控制面。业务域 CRUD 与业务工作流不进入 Platform Console。

### Business Console

面向计划、生产、仓储、质量、设备、经营等业务角色，是桌面端业务操作入口。导航按“能力域 → 模块/任务 → 页面”组织，但用户看到的集合必须依据角色和权限裁剪，而不是把完整能力目录作为默认侧栏。

### Business PDA

面向一线执行角色，采用任务优先而不是 PC 菜单树优先的范式。首页优先呈现我的任务、快捷入口和扫码；扫码或业务对象识别后直接进入报工、收货、上架、拣货、盘点、巡检、报修等对应工作流。

## 2. 桌面端信息架构

1. 长期桌面导航采用顶部能力域 + 左侧域内导航的 T 型结构。
2. 逻辑层级最多三级：**域 → 模块/任务组 → 页面**。更深的对象关系进入页面内树、Tab、Drawer、Sheet 或详情，而不是继续扩菜单树。
3. “创建、编辑、审批、确认、关闭、打印、报工、入库、排程运行”等动作属于页面操作，不是菜单项。
4. 物料、工单、订单、设备、批次等对象详情不是常驻菜单项，应从列表、搜索或上下文关联进入。
5. 页面内 Tabs 表达同一对象的不同视图，不进入全局导航树。
6. 能力目录描述产品边界，不等于任何具体角色的默认可见菜单。

## 3. 角色与发现能力

- 一线和专岗角色默认只看到与当前任务直接相关的工作台和少量入口。
- 厂长、生产负责人、超级管理员等跨域角色需要“近期使用”“星标收藏”和全局发现能力，不能依赖展开完整长菜单完成跨域工作。
- 全局命令/对象搜索应支持按菜单名以及物料、工单、采购单、销售单、设备、批次等业务对象进入相应页面或详情。
- 搜索负责“找到入口/对象”，不绕过对象本身的权限和范围约束。

## 4. 跨域上下文穿透

后端 bounded context 不是前端用户的操作边界。一个任务可以组合多个域的事实，但应保持事实来源清晰：

- WMS 作业需要就地看到物料、库位、批次、可用量、冻结和预留等库存事实；Inventory 同时保留余额、台账、批次与分析等事实视角。
- 生产、质量、设备、维护之间的关联通过链接、Drawer、Sheet、关联侧栏或对象详情穿透，避免用户反复回到主菜单手工切域。
- 跨域页面以用户任务为中心聚合信息，不为“后端有一个服务”机械新增导航入口。

## 5. 导航升级的产品判据

一个能力只有在能够形成真实、可操作、可授权的用户入口时才进入可见导航。后端存在、接口存在或设计文档存在都不等于菜单已交付。具体 route、权限和 facade 是否存在，以当前生产者为准，见 [`../reference/frontend/navigation-map.md`](../reference/frontend/navigation-map.md)。

## 6. 文档职责

- 本文只回答“用户应该怎样找到并进入能力”。
- 当前菜单/route/permission/facade 的可复核位置由 Reference 路由到代码，不在本文手抄。
- BusinessGateway、RBAC、菜单深度、壳层复用等工程规则由 Governance 管理。
- AppShell 与 Console / Business Console / PDA 的当前组件边界由 Architecture 管理。
- 迁移前同时混合产品、现态、状态与历史的旧正文已冻结到 [`../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md`](../reports/audits/frontend-navigation-map-pre-m2-n-2026-09-07.md)，只作为时点证据，不再是现态入口。
