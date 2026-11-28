import type { BusinessConsoleTelemetryAlarmRuleItem } from '@nerv-iip/api-client'
import { formatTelemetryUnit } from '@/data/businessLabels'

export function telemetryReferenceLines(
  rules: BusinessConsoleTelemetryAlarmRuleItem[],
  tagKey: string,
) {
  return rules.flatMap((rule) =>
    rule.isEnabled && rule.tagKey === tagKey && rule.thresholdValue != null
      ? [
          {
            value: rule.thresholdValue,
            label:
              `${rule.comparisonOperator} ${rule.thresholdValue} ${formatTelemetryUnit(rule.unitCode, '')}`.trim(),
          },
        ]
      : [],
  )
}

export function exceedsTelemetryThreshold(
  value: number,
  rule: BusinessConsoleTelemetryAlarmRuleItem,
) {
  const threshold = rule.thresholdValue
  if (!rule.isEnabled || threshold == null) return false
  switch (rule.comparisonOperator) {
    case '>':
      return value > threshold
    case '>=':
      return value >= threshold
    case '<':
      return value < threshold
    case '<=':
      return value <= threshold
    case '==':
      return value === threshold
    case '!=':
      return value !== threshold
    default:
      return false
  }
}
