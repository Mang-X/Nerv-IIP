# 部署与交付治理

本文规定 AppHost、Compose、安装/发布制品与部署 profile 的当前交付约束。拓扑本身见 [`../../architecture/platform/deployment.md`](../../architecture/platform/deployment.md)，操作步骤见 [`../../runbooks/deployment.md`](../../runbooks/deployment.md)。

## 单一拓扑规则

1. `infra/aspire/Nerv.IIP.AppHost` 是唯一平台级拓扑模型；不得为服务、客户或交付形态维护第二套完整服务图。
2. 完整平台 Compose 必须由 Aspire deployment target 生成。`infra/docker-compose.dev.yml` 仅作本地依赖兜底，`infra/compose/` 的 legacy overlay 仅作迁移期验证/既有发布演练。
3. 新服务、新基础设施依赖或新的部署 profile 先进入 AppHost，再由 Compose/安装/交付入口消费；不得先在手写 Compose 中形成事实再反向追认。
4. 主平台与 Connector Host 是不同分发单元；Connector Host 必须保持独立安装、升级和回滚边界。

## 配置与安全规则

1. AppHost、生成的 Compose、安装包和整合脚本必须消费同一配置语义。精确配置键和参数以 AppHost、服务配置、`nerv.ps1 help` 与安装脚本帮助为准。
2. 非 Development 环境所需的内部认证材料、IAM secret、Connector scope、消息 provider、CORS、数据库连接和服务 BaseUrl 必须显式提供；服务或脚本的 fail-fast 守卫不得被文档或交付包装绕过。
3. secret、token、pepper、密码、连接串和客户私有配置不得写入仓库、Compose 明文模板、命令回显或公开日志。诊断只记录是否已配置、fingerprint 或经批准的脱敏信息。
4. 服务间地址继续使用服务当前声明的配置键；不得在 PoC、Compose、安装包或 Production profile 静默 fallback 到 localhost。
5. 非 Development 的消息 provider、持久化与 seed/AutoMigrate 行为必须遵循服务启动守卫和 AppHost 环境门控；不能为了“部署能起来”把 Development 默认带入 Production 产物。

## 发布与数据规则

1. 数据库初始化、迁移、seed、备份与恢复按 ADR 0009 和 [`../../runbooks/database-release.md`](../../runbooks/database-release.md) 执行；部署入口不得绕过 EF migrations history 直接建生产表。
2. 安装/发布脚本必须遵循 [`../script-automation.md`](../script-automation.md) 的分类、副作用、日志、超时、进程清理和敏感信息要求。
3. 交付物必须可追溯到明确 commit/release 与 producer；真实客户安装器、系统服务注册、备份恢复或离线交付若尚未由代码 producer 提供，不得仅凭架构计划宣称已支持。
4. 镜像 major、服务数量、数据库数量、端口和 profile 默认值属于易漂移事实；Governance 不冻结副本，升级或变更按对应 producer 与跟踪 issue 验证。

## Compose 与安装边界

- Compose 进入交付前需要通过当前已有的产物/发布演练验证；检查项与命令由现有 verify 脚本和 workflow 定义。
- 客户差异通过受控参数、环境文件、secret 注入或安装入口表达，不复制长期平行 Compose。
- 安装包/zip/Windows Service/systemd 等制品只能承诺实际 producer 已实现的能力；规划项留在 issue/spec，不写成已交付规则。
- 本轮文档治理不修改 `.github/workflows/ci.yml`、AppHost、Compose、install/package 脚本或发布行为。
