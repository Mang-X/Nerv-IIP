# Nerv.IIP.Business.MasterData

BusinessMasterData 是业务平台的主数据服务，负责 SKU、单位、业务伙伴、工厂资源、班组/班次、日历、设备资产和参考数据等通用业务事实。当前服务按仓库根 `AGENTS.md`、`docs/architecture/business-platform-domain-architecture.md`、`docs/architecture/database-schema-catalog.md` 和 `docs/architecture/implementation-readiness.md` 的边界运行。

## 当前开发入口

从仓库根目录执行平台入口，不使用本服务目录下的旧模板脚本：

```powershell
.\nerv.ps1 bootstrap
.\nerv.ps1 dev
.\nerv.ps1 ports
pwsh scripts/verify-business-master-data-realignment.ps1
```

服务已纳入 `backend/Nerv.IIP.sln`、Aspire AppHost、`masterdata` schema catalog、IAM seed 和业务平台验证脚本。需要本地依赖时，由根 `.\nerv.ps1 dev -InfraOnly` 或 AppHost 管理 PostgreSQL/Redis/RabbitMQ/MinIO 等资源。

## 约束

1. 不在本服务目录维护第二套 Docker Compose、数据库密码或消息中间件拓扑。
2. 不使用模板遗留的 MySQL/Kafka 初始化说明；当前持久化基线以 PostgreSQL profile 和 schema catalog 为准。
3. 不直接接受终端用户 bearer；业务端点由 `InternalServiceAuthorizationPolicy` 保护，终端用户权限在 BusinessGateway facade 层执行。
4. 新增 schema、migration 或 public contract 时同步更新 database catalog、API/codegen 文档和 focused verify 脚本。

## 参考文档

- `docs/architecture/implementation-readiness.md`
- `docs/architecture/business-platform-domain-architecture.md`
- `docs/architecture/database-schema-catalog.md`
- `docs/architecture/api-contract-and-codegen.md`
- `docs/adr/0013-business-master-data-governance.md`
