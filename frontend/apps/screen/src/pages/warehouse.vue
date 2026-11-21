<script setup lang="ts">
import ScopePicker from '@/components/ScopePicker.vue'
import {
  NvScreenBarChart,
  NvScreenDonut,
  NvScreenFreshness,
  NvScreenPanel,
  NvScreenScrollArea,
  NvScrollBoard,
  NvScreenStatusLight,
  NvScreenStatusTag,
} from '@nerv-iip/ui'
import { ArrowUpFromLine, ChartPie, LayoutList } from '@lucide/vue'
import { ref } from 'vue'
import { RouterLink } from 'vue-router'
import ReadError from '@/components/ReadError.vue'
import { IS_REAL_DATA } from '@/data/config'
import type { WhTaskRow } from '@/data/contracts/warehouse'
import ScreenLayout from '@/layouts/ScreenLayout.vue'
import { useWarehouseScreen } from '../composables/useWarehouseScreen'

const {
  ADAPTER_ICONS,
  backLink,
  bandCells,
  board,
  boardFreshness,
  count,
  demoOps,
  domains,
  error,
  factoryName,
  flowUnit,
  fmtAge,
  heroKpi,
  inbound,
  loading,
  nf,
  opsError,
  opsFreshness,
  outbound,
  overdueTop,
  refresh,
  refreshOps,
  taskGroups,
  taskSummary,
  wcs,
  wcsView,
} = useWarehouseScreen()
</script>

<template>
  <ScreenLayout
    title="Nerv-IIP 仓储物流大屏"
    :line="board?.scopeLabel ?? factoryName"
    screen="指挥中心大屏 05"
  >
    <div v-if="domains" class="domain-controls">
      <div v-for="entry in domains.controls" :key="entry.label">
        <ScopePicker
          v-if="entry.scoped"
          :label="entry.label"
          :options="entry.source.options.value"
          :selected="entry.source.selected.value"
          @select="entry.source.select"
        />
        <span v-else>WCS · 当前组织</span>
        <small v-if="!entry.source.error.value && !entry.source.lastUpdated.value">读取中</small>
        <ReadError
          v-if="entry.source.error.value"
          :error="entry.source.error.value"
          :loading="entry.source.loading.value"
          :last-updated="entry.source.lastUpdated.value"
          @retry="entry.source.refresh"
        />
      </div>
    </div>
    <div v-if="board || domains" class="wb">
      <!-- 顶部 KPI 带：出库进度 hero + 五格，语义色渐隐强调线 -->
      <NvScreenPanel v-if="board" class="wb-band">
        <div class="wb-band-in">
          <div v-if="heroKpi" class="wb-hero">
            <div class="wb-hero-v">
              <span class="wb-num"
                >{{ heroKpi.value ?? '—' }}<small v-if="heroKpi.value !== null">%</small></span
              >
              <i class="wb-kpi-line cyan" aria-hidden="true" />
            </div>
            <div class="wb-hero-l">
              <ArrowUpFromLine :size="17" :stroke-width="1.8" class="wb-kpi-ic" />当日出库进度
            </div>
            <span class="wb-hero-sub">{{ heroKpi.sub }}</span>
          </div>
          <div v-for="c in bandCells" :key="c.label" class="wb-kpi">
            <dt class="wb-kpi-t">
              <component :is="c.icon" :size="17" :stroke-width="1.8" class="wb-kpi-ic" />{{
                c.label
              }}
            </dt>
            <dd class="wb-kpi-v" :class="c.tone">
              <span class="wb-num">{{ c.value }}<small>{{ c.unit }}</small></span>
              <i class="wb-kpi-line" :class="c.tone" aria-hidden="true" />
            </dd>
            <span
              class="wb-kpi-sub"
              :class="{
                bad: (c.subTone ?? c.tone) === 'bad',
                warn: (c.subTone ?? c.tone) === 'warn',
              }"
              >{{ c.sub }}</span
            >
          </div>
        </div>
      </NvScreenPanel>

      <div class="wb-main">
        <!-- 左：出入库双进度（大数字 + 发丝进度条 + 12h 流量） -->
        <section class="wb-flows">
          <NvScreenPanel v-if="inbound" title="当日入库 · 收货" class="wb-flow">
            <template #extra>
              <span class="wb-flow-docs"
                >收货单 {{ inbound.docsDone }}/{{ inbound.docsTotal }}</span
              >
            </template>
            <div class="wb-flow-hero">
              <span class="wb-flow-v">
                {{ nf.format(inbound.linesDone)
                }}<small>/ {{ nf.format(inbound.linesTotal) }} {{ flowUnit }}</small>
              </span>
            </div>
            <div class="wb-bar"><i :style="{ width: `${inbound.pct}%` }" /></div>
            <div class="wb-flow-meta">
              <span>
                收货完成率
                <b
                  >{{
                    inbound.docsTotal > 0
                      ? Math.round((inbound.docsDone / inbound.docsTotal) * 100)
                      : '—'
                  }}{{ inbound.docsTotal ? '%' : '' }}</b
                >
              </span>
              <span v-if="inbound.postFailedDocs > 0" class="wb-postfail">
                过账失败 {{ inbound.postFailedDoc }}
              </span>
              <span v-else class="wb-postok">{{
                inbound.docsTotal ? '暂无过账失败记录' : '今日暂无收货单'
              }}</span>
            </div>
            <!-- 小时流量是离散量 —— 柱状比面积曲线更诚实（每小时一根柱） -->
            <div class="wb-flow-spark">
              <NvScreenBarChart
                :series="[
                  { label: '完成', color: 'var(--nv-scr-cyan)', data: inbound.hourly },
                  { label: '失败', color: 'var(--nv-scr-red)', data: inbound.failedHourly },
                ]"
                :hover-labels="inbound.hourLabels"
                autoplay
              />
            </div>
            <div class="wb-flow-x">
              <span>{{ inbound.hourLabels[0] }}</span>
              <span>{{ inbound.hourLabels[6] }}</span>
              <span>现在</span>
            </div>
          </NvScreenPanel>

          <NvScreenPanel v-if="outbound" title="当日出库 · 发货" class="wb-flow out">
            <template #extra>
              <span class="wb-flow-docs"
                >{{ IS_REAL_DATA ? '已过账' : '发运' }} {{ outbound.docsDone }}/{{
                  outbound.docsTotal
                }}
                单</span
              >
            </template>
            <div class="wb-flow-hero">
              <span class="wb-flow-v">
                {{ nf.format(outbound.linesDone)
                }}<small>/ {{ nf.format(outbound.linesTotal) }} {{ flowUnit }}</small>
              </span>
            </div>
            <div class="wb-bar"><i :style="{ width: `${outbound.pct}%` }" /></div>
            <div class="wb-flow-meta">
              <span v-if="!outbound.docsTotal">今日暂无发货单</span>
              <span v-if="outbound.customers > 0">客户 <b>{{ outbound.customers }}</b> 家</span>
              <span v-if="outbound.latestShipment" class="wb-latest"
                >{{ IS_REAL_DATA ? '当日建单最近过账' : '最近发运' }}
                {{ outbound.latestShipment }}</span
              >
            </div>
            <div class="wb-flow-spark">
              <NvScreenBarChart
                :series="[
                  { label: '完成', color: 'var(--nv-scr-indigo)', data: outbound.hourly },
                  { label: '失败', color: 'var(--nv-scr-red)', data: outbound.failedHourly },
                ]"
                :hover-labels="outbound.hourLabels"
                autoplay
                :autoplay-ms="2800"
              />
            </div>
            <div class="wb-flow-x">
              <span>{{ outbound.hourLabels[0] }}</span>
              <span>{{ outbound.hourLabels[6] }}</span>
              <span>现在</span>
            </div>
          </NvScreenPanel>
        </section>

        <!-- 中：作业任务看板（拣货 / 上架 / 盘点分组，超时红标，自动滚动） -->
        <NvScreenPanel title="作业任务看板" class="wb-tasks">
          <template #extra>
            <span class="wb-tasks-sum">{{
              !board || board.mixedScopes ? '各作业范围分别统计' : taskSummary
            }}</span>
          </template>
          <div class="wb-tk">
            <section
              v-for="g in taskGroups"
              :key="g.key"
              class="tg"
              :data-kind="g.key"
              :style="{ flex: g.flex }"
            >
              <header class="tg-h">
                <component :is="g.icon" :size="14" :stroke-width="1.8" class="tg-ic" />
                <b class="tg-name">{{ g.title }}</b>
                <span class="tg-cnt">{{ g.isCount ? '待盘' : '积压' }} <b>{{ g.backlog }}</b></span>
                <em v-if="g.overdue > 0" class="tg-late">超时 {{ g.overdue }}</em>
                <span v-if="g.isCount && count" class="tg-var" :class="{ on: count.variance > 0 }">
                  差异 {{ count.variance }} 位
                </span>
                <span class="tg-rule" aria-hidden="true" />
                <span class="tg-done">{{ g.meta }}</span>
              </header>
              <p v-if="!g.rows.length" class="tg-empty">暂无{{ g.title }}任务</p>
              <div v-else class="tg-cols" :class="{ count: g.isCount }">
                <template v-if="!g.isCount">
                  <span>单号</span><span>物料</span><span class="r">数量</span><span>库位流向</span
                  ><span>来源单</span><span class="r">龄期</span>
                </template>
                <template v-else>
                  <span>单号</span><span>物料</span><span class="r">账面数量</span
                  ><span>待盘库位</span><span class="r">龄期</span>
                </template>
              </div>
              <div v-if="g.rows.length" class="tg-list">
                <NvScrollBoard :items="g.rows" :row-key="(r: WhTaskRow) => r.id" :speed="g.speed">
                  <template #row="{ item }">
                    <div class="tg-row" :class="{ count: g.isCount, late: item.overdue }">
                      <span class="tg-id">{{ item.id }}</span>
                      <span class="tg-sku">{{ item.sku }}</span>
                      <span class="tg-qty r"
                        >{{ nf.format(item.qty) }}<small> {{ item.unit }}</small></span
                      >
                      <span v-if="!g.isCount" class="tg-route">
                        <span class="tg-from">{{ item.from }}</span>
                        <i class="tg-arrow" aria-hidden="true">→</i>
                        <span class="tg-to">{{ item.to }}</span>
                      </span>
                      <span v-else class="tg-route">{{ item.from }}</span>
                      <span v-if="!g.isCount" class="tg-ref">{{ item.ref }}</span>
                      <span class="tg-age r" :class="{ late: item.overdue }">
                        {{ fmtAge(item.ageMin) }}<em v-if="item.overdue">超时</em>
                      </span>
                    </div>
                  </template>
                </NvScrollBoard>
              </div>
            </section>
          </div>
        </NvScreenPanel>

        <!-- 右：WCS 自动化（失败告警 + 指令状态合并为一块 —— 单一 flex 容器内动态共享
             垂直空间：失败多则告警区滚动、不挤压下方；省一个面板头/间距给适配器行） · 任务超时榜 -->
        <section class="wb-wcs">
          <NvScreenPanel
            title="WCS 自动化"
            :accent="wcs && wcs.failures.length > 0 ? 'red' : undefined"
            class="wb-wcsboard"
          >
            <template #extra>
              <span
                v-if="wcs && !wcs.readFailure"
                class="wf-count"
                :class="{ calm: !wcs || wcs.failures.length === 0 }"
              >
                {{
                  wcs && wcs.failures.length > 0
                    ? `${wcs.failures.length} 条未恢复`
                    : '暂无失败指令'
                }}
              </span>
            </template>
            <p v-if="wcs?.readFailure">
              {{ wcs.readFailure }} <button @click="refresh">重试</button>
            </p>
            <ReadError
              v-else-if="!wcs && domains?.wcs.error.value"
              :error="domains.wcs.error.value"
              @retry="domains.wcs.refresh"
            />
            <div v-else-if="wcs" class="wm">
              <!-- ① 失败告警（异常优先置顶；约 3 条完整显示，更多滚动） -->
              <NvScreenScrollArea class="wf-list">
                <div v-for="x in wcs.failures" :key="x.cmd" class="wf-row">
                  <i class="wf-dot" aria-hidden="true" />
                  <div class="wf-main">
                    <div class="wf-top">
                      <b class="wf-ad">{{ x.adapter }}</b>
                      <span class="wf-cmd">{{ x.cmd }}</span>
                      <span class="wf-since">{{ x.firstAt }} 起 · {{ x.sinceMin }} min</span>
                    </div>
                    <div class="wf-sub">
                      <span class="wf-err">{{ x.error }}</span>
                      <em class="wf-retry">重试 {{ x.retries }} 次</em>
                    </div>
                  </div>
                </div>
                <div v-if="wcs.failures.length === 0" class="wf-empty">
                  <NvScreenStatusLight tone="run" label="无失败指令" />
                </div>
              </NvScreenScrollArea>
              <!-- ② 链路负载：环形图 ↔ 适配器明细列表点击切换（同一区域二选一，列表得满高） -->
              <div class="wm-toggle">
                <span class="wm-toggle-t">链路负载</span>
                <div class="wm-seg" role="tablist" aria-label="链路负载视图">
                  <button
                    type="button"
                    class="wm-seg-b"
                    :class="{ on: wcsView === 'chart' }"
                    role="tab"
                    :aria-selected="wcsView === 'chart'"
                    aria-label="环形图"
                    @click="wcsView = 'chart'"
                  >
                    <ChartPie :size="13" :stroke-width="1.9" />
                  </button>
                  <button
                    type="button"
                    class="wm-seg-b"
                    :class="{ on: wcsView === 'list' }"
                    role="tab"
                    :aria-selected="wcsView === 'list'"
                    aria-label="链路明细"
                    @click="wcsView = 'list'"
                  >
                    <LayoutList :size="13" :stroke-width="1.9" />
                  </button>
                </div>
              </div>
              <div class="wm-body">
                <Transition name="wm-swap" mode="out-in">
                  <!-- 环形：排队/执行/失败 = 当前在链指令，中心 = 今日完成 -->
                  <div v-if="wcsView === 'chart'" key="chart" class="wm-chart">
                    <NvScreenDonut
                      class="wa-donut"
                      :size="104"
                      :segments="[
                        {
                          label: '执行中',
                          value: wcs.counts.running,
                          color: 'var(--nv-scr-cyan)',
                        },
                        {
                          label: '已完成',
                          value: wcs.counts.completed,
                          color: 'var(--nv-scr-green)',
                        },
                        {
                          label: '失败',
                          value: wcs.counts.failed,
                          color: 'var(--nv-scr-red)',
                        },
                      ]"
                    >
                      <b class="wa-dn-num">{{ nf.format(wcs.counts.completed) }}</b>
                      <span class="wa-dn-cap">今日完成</span>
                    </NvScreenDonut>
                  </div>
                  <!-- 适配器明细（满高，超出滚动） -->
                  <NvScreenScrollArea v-else key="list" class="wa-rows">
                    <div v-for="a in wcs.adapters" :key="a.kind" class="wa-row">
                      <component
                        :is="ADAPTER_ICONS[a.kind]"
                        :size="14"
                        :stroke-width="1.8"
                        class="wa-ic"
                      />
                      <span class="wa-name">{{ a.label }}</span>
                      <span class="wa-nums"
                        >执行 <b>{{ a.running }}</b> · 排队 <b>{{ a.queued }}</b></span
                      >
                      <span class="wa-done">完成 {{ nf.format(a.completed) }}</span>
                      <b v-if="a.failed > 0" class="wa-fail">失败 {{ a.failed }}</b>
                    </div>
                    <div v-if="wcs.adapters.length === 0" class="wf-empty">
                      <NvScreenStatusLight tone="run" label="无在链设备" />
                    </div>
                  </NvScreenScrollArea>
                </Transition>
              </div>
            </div>
          </NvScreenPanel>

          <NvScreenPanel
            v-if="board && !board.mixedScopes"
            title="任务超时榜 · TOP5"
            class="wb-overdue"
          >
            <template #extra>
              <NvScreenStatusTag tone="amber" label="龄期按创建时刻推算" />
            </template>
            <div class="wo-list">
              <div v-for="(r, i) in overdueTop" :key="r.id" class="wo-row">
                <b class="wo-rank" :class="{ top: i === 0 }">{{ i + 1 }}</b>
                <span class="wo-id">{{ r.id }}</span>
                <span class="wo-kind">{{ r.kindLabel }}</span>
                <span class="wo-sku">{{ r.sku }}</span>
                <b class="wo-age">{{ fmtAge(r.ageMin) }}</b>
              </div>
              <div v-if="overdueTop.length === 0" class="wo-empty">
                <NvScreenStatusLight tone="run" label="无超时任务" />
              </div>
            </div>
          </NvScreenPanel>
        </section>
      </div>

      <footer class="wb-foot">
        <span class="wb-foot-l">
          <RouterLink :to="backLink.to" class="wb-back">‹ {{ backLink.label }}</RouterLink>
          <span>WMS 作业域当前窗口</span>
        </span>
        <span class="wb-foot-r">
          <template v-if="board && !board.mixedScopes"
            >{{ IS_REAL_DATA ? '当日建单已完成' : '当日吞吐' }}
            <b>{{ nf.format(board.kpis.throughputLines) }}</b> {{ flowUnit }} （入
            {{ nf.format(board.inbound.linesDone) }} · 出
            {{ nf.format(board.outbound.linesDone) }}）</template
          >
          <ReadError v-if="error" :error="error" :loading="loading" @retry="refresh" />
          <NvScreenFreshness :tone="boardFreshness.tone" :label="`主数据 ${boardFreshness.text}`" />
          <ReadError v-if="opsError" :error="opsError" @retry="refreshOps" />
          <NvScreenFreshness
            v-else-if="demoOps"
            :tone="opsFreshness.tone"
            :label="`任务/WCS ${opsFreshness.text}`"
          />
        </span>
      </footer>
    </div>
    <div v-else class="wb-loading">
      <ReadError v-if="error" :error="error" :loading="loading" @retry="refresh" />
      <span v-else>正在读取…</span>
      <RouterLink :to="backLink.to">‹ {{ backLink.label }}</RouterLink>
    </div>
  </ScreenLayout>
</template>

<style scoped src="../styles/warehouse-layout.css"></style>
<style scoped src="../styles/warehouse-flow-and-tasks.css"></style>
<style scoped src="../styles/warehouse-automation-and-footer.css"></style>
