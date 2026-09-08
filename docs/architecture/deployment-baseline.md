# 部署基线（M2-J 兼容入口）

此路径自 M2-J 起只负责兼容旧链接，不再承载部署正文。

- 当前平台部署拓扑：[`platform/deployment.md`](platform/deployment.md)
- 当前 CI / 构建 / 交付规则：[`../governance/delivery/`](../governance/delivery/)
- 当前 bootstrap、Compose、release-install 与排障操作：[`../runbooks/deployment.md`](../runbooks/deployment.md)
- M2-J 迁移前的时点事实与生命周期裁决：[`../reports/audits/deployment-baseline-pre-m2-j-2026-09-07.md`](../reports/audits/deployment-baseline-pre-m2-j-2026-09-07.md)

精确拓扑、命令、参数、版本和配置行为始终回到 AppHost、`nerv.ps1 help`、当前 workflow/脚本/配置 producer。兼容入口删除条件由 M2-M/M4 统一收口。
