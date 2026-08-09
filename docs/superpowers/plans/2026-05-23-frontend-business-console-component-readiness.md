# Business Console 前端组件就绪计划

> **面向自主代理：**必须使用以下子技能之一逐项实施本计划：superpowers:subagent-driven-development（推荐）或 superpowers:executing-plans。步骤使用复选框（`- [ ]`）语法进行跟踪。

**目标：**将 #143 实施为 Business Console 页面的设计系统就绪工作，并以 `frontend/DESIGN` 作为权威规范。

**架构：**`frontend/packages/ui` 拥有基础组件和封装组件。应用页面只从 `@nerv-iip/ui` 导入。FileUpload 与 FileStorage 的公开 upload-session/tus/download-grant 契约交互，绝不直接访问 MinIO。

**技术栈：**Vue 3、Tailwind CSS v4、shadcn-vue、Reka UI、lucide 图标，以及用于断点续传的可选 Uppy core/headless + `@uppy/tus`。

---

## 规格

使用 `frontend/DESIGN/roadmaps/business-console-readiness.md` 作为设计系统契约。不得将本计划视为视觉事实来源。

## 任务 1：编码前更新 DESIGN

- [ ] **步骤 1：创建或更新组件文档**

实施前添加组件文档：

1. `frontend/DESIGN/components/tabs.md`
2. `frontend/DESIGN/components/sheet.md`
3. `frontend/DESIGN/components/date-picker.md`
4. `frontend/DESIGN/components/chart.md`
5. `frontend/DESIGN/components/file-upload.md`
6. `frontend/DESIGN/components/progress.md`
7. `frontend/DESIGN/components/scroll-area.md`

- [ ] **步骤 2：更新索引和待办清单**

更新 `frontend/DESIGN/index.md` 和 `frontend/DESIGN/components/install-backlog.md`，让后续代理能够看到哪些组件已安装、哪些仍待处理，以及每个组件由哪个工作流负责。

## 任务 2：安装并导出 shadcn-vue 基础组件

- [ ] **步骤 1：从 `frontend/` 安装基础组件**

运行：

```powershell
pnpm dlx shadcn-vue@latest add tabs sheet popover calendar range-calendar chart progress scroll-area
```

- [ ] **步骤 2：导出公开部件**

更新 `frontend/packages/ui/src/index.ts`，导出已安装基础组件的所有公开部件。

- [ ] **步骤 3：添加导出契约测试**

更新 `frontend/packages/ui/src/design-system.contract.test.ts`，或添加聚焦的导出测试，确保新基础组件由稳定的 `@nerv-iip/ui` 导出覆盖。

## 任务 3：构建 FileUpload 封装组件

- [ ] **步骤 1：添加传输抽象**

创建 FileUpload 传输边界，使其能够创建 FileStorage 上传会话，然后使用 server-proxy 或 tus 指令。

- [ ] **步骤 2：需要断点续传时添加 Uppy tus 适配器**

在 FileUpload 封装组件内部优先使用 Uppy core/headless 加 `@uppy/tus`。不得将 Uppy Dashboard 暴露为默认渲染外壳。

- [ ] **步骤 3：实施 shadcn 风格的 UI**

上传外壳使用现有的 `Button`、`Input`、`Progress`、`Alert`、`Badge`、`Empty`、`Spinner` 和 `Tooltip` 基础组件。

- [ ] **步骤 4：测试公开行为**

测试：

1. 接受和拒绝文件类型/大小的行为。
2. 上传进度和重试状态。
3. 完成后的输出包含 `fileId`。
4. 公开状态绝不暴露 `objectKey` 或对象存储直连 URL。

## 任务 4：只在需要时构建 Chart 和 Date 封装组件

- [ ] **步骤 1：Chart 基础组件**

先导出 shadcn-vue 图表基础组件。只有在业务页面出现重复需求后才添加领域封装组件，例如 KPI 迷你图、生产趋势或库存移动趋势。

- [ ] **步骤 2：组合日期选择器**

将 Popover + Calendar/RangeCalendar 组合成紧凑的日期/日期范围控件，适用于工具栏筛选器和表单。

## 任务 5：验证

- [ ] **步骤 1：运行前端质量门禁**

运行：

```powershell
pnpm -C frontend typecheck
pnpm -C frontend test
pnpm -C frontend build
```

预期：命令在受影响区域通过。

- [ ] **步骤 2：页面使用组件时运行聚焦视觉检查**

当任何应用页面使用这些基础组件时，使用 Playwright 验证桌面端和移动端截图，并检查文本溢出、控件重叠和焦点状态失效问题。

