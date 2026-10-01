# 小步 · 家庭学习系统

本地私有的家庭学习应用，基于项目《家庭学习系统开发设计 v1.1》开发。首期适配北师大版三年级第一单元“混合运算”，含 9 个可测能力与 20 道原创样题。样题不是教材原题，使用前由家长审核。

## 启动

依赖：.NET 10 SDK、Node.js 20.19 或以上、PostgreSQL 16 或以上。依赖版本由项目和锁文件固定。当前开发机器的 SDK 与数据库位于项目内隔离目录。

```sh
sh scripts/dev.sh
```

浏览器打开 `http://127.0.0.1:5080`。首次使用创建私有家庭，没有预设管理密码。

1. 创建学生。
2. 内容与发布 → 创建混合运算样例 → 检查题干、答案、判分与测量能力 → 审核发布 → 绑定学生。
3. 进度与计划 → 确认课时 → 保存预算 → 生成草稿 → 确认发布。
4. 今日学习 → 进入孩子模式 → 开始任务、提交答案、完成。
5. 返回家长登录 → 查看证据、复习日程与每周回顾；开放题由家长判分。

也可从空白草稿添加能力、测量题、讲解资源和课时。未完成内容允许保存，审核发布前由服务端校验完整性；已有测量定义只读，改变测量含义须新增独立能力。

生成草稿不会影响已经发布的计划。重生成保留已完成、进行中、锁定和必做任务。所有写入有幂等标识，家庭隔离由服务端与复合外键执行。未审核内容不能进入正式作答。

## 验证

```sh
sh scripts/check.sh
python3 tests/api_acceptance.py
python3 tests/advanced_api_acceptance.py
python3 tests/boundary_api_acceptance.py
python3 tests/concurrency_api_acceptance.py
python3 tests/progress_api_acceptance.py
python3 tests/content_authoring_api_acceptance.py
python3 tests/provenance_api_acceptance.py
dotnet build tests/persistence
python3 tests/worker_fault_acceptance.py
python3 tests/restore_acceptance.py
cd src/web && npm run test:e2e
```

接口验收需要服务已经启动，只创建验收家庭。恢复验收需要 PostgreSQL 客户端和 GPG，备份后恢复到单独的新库并在检查后移除该验收库。建议依次运行，避免认证限流影响并行测试。浏览器测试默认使用 Playwright 浏览器，可通过 `CHROMIUM_PATH` 指定已有 Chromium。

## 运行配置

`ConnectionStrings__Learning` 指定 PostgreSQL 连接。默认仅在本机 55432 端口访问 `learning` 数据库。服务默认监听本机，远程部署需另行配置 HTTPS、受限数据库账号和代理。

后台每 500 毫秒读取同库 Outbox，在事务中整学生回放固定事件序列，创建新评估世代后原子切换活动绑定。历史世代保留用于审计，当前计算不双计。更正判分先预览影响，再明确确认。

来源只在本地处理。PDF 限 10 MB、200 页，无文本层明确返回 `NEEDS_OCR`。Mock Builder 仅验证导入、来源和审核流程；不代表真实模型、Embedding 或映射质量。用户已指定模型稍后接入。生成时记录正式内容库快照；审核接受的候选保留原文位置与决定，并关联到创建的能力和草稿，发布后仍可查看来源。旧记录缺失的输入或来源不会推测补齐。

## 项目文档

- `docs/design/baseline-v1.1.md`：从设计会话取得的基线副本。
- `docs/STATUS.md`：实际进度和未通过项。
- `docs/runbooks/local.md`：备份、恢复、删除清单、运行与故障处理。
- `docs/adr`：实现决策。

V1 目标仍在进行中。真实教材核对、K1 模型质量评测、真实平板试用及连续四周家庭观察未被开发测试替代。
