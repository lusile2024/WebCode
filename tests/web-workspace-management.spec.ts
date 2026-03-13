import { test, expect } from '@playwright/test';

test.describe('统一工作区管理功能测试', () => {
  // 测试配置
  const BASE_URL = 'http://localhost:5000';
  const USERNAME = 'luhaiyan';
  const PASSWORD = 'Lusile@0680';
  const SCREEN_SIZE = { width: 1200, height: 800 };

  test.beforeEach(async ({ page }) => {
    // 设置窗口大小
    await page.setViewportSize(SCREEN_SIZE);
    // 访问登录页
    await page.goto(`${BASE_URL}/login`);
    // 登录
    await page.fill('input[name="username"]', USERNAME);
    await page.fill('input[name="password"]', PASSWORD);
    await page.click('button[type="submit"]');
    // 等待跳转到代码助手页面
    await page.waitForURL('**/code-assistant', { timeout: 10000 });
    await page.waitForLoadState('networkidle');
  });

  test('测试页面加载和基本元素显示', async ({ page }) => {
    // 检查页面标题
    await expect(page).toHaveTitle(/WebCode/);
    // 检查顶部用户信息区域
    await expect(page.locator('.user-info-panel')).toBeVisible();
    // 检查工作区切换器显示
    await expect(page.locator('text="工作目录"').first()).toBeVisible();
    // 检查左侧活动栏
    await expect(page.locator('.activity-bar')).toBeVisible();
  });

  test('测试新建会话弹窗功能', async ({ page }) => {
    // 点击活动栏的会话按钮
    await page.click('[data-menu-item="sessions"]');
    // 点击新建会话按钮
    await page.click('button:has-text("新建会话")');
    // 检查弹窗是否出现
    await expect(page.locator('.modal-title:text("新建会话")')).toBeVisible();

    // 检查两个Tab是否存在
    await expect(page.locator('role="tab":has-text("选择已有目录")')).toBeVisible();
    await expect(page.locator('role="tab":has-text("自定义路径")')).toBeVisible();

    // 切换到自定义路径Tab
    await page.click('role="tab":has-text("自定义路径")');
    await expect(page.locator('text="使用默认目录"')).toBeVisible();
    await expect(page.locator('text="自定义工作路径"')).toBeVisible();

    // 关闭弹窗
    await page.click('button[aria-label="Close"]');
    await expect(page.locator('.modal-title:text("新建会话")')).toBeHidden();
  });

  test('测试工作区切换器功能', async ({ page }) => {
    // 点击工作区切换器
    await page.click('text="工作目录"').first();
    // 检查下拉菜单是否出现
    await expect(page.locator('text="切换工作目录"')).toBeVisible();
    // 检查搜索框是否存在
    await expect(page.locator('input[placeholder="搜索目录..."]')).toBeVisible();

    // 测试搜索功能
    await page.fill('input[placeholder="搜索目录..."]', '测试');
    // 关闭下拉菜单
    await page.click('body', { position: { x: 10, y: 10 } });
  });

  test('测试会话列表目录信息显示', async ({ page }) => {
    // 打开会话侧边栏
    await page.click('[data-menu-item="sessions"]');
    // 检查会话列表是否显示
    await expect(page.locator('.session-list-panel')).toBeVisible();

    // 检查会话项是否显示目录信息
    const sessionItems = page.locator('.session-item');
    if (await sessionItems.count() > 0) {
      // 至少有一个会话时，检查是否有目录相关信息
      const firstSession = sessionItems.first();
      await expect(firstSession.locator('.directory-name')).toBeVisible();
      await expect(firstSession.locator('.directory-path')).toBeVisible();
    }
  });

  test('测试目录授权模态框', async ({ page }) => {
    // 打开会话侧边栏
    await page.click('[data-menu-item="sessions"]');

    // 如果有会话，点击授权按钮
    const shareButtons = page.locator('button[title="分享会话"]');
    if (await shareButtons.count() > 0) {
      await shareButtons.first().click();
      // 检查授权模态框是否出现
      await expect(page.locator('text="目录授权管理"')).toBeVisible();
      // 关闭模态框
      await page.click('button[aria-label="Close"]');
    }
  });

  test('测试切换工作目录功能', async ({ page }) => {
    // 点击工作区切换器
    await page.click('text="工作目录"').first();

    // 如果有目录列表
    const directoryItems = page.locator('.directory-item');
    if (await directoryItems.count() > 0) {
      // 切换到第一个非当前目录
      const firstDir = directoryItems.first();
      const isCurrent = await firstDir.locator('text="当前"').count() > 0;

      if (!isCurrent) {
        await firstDir.click();
        await page.click('button:has-text("切换目录")');
        // 等待切换完成
        await page.waitForLoadState('networkidle');
        // 检查切换是否成功
        await expect(page.locator('.notification-success:text("切换成功")')).toBeVisible();
      }
    }
  });

  test('测试Git项目导入功能', async ({ page }) => {
    const TEST_GIT_URL = 'https://gh.llkk.cc/https://github.com/lusile2024/skills-hub.git';

    // 打开项目管理页面
    await page.click('[data-menu-item="projects"]');
    await page.waitForLoadState('networkidle');

    // 点击导入Git项目按钮
    await page.click('button:has-text("导入Git项目")');
    await expect(page.locator('text="导入Git仓库"')).toBeVisible();

    // 填写Git仓库地址
    await page.fill('input[placeholder="请输入Git仓库地址"]', TEST_GIT_URL);

    // 填写项目名称（自动生成）
    const projectNameInput = page.locator('input[placeholder="项目名称"]');
    const projectName = await projectNameInput.inputValue();
    expect(projectName).not.toBeEmpty();

    // 点击确认导入
    await page.click('button:has-text("开始导入")');

    // 等待导入完成（最长等待30秒）
    await page.waitForSelector('text="导入成功"', { timeout: 30000 });
    await expect(page.locator('text="导入成功"')).toBeVisible();

    // 返回新建会话页面
    await page.click('[data-menu-item="sessions"]');
    await page.click('button:has-text("新建会话")');

    // 切换到"选择已有目录"Tab
    await page.click('role="tab":has-text("选择已有目录")');

    // 检查导入的项目是否在目录列表中
    await expect(page.locator(`text="${projectName}"`)).toBeVisible();

    // 选择导入的项目目录
    await page.click(`text="${projectName}"`);

    // 确认创建会话
    await page.click('button:has-text("创建会话")');
    await page.waitForLoadState('networkidle');

    // 检查会话是否成功创建并关联到对应目录
    await expect(page.locator('text="工作目录"').first()).toBeVisible();
    await page.click('text="工作目录"').first();
    await expect(page.locator(`text="${projectName}"`)).toBeVisible();
  });
});
