# API 契约与代码生成 Runbook

本页只描述当前执行与排障步骤；规范性规则见 [`../governance/api/contracts-and-codegen.md`](../governance/api/contracts-and-codegen.md)，稳定路径见 [`../reference/api/contracts-and-codegen.md`](../reference/api/contracts-and-codegen.md)。

## 首选闭环：验证 OpenAPI / api-client 无漂移

仓库根目录执行：

```powershell
pwsh -NoProfile -File ./scripts/verify-openapi-client-drift.ps1
```

该脚本是当前契约漂移闭环：默认会启动并导出 PlatformGateway 与 BusinessGateway OpenAPI、按 `frontend/package.json` 安装/调用当前 pnpm、执行 `generate:api`，最后检查受管 snapshot 与 generated 目录的 tracked/untracked drift。不要用一串自定义 shell 命令替代它后再声称通过同一门禁。

脚本当前要求 PowerShell 7、.NET SDK 10、Node.js 22.22.3 与 pnpm 11.22.0；精确要求以脚本头和仓库 package/build 配置为准。`-SkipRegenerate` / `-SkipFrontendInstall` 只能在调用方明确已经满足对应前置条件时使用，不能用来跳过本应验证的生成事实。

## 仅导出 Gateway OpenAPI

需要单独刷新受控 snapshot 时，在仓库根目录执行：

```powershell
pwsh -NoProfile -File ./scripts/export-gateway-openapi.ps1
```

该脚本会构建并启动本地 PlatformGateway 与 BusinessGateway，从 `/swagger/v1/swagger.json` 获取文档，并写入：

- `frontend/packages/api-client/openapi/platform-gateway.v1.json`
- `frontend/packages/api-client/openapi/business-gateway-console.v1.json`

同时会写入 `artifacts/openapi-export/**` 与脚本日志，并负责停止其管理的 Gateway 进程。snapshot 只能来自此类后端导出流程，不手工编辑。

## 仅重新生成前端 API client

在仓库根目录执行：

```bash
pnpm -C frontend generate:api
```

当前 `frontend/package.json` 还提供：

```bash
pnpm -C frontend check
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

按实际影响运行；不要把只执行 `generate:api` 描述为 typecheck/test/build 已通过。

## 常规公开 API 变更流程

1. 修改后端 endpoint/DTO/授权和对应测试，并确认稳定 `operationId`。
2. 若是业务服务 endpoint，按 [`../governance/api/facade-coverage.md`](../governance/api/facade-coverage.md) 同步分类机器事实。
3. 运行 `scripts/export-gateway-openapi.ps1` 或直接运行首选的 `scripts/verify-openapi-client-drift.ps1` 获取受控 snapshot。
4. 运行 `pnpm -C frontend generate:api`（若首选 drift 脚本已完成该步则无需重复）。
5. 只从 `@nerv-iip/api-client` 稳定入口接线消费方，不从 `src/generated/**` 深层导入。
6. 运行受影响的前端/后端测试与当前 CI；确认 OpenAPI/api-client Drift、Script Governance 和其它被 impact plan 选中的 lane 真实通过。

## BusinessGateway surface canonicalization / restore

BusinessGateway client surface 的规范化/恢复操作必须同时读取：

- Governance 合同：[`../governance/api/business-gateway-surface.md`](../governance/api/business-gateway-surface.md)
- 固定恢复输入：[`../reference/api/business-gateway-surface-restore.manifest.json`](../reference/api/business-gateway-surface-restore.manifest.json)

固定 restore 必须使用合同规定的 SDK/TFM、locked mode、独立 package/cache staging 和仓库 `NuGet.config`；任何 manifest/lock/reference graph 漂移都 fail closed，不得现场刷新 approved fixture 后继续。精确 `dotnet restore` / `dotnet msbuild` 参数由该 Governance 合同维护，本 Runbook 不复制第二份可能漂移的参数表。

## Facade coverage

修改业务服务 HTTP endpoint 时，更新 [`../reference/api/facade-coverage-matrix.json`](../reference/api/facade-coverage-matrix.json)。

- `exposed`：必须有 Gateway facade、非空 `gateways` / `gatewayOperationIds`，且 operationId 能在对应 Gateway OpenAPI snapshot 验证。
- `deferred`：必须保留明确 follow-up。
- `internal`：必须保留明确 rationale。

`Nerv.IIP.FacadeCoverage.Tests` 读取 Reference JSON 与 Governance Markdown；迁移后不要把测试或脚本重新接回旧 Architecture 兼容路径。

## 排障

- **snapshot 漂移**：先确认后端 OpenAPI 是否为预期变更，再重新导出/生成；不要反向编辑 snapshot 或 generated 文件。
- **operationId 漂移**：在 Gateway endpoint 命名生产者修复，不在生成产物中重命名。
- **机器间 description 漂移**：确认 Gateway Swagger 配置仍关闭 external NuGet XML documentation，且导出脚本按当前规则使用 `NUGET_XMLDOC_MODE=skip`；不要通过删除本机缓存文件或改变 `NUGET_PACKAGES` 规避。
- **生成目录残留**：使用仓库现有生成/清理链路，不新增临时脚本或手工删除后提交不完整结果。
- **门禁失败**：按失败 lane 的当前 producer/测试定位。只报告真实执行结果，不把 docs-only、局部测试或合法 skip 外推为完整 CI 证据。

历史第三阶段总验收脚本 `scripts/verify-third-slice-console.ps1` 已退役，不应重新作为当前 API/codegen 验证入口。