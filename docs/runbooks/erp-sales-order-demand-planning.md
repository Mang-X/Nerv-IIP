# ERP 销售订单 → DemandPlanning 验证 Runbook

本页只承载开发/演示/真实跨进程验证与故障诊断操作；当前事实所有权和版本收敛见 [`../architecture/business/sales-order-to-demand-planning.md`](../architecture/business/sales-order-to-demand-planning.md)。精确参数、默认值、证据目录和 transport 行为以当前 AppHost 配置、脚本帮助、测试和代码为准。

## 开发/演示

开发环境可按当前 ERP seed/config producer 准备可重复销售订单来源；任何 demo/history seed 必须保持 Development-only，非 Development 环境出现启用配置时应 fail-closed。不要把演示 seed 的编号、客户/SKU 或当前页面状态写回 Architecture。

需要演示手工录入时，先关闭会占用同一业务编号/来源的 demo seed，再按当前 MasterData、ProductEngineering、ERP 和 DemandPlanning 公开流程准备客户、SKU/UOM、站点、ProductionVersion 及销售订单；默认 seed 存在时直接复用，不重复创建同号订单。

## 真实跨进程验收

仓库受治理入口：

```powershell
pwsh scripts/verify-erp-sales-order-demand-planning.ps1
```

脚本当前帮助和源码是参数、超时预算、基础设施 profile、进程管理与 evidence 路径的权威来源。验收必须使用真实受支持 transport 与一次性数据库，不得用 InMemory transport 冒充跨进程消息证明。

最低验证语义：

1. released 订单在 Planning 收敛为带稳定 source reference/version 的 DemandSource；
2. 更高版本 changed 更新数量/交期；
3. 低版本迟到事件不回滚 watermark；
4. cancelled 使需求归零并阻止旧 release/change 复活；
5. 完全重复幂等键不产生第二次业务影响；
6. MRP/pegging 沿用 ERP source reference。

## 运行纪律

- 状态变更写请求只按脚本定义的有界预算发送一次；请求超时代表提交结果未知，不靠重新 POST 猜测。
- 收敛通过后续查询/消息证据证明；transport/数据库瞬态错误与业务拒绝需要保持不同分类。
- 失败时保留受治理的脱敏 HTTP 观测、inbox/DLQ/watermark/DemandSource、transport 与服务日志证据；秘密、token、连接串不得进入 artifact。
- finally 必须核对脚本启动的进程、一次性数据库和基础设施资源均完成清理；清理失败就是验证失败。

M2-L 前包含历史故障成因、特定 CI 首轮事件和旧动态参数的原文冻结于 [`../reports/m2-l-sales-order-to-demand-planning-pre-clean-2026-09-07.md`](../reports/m2-l-sales-order-to-demand-planning-pre-clean-2026-09-07.md)，不作为当前操作说明。
