# Page: Login Page（登录页）

双栏认证页。左：品牌面板（`bg-primary`，`lg` 以下隐藏）；右：登录表单卡。
两个 console 同构。

> 历史修正：本文早期版本描述的 `<style scoped>` CSS Grid + `clamp()` 布局已被
> 纯 Tailwind 双栏实现取代（现网两处登录页均无 scoped 样式），「登录页是
> Tailwind-only 规则的例外」的旧说法作废。

## 规则

1. **布局**：`<main class="grid min-h-svh lg:grid-cols-2">`。左栏品牌面板
   `hidden … lg:flex`（`bg-primary` + 点阵纹理 + 品牌名/标语，文案走 i18n
   `t('app.brand')` 等）；右栏居中放 `LoginForm`，窄屏时顶部显示小号品牌行
   （`lg:hidden`）。
2. **无外壳**：登录页不渲染顶栏/侧栏 shell（不裹 `DefaultLayout`/`BusinessLayout`）。
3. **路由元**：`definePage({ meta: { guestOnly: true, title: 'routes.login' } })`——
   已登录访问即重定向。
4. **重定向消毒**：`?redirect=` 必须过 `sanitizeRedirectPath()`（`@/router/redirects`）
   再 `router.push`，绝不直接跳任意 URL。
5. **认证错误呈现**：`LoginForm` 的 `error` prop → 字段上方 `Alert variant="destructive"`
   （原版 Alert 即现役，无 Nv 版），同时两个输入框 `:data-invalid` 标红；`pending` 时
   禁用表单、按钮内 Spinner。
6. **不加**社交登录按钮、找回密码链接——认证流支持前不做假入口。
7. 登录卡片视觉规格（ring 卡片等）见设计走查结论，改样式前先看现网实现，不自创。

## 判定

- 「已登录用户访问 `/login` 会被弹回吗？」（`guestOnly` 生效）
- 「`?redirect=//evil.com` 会跳出去吗？」会 → 打回（没过 `sanitizeRedirectPath`）。
- 「登录失败的错误是内联 Alert + 字段标红，还是 toast/常驻页面文字？」

## 正例

现网实现（两处同构，照抄起点）：

- `apps/business-console/src/pages/login.vue` + `src/components/auth/LoginForm.vue`
- `apps/console/src/pages/login.vue` + `src/components/auth/LoginForm.vue`

`LoginForm` 接口：

```ts
// props
error?: string    // 在字段上方渲染 Alert variant="destructive"，并标红两个输入
pending?: boolean // 禁用表单，登录按钮内 Spinner
// emits
submit: { loginName: string, password: string }
```

页面骨架（摘自现网 `login.vue`）：

```vue
<template>
  <main class="grid min-h-svh lg:grid-cols-2">
    <div
      class="relative hidden overflow-hidden bg-primary lg:flex lg:flex-col lg:items-center lg:justify-center"
    >
      <!-- 点阵纹理 + 渐变遮罩 + 品牌名/标语（i18n） -->
    </div>
    <div class="flex flex-col items-center justify-center p-6 md:p-10">
      <div class="flex w-full max-w-sm flex-col gap-6">
        <!-- lg 以下的小号品牌行 -->
        <LoginForm :error="authError" :pending="pending" @submit="submit" />
      </div>
    </div>
  </main>
</template>
```

<!-- 反例：暂无现网证据（2026-07-11 走查未涉登录页问题）。 -->
