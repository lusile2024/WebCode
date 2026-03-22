#!/usr/bin/env node

const { spawn } = require('child_process');
const path = require('path');

console.log('🚀 启动 Playwright 测试...');

// 测试配置
const testFiles = [
  'tests/web-workspace-management-unified.spec.ts',
  'tests/web-workspace-management.spec.ts',
  'tests/workspace-authorization.spec.ts'
];

let currentTestIndex = 0;

function runTest(testFile) {
  return new Promise((resolve, reject) => {
    console.log(`\n📋 运行测试文件: ${testFile}`);

    const process = spawn('npx', [
      'playwright',
      'test',
      testFile,
      '--headed',
      '--project=chromium',
      '--timeout=120000'
    ], {
      cwd: __dirname,
      stdio: 'inherit'
    });

    process.on('close', (code) => {
      if (code === 0) {
        console.log(`✅ 测试文件 ${testFile} 通过`);
        resolve();
      } else {
        console.log(`❌ 测试文件 ${testFile} 失败`);
        reject(new Error(`测试失败，退出码: ${code}`));
      }
    });

    process.on('error', (error) => {
      console.error(`❌ 运行测试时出错: ${error.message}`);
      reject(error);
    });
  });
}

async function runAllTests() {
  try {
    console.log('🎯 开始运行统一工作区功能测试套件...\n');

    for (const testFile of testFiles) {
      await runTest(testFile);
    }

    console.log('\n🎉 所有测试已完成！');
  } catch (error) {
    console.error('\n💥 测试执行过程中出现错误:', error.message);
    process.exit(1);
  }
}

// 如果直接运行此脚本
if (require.main === module) {
  runAllTests();
}

module.exports = { runTest, runAllTests };