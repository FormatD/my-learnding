import { defineConfig } from '@playwright/test';
export default defineConfig({ testDir: './tests', timeout: 60000, use: { baseURL: process.env.LEARNING_TEST_URL || 'http://127.0.0.1:5080', viewport: { width: 1024, height: 1366 }, launchOptions: { executablePath: process.env.CHROMIUM_PATH } }, reporter: 'list' });
