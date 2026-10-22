# M2-J 迁移前部署基线审计快照

> 冻结日期：2026-09-07  
> 基线提交：`8c9be9b15e0c1e48c3b1f06ba1e99b611ee3477c`  
> 来源：迁移前 `docs/architecture/deployment-baseline.md`

本报告只记录 M2-J 拆分时看到的**历史混合状态**，不再定义当前部署架构、交付规则或操作命令。迁移前完整原文可从固定提交查看：<https://github.com/Mang-X/Nerv-IIP/blob/8c9be9b15e0c1e48c3b1f06ba1e99b611ee3477c/docs/architecture/deployment-baseline.md>。

## 为什么需要拆分

迁移前页面同时包含四类生命周期：

1. 当前平台部署拓扑、AppHost/Compose/Connector Host 边界。
2. secret、BaseUrl、消息 provider、安装包与脚本必须遵守的交付规则。
3. bootstrap、Compose publish/prepare/deploy、release-install、数据库迁移和排障操作。
4. “当前阶段”、已完成数量、最近成功证据、历史入口删除、待跟踪 issue 与阶段性实现结论。

第 4 类内容会随代码头与执行批次变化，不能继续冒充 Architecture；前 3 类也具有不同更新频率和责任人。

## 迁移时点的历史证据示例

原页面记录过 Connector 断连验收的特定 evidence 路径、3/3 轮次和毫秒级耗时，也记录过 AppHost 项目/数据库资源数量、legacy Compose 当时覆盖范围、已删除的第四阶段脚本、Production 输入闭合进度以及后续 issue 编号。这些都只证明迁移前基线在当时时点的判断，M2-J 后不再复制到现态 Architecture/Governance/Runbook。

## M2-J 生命周期裁决

- 当前部署拓扑：[`../../architecture/platform/deployment.md`](../../architecture/platform/deployment.md)
- 当前 CI/构建/交付规则：[`../../governance/delivery/`](../../governance/delivery/)
- 当前部署操作：[`../../runbooks/deployment.md`](../../runbooks/deployment.md)
- 后端 bootstrap 阶段计划：[`../../status/archive/backend-bootstrap-plan.md`](../../status/archive/backend-bootstrap-plan.md)
- MAN-669 CI 实测：[`backend-ci-build-strategy-man-669.md`](backend-ci-build-strategy-man-669.md)

迁移只改变文档所有权与阅读路径，不修改 CI workflow、AppHost、Compose、安装脚本、发布拓扑或运行时行为。
