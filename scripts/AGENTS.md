# 脚本目录路由

## 目录与命名

| 位置 | 放什么 |
| --- | --- |
| 根目录 | 供开发者、运维或 `nerv.ps1` 直接调用的命令 |
| `ci/` | 工作流的变更分析、工作区选择、分片计时和汇总检查 |
| `config/` | 脚本共享的 JSON 清单、策略、基线与豁免 |
| `lib/` | 由入口加载的 PowerShell 函数库 |
| `tests/`、`tests/fixtures/` | 可重复执行的契约测试及其固定输入 |
| `install/`、`package/`、`support/` | 随包安装的脚本与配置、打包命令、可复用的验证辅助程序 |
| `tmp/` | 仅供本机的一次性脚本与草稿；Git 只跟踪该目录的忽略规则 |

新增命令按 `动词-对象.ps1` 命名，使用小写英文和连字符。JSON 文件使用小写连字符，函数库沿用 `PascalCase.ps1`，契约测试沿用 `主题.Tests.ps1`。从 `tmp/` 提升为正式脚本时，按用途迁入相应目录并接入实际调用方。可再生成的运行产物写入仓库根的 `artifacts/`。

## 修改和验证

- **修改脚本：** 修改前必须读 `docs/governance/script-automation.md`；脚本分类、副作用、ownership、helper 与门禁以该 Governance 和当前机器检查为准。
- **执行、兼容验证或排障：** 再读 `docs/runbooks/script-automation.md`，并以 `nerv.ps1 help`、目标脚本 `Get-Help`、源码和测试核实当前命令/参数。
- **历史事故/扫描器形成过程：** 只在任务确需核对历史时按原路径从 Git 历史读取事故记录，脚本直接引用的审计依据保留在原路径；历史结论不得覆盖当前 checker/helper 行为。
- **脚本/CI 清洗：** 删除重复/临时脚本、缩减影子框架或更广 CI routing 属 #2157 owner，不借普通脚本修改扩张范围。
