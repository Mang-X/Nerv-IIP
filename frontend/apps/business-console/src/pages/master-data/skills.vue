<script setup lang="ts">
import type {
  BusinessConsolePersonnelSkillMatrixCell,
  BusinessConsolePersonnelSkillMatrixRow,
} from '@nerv-iip/api-client'
import {
  usePersonnelSkillAssignment,
  usePersonnelSkillMatrix,
  useBusinessWorkers,
} from '@/composables/useBusinessMasterData'
import { useSkillCatalog } from '@/composables/usePromotedCatalogs'
import WorkerSelect from '@/components/masterData/WorkerSelect.vue'
import BusinessLayout from '@/layouts/BusinessLayout.vue'
import {
  BadgePro,
  ButtonPro,
  DialogPro,
  DialogProContent,
  DialogProDescription,
  DialogProFooter,
  DialogProHeader,
  DialogProTitle,
  DialogProTrigger,
  Field,
  FieldDescription,
  FieldGroup,
  FieldLabel,
  InputPro,
  PageHeader,
  SectionCard,
  SectionCards,
  SelectPro,
  SelectProContent,
  SelectProItem,
  SelectProTrigger,
  SelectProValue,
  Spinner,
  Toolbar,
  TooltipPro,
  TooltipProContent,
  TooltipProProvider,
  TooltipProTrigger,
} from '@nerv-iip/ui'
import { PlusIcon, RefreshCwIcon } from 'lucide-vue-next'
import { computed, reactive, ref, watch } from 'vue'
import { formatDate } from '@/utils/format'
import { notifyError, notifySuccess } from '@/utils/notify'

definePage({ meta: { requiresAuth: true, title: '人员技能' } })

// 人员技能：矩阵（工人 × 技能，一屏可读「谁会什么、到期没」）+ 登记 Dialog（录入/更新某工人某技能等级）。
const matrix = usePersonnelSkillMatrix()
const skillAssignment = usePersonnelSkillAssignment()
// 技能目录已升为主数据（#402，「基础数据 › 技能目录」）：登记选技能 + 矩阵列头解析中文名。
const { skills: skillCatalog, refresh: refreshSkillCatalog } = useSkillCatalog()
// 工人目录（IAM 用户）：把行 userId 解析成工人姓名（工号 · 部门）。
const workerDir = useBusinessWorkers()

// 技能等级取自字典 `skill-level`（系统枚举），Phase 1 用前端常量兜底中文。
const SKILL_LEVELS = [
  { value: 'junior', label: '初级' },
  { value: 'intermediate', label: '中级' },
  { value: 'senior', label: '高级' },
  { value: 'expert', label: '专家' },
] as const
const levelLabel = (value: string | undefined | null): string => {
  if (!value) return ''
  return SKILL_LEVELS.find((l) => l.value === value)?.label ?? value
}

// ── 说人话解析 ───────────────────────────────────────────────
// userId → 工人姓名（工号 · 部门）；解析不到时给友好占位，绝不暴露裸 userId。
const workerName = (userId: string | undefined): string => {
  if (!userId) return '未知工人'
  const worker = workerDir.workers.value.find((w) => w.userId === userId)
  if (!worker) return '未知工人'
  return worker.displayName?.trim() || '未命名工人'
}
const workerMeta = (userId: string | undefined): string => {
  const worker = workerDir.workers.value.find((w) => w.userId === userId)
  if (!worker) return ''
  const parts = [worker.employeeNo, worker.department].filter((v) => Boolean(v && String(v).trim()))
  return parts.join(' · ')
}
// skillCode → 技能中文名（技能目录 skillName；查不到回退编码本身，不暴露裸编码占位）。
const skillName = (skillCode: string): string => {
  const def = skillCatalog.value.find((s) => s.skillCode === skillCode)
  return def?.skillName?.trim() || skillCode
}

// 登记用技能下拉选项：取技能目录的启用项。
const skillOptions = computed(() =>
  skillCatalog.value
    .filter((s) => s.enabled !== false && (s.skillCode ?? '').trim().length > 0)
    .map((s) => ({ value: (s.skillCode ?? '').trim(), label: s.skillName?.trim() || (s.skillCode ?? '').trim() })),
)

// ── 矩阵行/格 ────────────────────────────────────────────────
// 按列头从某行 skills 取对应格（无该技能则 undefined）。
const cellOf = (
  row: BusinessConsolePersonnelSkillMatrixRow,
  skillCode: string,
): BusinessConsolePersonnelSkillMatrixCell | undefined =>
  (row.skills ?? []).find((c) => c.skillCode === skillCode)

/** 到期状态：past=已过期、soon=临期(≤30天)、ok=有效、none=无到期日。token + 文字双编码。 */
type ExpiryTone = 'past' | 'soon' | 'ok' | 'none'
function expiryTone(effectiveTo: string | undefined): ExpiryTone {
  if (!effectiveTo) return 'none'
  const end = new Date(effectiveTo)
  if (Number.isNaN(end.getTime())) return 'none'
  const days = Math.floor((end.getTime() - Date.now()) / 86_400_000)
  if (days < 0) return 'past'
  if (days <= 30) return 'soon'
  return 'ok'
}
const EXPIRY_CLASS: Record<ExpiryTone, string> = {
  past: 'border-destructive/30 bg-destructive/10 text-destructive',
  soon: 'border-warning/30 bg-warning/10 text-warning',
  ok: 'border-border bg-muted text-muted-foreground',
  none: 'border-border bg-muted text-muted-foreground',
}
const EXPIRY_BADGE: Record<ExpiryTone, string> = { past: '已过期', soon: '临期', ok: '', none: '' }

// 前端按工人/技能筛选（搜工人姓名/工号、按技能列只看持有者）。
const keyword = ref('')
const skillFilter = ref('all')
const filteredRows = computed(() => {
  const kw = keyword.value.trim().toLowerCase()
  return matrix.rows.value.filter((row) => {
    if (kw) {
      const hay = `${workerName(row.userId)} ${workerMeta(row.userId)}`.toLowerCase()
      if (!hay.includes(kw)) return false
    }
    if (skillFilter.value !== 'all') {
      const cell = cellOf(row, skillFilter.value)
      if (!cell || !cell.level) return false
    }
    return true
  })
})
// 列：按技能筛选时只显示该列，否则全部技能列。
const visibleSkillCodes = computed(() =>
  skillFilter.value === 'all'
    ? matrix.skillCodes.value
    : matrix.skillCodes.value.filter((code) => code === skillFilter.value),
)

// ── KPI（说人话、用总量；临期/过期是能驱动行动的业务指标）。
const expiryStats = computed(() => {
  let soon = 0
  let past = 0
  for (const row of matrix.rows.value) {
    for (const cell of row.skills ?? []) {
      const tone = expiryTone(cell.effectiveTo)
      if (tone === 'soon') soon += 1
      else if (tone === 'past') past += 1
    }
  }
  return { soon, past }
})

const matrixListError = computed(() =>
  matrix.matrixError.value ? '技能矩阵加载失败，请稍后重试。' : '',
)
const hasDimensions = computed(
  () => matrix.skillCodes.value.length > 0 && matrix.rows.value.length > 0,
)

function refreshAll() {
  void matrix.refresh()
  void refreshSkillCatalog()
}

// ── 登记 Dialog ─────────────────────────────────────────────
const skillOpen = ref(false)
const skillShowErrors = ref(false)
const skillForm = reactive({ userId: '', skillCode: '', level: '', effectiveFrom: '' })
function isNonEmpty(value: string) {
  return value.trim().length > 0
}
const canAssignSkill = computed(() =>
  isNonEmpty(skillForm.userId) && isNonEmpty(skillForm.skillCode) && isNonEmpty(skillForm.level),
)
watch(skillOpen, (open) => {
  if (open) {
    skillShowErrors.value = false
  }
})
/** 打开登记弹窗，可预填某工人某技能（格子点击进入）。 */
function openAssign(prefill?: { userId?: string, skillCode?: string }) {
  Object.assign(skillForm, {
    userId: prefill?.userId ?? '',
    skillCode: prefill?.skillCode ?? '',
    level: '',
    effectiveFrom: '',
  })
  skillShowErrors.value = false
  skillOpen.value = true
}
async function submitSkill() {
  if (!canAssignSkill.value) {
    skillShowErrors.value = true
    return
  }
  try {
    await skillAssignment.assign({
      userId: skillForm.userId,
      skillCode: skillForm.skillCode.trim(),
      level: skillForm.level,
      effectiveFrom: skillForm.effectiveFrom.trim() || undefined,
    })
    notifySuccess('已登记人员技能。')
    skillShowErrors.value = false
    skillOpen.value = false
    void matrix.refresh()
  }
  catch (error) {
    notifyError(error)
  }
}
</script>

<template>
  <BusinessLayout>
    <PageHeader title="人员技能" :breadcrumbs="[{ label: '基础数据' }]" :count="`${matrix.rows.value.length} 名工人`">
      <template #actions>
        <ButtonPro size="sm" variant="outline" type="button" :disabled="matrix.matrixPending.value" @click="refreshAll">
          <RefreshCwIcon aria-hidden="true" />
          刷新
        </ButtonPro>
        <ButtonPro size="sm" type="button" @click="openAssign()">
          <PlusIcon aria-hidden="true" />
          登记技能
        </ButtonPro>
      </template>
    </PageHeader>

    <p class="text-sm text-muted-foreground">
      技能矩阵：行为工人、列为技能，格内为等级与有效期，用于派工上岗资格校验。点击格子可登记或更新；临期 / 过期会高亮提醒。
    </p>

    <SectionCards :columns="3">
      <SectionCard description="在岗工人" :value="matrix.rows.value.length" hint="已登记任一技能的工人" />
      <SectionCard description="技能项" :value="matrix.skillCodes.value.length" hint="矩阵覆盖的技能种类" />
      <SectionCard
        description="临期 / 已过期"
        :value="`${expiryStats.soon} / ${expiryStats.past}`"
        hint="临期（30 天内到期）需及时复评"
      />
    </SectionCards>

    <Toolbar v-model:search="keyword" search-placeholder="搜索工人姓名 / 工号 / 部门">
      <template #filters>
        <SelectPro v-model="skillFilter">
          <SelectProTrigger class="w-44"><SelectProValue placeholder="按技能筛选" /></SelectProTrigger>
          <SelectProContent>
            <SelectProItem value="all">全部技能</SelectProItem>
            <SelectProItem v-for="code in matrix.skillCodes.value" :key="code" :value="code">
              {{ skillName(code) }}
            </SelectProItem>
          </SelectProContent>
        </SelectPro>
      </template>
    </Toolbar>

    <p v-if="matrixListError" class="text-sm text-destructive" role="alert">{{ matrixListError }}</p>

    <!-- 空态：无维度（无工人 / 无技能登记）→ 引导去登记，绝不造假数据网格。 -->
    <div
      v-if="!hasDimensions && !matrix.matrixPending.value"
      class="flex flex-col items-center gap-3 rounded-md border border-dashed border-border bg-muted/20 px-4 py-12 text-center"
    >
      <p class="text-sm text-muted-foreground">还没有任何技能登记，先为工人登记一项技能即可生成矩阵。</p>
      <ButtonPro size="sm" type="button" @click="openAssign()">
        <PlusIcon aria-hidden="true" />
        去登记技能
      </ButtonPro>
    </div>

    <!-- 筛选无结果。 -->
    <div
      v-else-if="hasDimensions && filteredRows.length === 0"
      class="flex flex-col items-center gap-3 rounded-md border border-dashed border-border bg-muted/20 px-4 py-12 text-center"
    >
      <p class="text-sm text-muted-foreground">
        没有符合条件的工人{{ skillFilter !== 'all' ? `（持有「${skillName(skillFilter)}」）` : '' }}。
      </p>
      <ButtonPro size="sm" variant="outline" type="button" @click="keyword = ''; skillFilter = 'all'">清空筛选</ButtonPro>
    </div>

    <!-- 矩阵网格：横向可滚、首列（工人）冻结。 -->
    <div v-else class="overflow-x-auto rounded-md border border-border">
      <table class="w-full border-collapse text-sm">
        <thead>
          <tr class="border-b border-border bg-muted/40">
            <th class="sticky left-0 z-10 min-w-44 bg-muted/40 px-3 py-2 text-left font-medium text-muted-foreground">
              工人
            </th>
            <th
              v-for="code in visibleSkillCodes"
              :key="code"
              class="min-w-32 px-3 py-2 text-left font-medium text-muted-foreground"
            >
              <TooltipProProvider>
                <TooltipPro>
                  <TooltipProTrigger as-child>
                    <span class="block max-w-32 truncate">{{ skillName(code) }}</span>
                  </TooltipProTrigger>
                  <TooltipProContent>{{ skillName(code) }}</TooltipProContent>
                </TooltipPro>
              </TooltipProProvider>
            </th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="row in filteredRows" :key="row.userId" class="border-b border-border last:border-b-0">
            <th class="sticky left-0 z-10 bg-card px-3 py-2 text-left align-top font-normal">
              <div class="font-medium text-foreground">{{ workerName(row.userId) }}</div>
              <div v-if="workerMeta(row.userId)" class="text-xs text-muted-foreground">{{ workerMeta(row.userId) }}</div>
            </th>
            <td v-for="code in visibleSkillCodes" :key="code" class="px-3 py-2 align-top">
              <button
                v-if="cellOf(row, code)?.level"
                type="button"
                class="flex w-full flex-col items-start gap-1 rounded-sm px-2 py-1 text-left transition-colors hover:bg-accent"
                @click="openAssign({ userId: row.userId, skillCode: code })"
              >
                <BadgePro
                  variant="neutral"
                  :class="['rounded-sm', EXPIRY_CLASS[expiryTone(cellOf(row, code)!.effectiveTo)]]"
                >
                  {{ levelLabel(cellOf(row, code)!.level) }}
                  <span v-if="EXPIRY_BADGE[expiryTone(cellOf(row, code)!.effectiveTo)]" class="ml-1">
                    · {{ EXPIRY_BADGE[expiryTone(cellOf(row, code)!.effectiveTo)] }}
                  </span>
                </BadgePro>
                <span v-if="cellOf(row, code)!.effectiveTo" class="text-xs text-muted-foreground">
                  至 {{ formatDate(cellOf(row, code)!.effectiveTo) }}
                </span>
              </button>
              <button
                v-else
                type="button"
                class="flex w-full items-center justify-center rounded-sm px-2 py-1 text-muted-foreground/50 transition-colors hover:bg-accent hover:text-muted-foreground"
                :aria-label="`为 ${workerName(row.userId)} 登记「${skillName(code)}」`"
                @click="openAssign({ userId: row.userId, skillCode: code })"
              >
                <PlusIcon class="size-4" aria-hidden="true" />
              </button>
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- 登记 Dialog：录入 / 更新某工人某技能等级；可由「登记技能」按钮或格子点击触发。 -->
    <DialogPro v-model:open="skillOpen">
      <DialogProTrigger class="sr-only" as-child>
        <button type="button">登记技能</button>
      </DialogProTrigger>
      <DialogProContent class="sm:max-w-lg">
        <DialogProHeader>
          <DialogProTitle>登记人员技能</DialogProTitle>
          <DialogProDescription>为某位工人登记一项技能与等级，可选填生效日期。带 * 为必填项。</DialogProDescription>
        </DialogProHeader>
        <form class="grid gap-4" @submit.prevent="submitSkill">
          <p v-if="skillShowErrors && !canAssignSkill" class="text-sm text-destructive" role="alert">请完整填写带 * 的必填项（已标红）。</p>
          <FieldGroup class="grid gap-3 sm:grid-cols-2">
            <Field class="sm:col-span-2" :data-invalid="skillShowErrors && !isNonEmpty(skillForm.userId)">
              <FieldLabel for="skill-worker">工人 <span class="text-destructive">*</span></FieldLabel>
              <WorkerSelect id="skill-worker" v-model="skillForm.userId" placeholder="搜索并选择工人" />
            </Field>
            <Field :data-invalid="skillShowErrors && !isNonEmpty(skillForm.skillCode)">
              <FieldLabel for="skill-code">技能 <span class="text-destructive">*</span></FieldLabel>
              <SelectPro v-model="skillForm.skillCode">
                <SelectProTrigger id="skill-code"><SelectProValue placeholder="请选择技能" /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem v-for="s in skillOptions" :key="s.value" :value="s.value">{{ s.label }}</SelectProItem>
                </SelectProContent>
              </SelectPro>
              <p v-if="!skillOptions.length" class="text-xs text-muted-foreground">技能目录为空——请先在「基础数据 › 技能目录」里维护技能项。</p>
            </Field>
            <Field :data-invalid="skillShowErrors && !isNonEmpty(skillForm.level)">
              <FieldLabel for="skill-level">等级 <span class="text-destructive">*</span></FieldLabel>
              <SelectPro v-model="skillForm.level">
                <SelectProTrigger id="skill-level"><SelectProValue placeholder="请选择等级" /></SelectProTrigger>
                <SelectProContent>
                  <SelectProItem v-for="lvl in SKILL_LEVELS" :key="lvl.value" :value="lvl.value">{{ lvl.label }}</SelectProItem>
                </SelectProContent>
              </SelectPro>
            </Field>
            <Field>
              <FieldLabel for="skill-from">生效日期</FieldLabel>
              <InputPro id="skill-from" v-model="skillForm.effectiveFrom" type="date" />
              <FieldDescription>留空表示即时生效。</FieldDescription>
            </Field>
          </FieldGroup>
          <DialogProFooter>
            <ButtonPro type="button" variant="outline" @click="skillOpen = false">取消</ButtonPro>
            <ButtonPro type="submit" :disabled="skillAssignment.assignPending.value">
              <Spinner v-if="skillAssignment.assignPending.value" aria-hidden="true" />登记技能
            </ButtonPro>
          </DialogProFooter>
        </form>
      </DialogProContent>
    </DialogPro>
  </BusinessLayout>
</template>
