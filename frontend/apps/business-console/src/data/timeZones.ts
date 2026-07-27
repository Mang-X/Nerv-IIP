/**
 * 工厂时区（IANA Time Zone Database 标识符）前端受控清单。
 *
 * 时区不是业务字典、后端也不下发目录，但取值必须是合法 IANA ID（后端与前端都按 IANA 解析），
 * 手输极易写成 `GMT+8` / `Asia/ShangHai` 这类无效值。这里给一份**中国与亚太常用**清单，
 * 覆盖国内工厂与东南亚/日韩/印度等常见海外厂区；需要更多时区时在此扩充，不在页面里写死。
 */
import type { RefOption } from './masterDataReference'

/** 平台默认时区（新建工厂的预置值）。 */
export const DEFAULT_TIME_ZONE = 'Asia/Shanghai'

export const TIME_ZONE_OPTIONS: RefOption[] = [
  { value: 'Asia/Shanghai', label: '中国标准时间（UTC+8）· 上海' },
  { value: 'Asia/Urumqi', label: '中国西部时间（UTC+6）· 乌鲁木齐' },
  { value: 'Asia/Hong_Kong', label: '香港时间（UTC+8）' },
  { value: 'Asia/Macau', label: '澳门时间（UTC+8）' },
  { value: 'Asia/Taipei', label: '台北时间（UTC+8）' },
  { value: 'Asia/Tokyo', label: '日本标准时间（UTC+9）· 东京' },
  { value: 'Asia/Seoul', label: '韩国标准时间（UTC+9）· 首尔' },
  { value: 'Asia/Singapore', label: '新加坡时间（UTC+8）' },
  { value: 'Asia/Kuala_Lumpur', label: '马来西亚时间（UTC+8）· 吉隆坡' },
  { value: 'Asia/Bangkok', label: '中南半岛时间（UTC+7）· 曼谷' },
  { value: 'Asia/Ho_Chi_Minh', label: '越南时间（UTC+7）· 胡志明市' },
  { value: 'Asia/Jakarta', label: '印尼西部时间（UTC+7）· 雅加达' },
  { value: 'Asia/Manila', label: '菲律宾时间（UTC+8）· 马尼拉' },
  { value: 'Asia/Kolkata', label: '印度标准时间（UTC+5:30）· 加尔各答' },
  { value: 'Asia/Dubai', label: '海湾标准时间（UTC+4）· 迪拜' },
  { value: 'Europe/Berlin', label: '中欧时间（UTC+1/+2）· 柏林' },
  { value: 'Europe/London', label: '英国时间（UTC+0/+1）· 伦敦' },
  { value: 'America/Mexico_City', label: '墨西哥中部时间（UTC-6）· 墨西哥城' },
  { value: 'America/Chicago', label: '北美中部时间（UTC-6/-5）· 芝加哥' },
  { value: 'America/Los_Angeles', label: '北美太平洋时间（UTC-8/-7）· 洛杉矶' },
  { value: 'UTC', label: '协调世界时（UTC）' },
]
