import type { Page } from '@playwright/test'
import { expect } from '@playwright/test'

/**
 * 走真实登录页 UI 登录（不注入 localStorage 会话——live 层必须走真实 IAM 认证路径）。
 *
 * 凭据只从环境变量读取（`NERV_IIP_LIVE_USER` / `NERV_IIP_LIVE_PASSWORD`），
 * 不硬编码任何默认密码；缺失时明确报错。
 *
 * 选择器依据 `src/pages/login.vue`：`aria-label="账号"` / `aria-label="密码"` 输入框 +
 * 「登录」提交按钮；成功后重定向到 `/`（工作台）。
 */
export async function loginViaUi(page: Page): Promise<void> {
  const user = process.env.NERV_IIP_LIVE_USER
  const password = process.env.NERV_IIP_LIVE_PASSWORD
  if (!user || !password) {
    throw new Error(
      '缺少 live 登录凭据：请设置环境变量 NERV_IIP_LIVE_USER 与 NERV_IIP_LIVE_PASSWORD' +
        '（本地栈 IAM admin 凭据，勿硬编码入仓）。',
    )
  }

  await page.goto('/login')
  await page.getByLabel('账号').fill(user)
  await page.getByLabel('密码').fill(password)
  await page.getByRole('button', { name: '登录' }).click()

  await expect(page).toHaveURL('/')
  await expect(page.getByRole('heading', { name: '工作台' })).toBeVisible()
}
