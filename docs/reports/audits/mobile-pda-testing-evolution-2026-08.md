# business-pda 测试分层演进审计（冻结）

当前证明边界见 [`../../governance/testing/mobile-pda.md`](../../governance/testing/mobile-pda.md)，当前执行入口见 [`../../runbooks/testing/mobile-pda.md`](../../runbooks/testing/mobile-pda.md)。

M2-H 前的 `mobile-pda-testing-and-smoke.md` 同时维护 jsdom、mock Playwright、live stack、Android emulator/APK、真机 smoke，以及大量 spec 计数和逐轮走查结果。拆分后：

- 长期“每层能证明什么”进入 Governance；
- 当前命令与设备 smoke 步骤进入 Runbook；
- spec/fixture/build producer 导航进入 Reference；
- 具体用例数、某次 live/emulator/真机结果和历史问题形成过程留在本冻结审计/Git 历史，不再作为现态规则。

完整 pre-M2-H 正文：

`6e8747a8f93a6398c45c8eb2f2a33ad3a7b64019:docs/architecture/mobile-pda-testing-and-smoke.md`
