# P2 部署演练与性能阈值化实施计划

**目标：**通过将部署产物和性能基线转化为显式的按需启用发布门禁，完成 P2 第二步加固。

**架构：**保持默认本地验证轻量化。部署演练使用独立的受治理脚本，仅在选择 profile 时启动一次性 Compose 项目。性能阈值仍可按每次运行配置，并写出 JSONL/摘要，作为 CI 或发布证据。

**实施状态（2026-05-26）：**本切片新增初始发布演练入口、机器可读的性能指标、可配置的耗时阈值，并更新就绪性、部署和脚本文档。

## 任务

- [x] 为 `Nerv.IIP.Business.Performance.Tests` 增加机器可读的性能指标输出。
- [x] 为 `scripts/verify-business-performance-baseline.ps1` 增加可配置的全局及逐场景耗时阈值。
- [x] 增加 `scripts/verify-production-release-rehearsal.ps1`，并提供显式的 `dependencies` 和 `platform-smoke` profile。
- [x] 确保 Notification 暴露 `/health`，并能在 PostgreSQL profile 下运行仅限 Development 的自动迁移冒烟验证。
- [x] 更新部署、就绪性和脚本治理文档。

## 验证

```powershell
dotnet test backend/tests/Nerv.IIP.Business.Performance.Tests/Nerv.IIP.Business.Performance.Tests.csproj --no-restore --filter FullyQualifiedName~PerformanceMetricTests
dotnet test backend/services/Notification/tests/Nerv.IIP.Notification.Web.Tests/Nerv.IIP.Notification.Web.Tests.csproj --no-restore --filter FullyQualifiedName~NotificationStartupTests
pwsh scripts/check-script-governance.ps1
pwsh scripts/verify-production-deployment-artifacts.ps1 -SkipDockerComposeConfig
git diff --check
```
