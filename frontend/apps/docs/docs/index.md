---
layout: home

hero:
  name: Nerv-IIP 产品文档
  text: 从资料、计划到执行的业务平台上手指南
  tagline: 用三条端到端路径理解当前产品主线，并用流程图识别单据、状态和限制。
  actions:
    - theme: brand
      text: 基础资料到工程资料
      link: /getting-started/engineering-to-production
    - theme: alt
      text: 计划到完工入库
      link: /getting-started/planning-to-finished-goods
    - theme: alt
      text: 查看流程图
      link: /processes/

features:
  - title: 10 分钟理解主线
    details: 先看资料如何准备，再看计划如何进入 MES，最后看仓储库存如何回写业务结果。
  - title: 按角色组织
    details: 面向工程、计划、生产、仓储、质量和设备角色说明页面入口、对象流和状态变化。
  - title: 如实标注限制
    details: 只描述当前 Business Console、Business PDA 和 BusinessGateway 已暴露的能力；未形成闭环的内容写入内部缺口记录。
---

## 推荐阅读顺序

1. [基础资料到工程资料](/getting-started/engineering-to-production)：先确认 SKU、UOM、工厂资源，再建立 EBOM、MBOM、工艺路线和生产版本。
2. [需求计划到完工入库](/getting-started/planning-to-finished-goods)：从需求、MRP 和 APS 到生产工单、报工与完工入库。
3. [仓储收发与库存闭环](/getting-started/wms-inventory-cycle)：理解收货、上架、库存、拣货、出库和库存移动。
4. [核心流程图](/processes/)：用六张图快速对照工程资料、计划生产、仓储库存、质量审批、设备维护和条码追溯。

## 当前文档口径

本文档站独立于 Business Console。页面入口以当前 `frontend/apps/business-console/src/pages` 中存在的路由为准，业务能力以 BusinessGateway facade 和 readiness 文档描述的当前状态为准。高级报表、完整甘特、完整 CMMS 工作台、正式条码前端页面、移动专用 `/api/mobile/v1/**`、离线同步和 MinIO/S3 multipart 仍按当前限制处理。

[内部缺口记录](/internal/gaps/product-docs-overview)
