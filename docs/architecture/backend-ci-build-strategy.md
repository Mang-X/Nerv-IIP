# 后端 CI 构建策略（M2-J 兼容入口）

此路径自 M2-J 起不再保存 MAN-669 的实测数据或当前 CI 规则。

- MAN-669 运行 ID、耗时、artifact 大小、探针方法与裁决依据已按原 Git blob 冻结到 [`../reports/audits/backend-ci-build-strategy-man-669.md`](../reports/audits/backend-ci-build-strategy-man-669.md)。
- 当前 CI/构建规则位于 [`../governance/delivery/ci-build.md`](../governance/delivery/ci-build.md)。
- 精确 job、step、runner、timeout 与命令以 `.github/workflows/ci.yml`、`scripts/backend-test-shards.json` 和对应 runner/verifier 为准。

兼容入口删除条件由 M2-M/M4 统一收口。
