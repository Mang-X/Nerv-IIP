/**
 * NvUI import hygiene 门禁的调用壳（ADR 0020 Decision 4.4 / #789 收口 / #2022）。
 *
 * 规则本体是 `@nerv-iip/ui/test-support` 里的唯一实现，四个 app 的这份壳必须字节相同——
 * 一致性与完整性由该实现自己断言。要改规则请改那边，不要在这里加特例。
 */
import { runNvUiImportHygieneContract } from '@nerv-iip/ui/test-support'

runNvUiImportHygieneContract(import.meta.url)
