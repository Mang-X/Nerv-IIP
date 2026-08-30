[Issue #2192 C API surface canonicalization 合同实施计划索引](https://github.com/Mang-X/Nerv-IIP/issues/2192)

本阶段只交付可审阅合同、restore manifest 与 evaluated ProjectReference closure 的 per-project lock fixtures：

- `docs/architecture/business-gateway-api-surface-canonicalization.md`
- `docs/architecture/business-gateway-api-surface-restore.manifest.json`
- `backend/**/packages.lock.json`（仅 manifest 列出的 15 个 evaluated ProjectReference 项目）

Issue #2192 受控 spec 区块与合同文档同等权威；任何差异都阻断。baseline、治理检查、等价 mutation、生产代码及 provider/full-chain 证据均留待后续独立 PR。
