---
layout: home

hero:
  name: Nerv-IIP 产品文档
  text: 从资料、计划到执行的业务平台上手指南
  tagline: 按角色找到你的第一周路径，用端到端教程走通主线，用流程图理解单据、状态和限制。
  actions:
    - theme: brand
      text: 按角色入门
      link: /roles/
    - theme: alt
      text: 基础资料到工程资料
      link: /getting-started/engineering-to-production
    - theme: alt
      text: 查看流程图
      link: /processes/
features:
  - title: 按角色组织
    details: 计划员、班组长、仓管员、质检员、设备工程师、采购与财务，每个角色一张"第一周要会的路径"清单，标注当前可用性。
  - title: 10 分钟理解主线
    details: 三条端到端教程：先看资料如何准备，再看计划如何进入 MES，最后看仓储库存如何回写业务结果。
  - title: 如实标注限制
    details: 只描述当前已暴露的能力；路径走不通处直接标注缺口并引用跟踪它的 GitHub issue。
---

## 文档怎么组织

文档按"角色入口 + 四类内容"组织，各回答一类问题：

1. [按角色入门](/roles/)：我是某个角色，第一周该会什么，哪些路今天能走通。
2. 教程：新手第一次如何走通一条主线——[工程资料](/getting-started/engineering-to-production)、[计划到完工](/getting-started/planning-to-finished-goods)、[仓储库存](/getting-started/wms-inventory-cycle)。
3. [操作指南](/how-to/)：已经上手后，按任务查步骤。
4. [概念解释](/explanation/)：业务为什么这样运作，核心是[六张流程图](/processes/)。
5. [参考](/reference/)：页面与字段字典，供查阅。

## 当前文档口径

本文档站独立于 Business Console。页面入口以当前 `frontend/apps/business-console/src/pages` 中存在的路由为准，业务能力以 BusinessGateway facade 和 readiness 文档描述的当前状态为准。高级报表、完整甘特、完整 CMMS 工作台、完整打印管理体验、移动专用 `/api/mobile/v1/**`、离线同步和 MinIO/S3 multipart 仍按当前限制处理。

[内部缺口记录](/internal/gaps/product-docs-overview)
