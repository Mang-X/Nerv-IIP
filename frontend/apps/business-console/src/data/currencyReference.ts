/**
 * 币种受控值：GB/T 12406《表示货币的代码》（等同 ISO 4217）的三位字母代码。
 *
 * 仓库里没有币种主数据，后端只校验长度，所以在前端集中成一张常用币种表，表单从这里选，
 * 提交的仍是三位字母代码。收录范围：人民币加上中国外汇交易中心挂牌交易的主要外币。
 */
import type { SearchSelectOption } from '@nerv-iip/ui'

export const CURRENCY_OPTIONS: SearchSelectOption[] = [
  { value: 'CNY', label: 'CNY 人民币' },
  { value: 'USD', label: 'USD 美元' },
  { value: 'EUR', label: 'EUR 欧元' },
  { value: 'JPY', label: 'JPY 日元' },
  { value: 'HKD', label: 'HKD 港元' },
  { value: 'GBP', label: 'GBP 英镑' },
  { value: 'MOP', label: 'MOP 澳门元' },
  { value: 'KRW', label: 'KRW 韩元' },
  { value: 'SGD', label: 'SGD 新加坡元' },
  { value: 'AUD', label: 'AUD 澳大利亚元' },
  { value: 'NZD', label: 'NZD 新西兰元' },
  { value: 'CAD', label: 'CAD 加拿大元' },
  { value: 'CHF', label: 'CHF 瑞士法郎' },
  { value: 'THB', label: 'THB 泰铢' },
  { value: 'MYR', label: 'MYR 马来西亚林吉特' },
  { value: 'RUB', label: 'RUB 俄罗斯卢布' },
]

/**
 * 编辑已有记录时用：后端历史上接受过任意三位代码，表外的旧值也要能原样显示、原样提交。
 */
export function currencyOptionsIncluding(code: string): SearchSelectOption[] {
  if (!code || CURRENCY_OPTIONS.some((option) => option.value === code)) return CURRENCY_OPTIONS
  return [...CURRENCY_OPTIONS, { value: code, label: code }]
}
