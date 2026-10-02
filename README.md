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

需要观察过程的题目，可在计划草稿追加“正式题目练习”，选择该计划固定版本的题目。作答后在“证据与复习”查看原题和参考答案，逐项记录正确、错误、未知以及提示程度；先预览影响再确认。未观察的步骤不会被推断为正负证据。

也可从空白草稿添加能力、测量题、讲解资源和课时。未完成内容允许保存，审核发布前由服务端校验完整性；已有测量定义只读，改变测量含义须新增独立能力。

能力拆分、合并和替换可在“内容与发布”记录人工提案。先整理并发布新能力及测量题，再选择来源与生效版本、填写理由、审核并预览确认。来源能力停用，历史任务和结果保留；权重不复制或分摊掌握状态，学生内容版本由家长单独选择。

纸质错题可先保存图片和待校正正文，在“证据与复习”补齐并审核归因。原题未入库时可创建原题草稿，人工审核发布并绑定新内容版本；核对同一道原题、原始答案和判分依据后预览确认，才代录首次作答。使用本次确认时间，不推测历史练习间隔。已确认记录保留，后续通过判分更正修订。

目标可用阅读、听力和能力练习模板，设置指定星期、起止日期、每周次数与优先级。完成次数按学生当地实际完成日期统计；能力练习必须提交作答，阅读与听力只记录行为。目标编辑、暂停、恢复保留设置及修订历史；新计划采用新设置，旧任务保留生成时的目标快照。

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
python3 tests/observed_steps_api_acceptance.py
python3 tests/paper_learning_api_acceptance.py
python3 tests/goals_api_acceptance.py
python3 tests/family_api_acceptance.py
python3 tests/catalog_directory_api_acceptance.py
dotnet build tests/persistence
python3 tests/worker_fault_acceptance.py
python3 tests/goal_migration_acceptance.py
python3 tests/family_migration_acceptance.py
python3 tests/catalog_migration_acceptance.py
python3 tests/plan_boundaries_api_acceptance.py
python3 tests/plan_rule_migration_acceptance.py
python3 tests/knowledge_changes_api_acceptance.py
python3 tests/operations_api_acceptance.py
python3 tests/builder_retry_persistence_acceptance.py
python3 tests/builder_retry_migration_acceptance.py
python3 tests/builder_retry_api_acceptance.py
python3 tests/restore_acceptance.py
python3 tests/family_restore_acceptance.py
cd src/web && npm run test:e2e
```

常规接口验收需要服务已经启动，只创建验收家庭。计划边界、运行监控、建库重试和能力变更验收自行启动隔离服务；能力变更验收另需 GPG 做隔离加密恢复。计划边界、运行监控、建库重试、能力变更与迁移验收均需先构建 `tests/persistence`，使用一次性数据库并在结束后删除。恢复验收需要 PostgreSQL 客户端和 GPG，备份后恢复到单独的新库并在检查后移除该验收库。建议依次运行，避免认证限流影响并行测试。浏览器测试默认使用 Playwright 浏览器，可通过 `CHROMIUM_PATH` 指定已有 Chromium。

## 运行配置

`ConnectionStrings__Learning` 指定 PostgreSQL 连接。默认仅在本机 55432 端口访问 `learning` 数据库。服务默认监听本机，远程部署需另行配置 HTTPS、受限数据库账号和代理。

家长“家庭设置”可查看后台运行状态、当前家庭待处理结果及原记录重试入口；超过30秒或自动重试停止会提示，独立监控记本地日志。请求统计只覆盖本次服务启动后的分钟窗口，完整备份仍需另行配置。

后台每 500 毫秒读取同库 Outbox，在事务中整学生回放固定事件序列，创建新评估世代后原子切换活动绑定。历史世代保留用于审计，当前计算不双计。更正判分先预览影响，再明确确认。

来源只在本地处理。辅助建库新增运行记录与尝试历史；可重试异常最多重试三次，按2/4/8秒等待。持续失败后由内容编辑者填写依据重新处理原任务，原输入不变，成功不会重复生成。

PDF 限 10 MB、200 页，无文本层明确返回 `NEEDS_OCR`。Mock Builder 仅验证导入、来源和审核流程；不代表真实模型、Embedding 或映射质量。用户已指定模型稍后接入。生成时记录正式内容库快照；审核接受的候选保留原文位置与决定，并关联到创建的能力和草稿，发布后仍可查看来源。旧记录缺失的输入或来源不会推测补齐。

## 项目文档

- `docs/design/baseline-v1.1.md`：从设计会话取得的基线副本。
- `docs/STATUS.md`：实际进度和未通过项。
- `docs/runbooks/local.md`：备份、恢复、删除清单、运行与故障处理。
- `docs/adr`：实现决策。

V1 目标仍在进行中。真实教材核对、K1 模型质量评测、真实平板试用及连续四周家庭观察未被开发测试替代。

家庭设置支持负责人管理成员、全家 ZIP 导出及全家删除回执。编辑/发布角色与学生访问权限分开。恢复加密备份必须同时应用独立学生和家庭删除清单，详见本地运行手册。

内容编辑支持教材版本、单元、课程与课时目录，发布保留独立修订。目标可限定教材单元、课程或能力；听力课程只记录行为。旧内容中未知的教材版本与归属不会自动推断。

隔离建库恢复浏览器由 `RUN_BUILDER_BROWSER=1 python3 tests/builder_retry_api_acceptance.py` 调起；需要同前面的 `CHROMIUM_PATH` 配置。普通浏览器套件可用 `npm run test:e2e --prefix src/web -- --grep-invert 隔离建库失败恢复`，隔离用例只有在提供专门故障数据时执行。

本地性能复测：`RUN_PERFORMANCE_BROWSER=1 python3 tests/performance_acceptance.py`（需同上 `CHROMIUM_PATH`）。测量工具自行创建并删除隔离数据库和服务，记录真实作答与后台更新；模拟网络不等于实际平板/Wi-Fi。结果与方法见 `docs/verification/local-performance.json`、`docs/verification/local-performance.md`。读取一致性精确验收：`python3 tests/mastery_snapshot_acceptance.py`，运行前同样先构建 `tests/persistence`。普通浏览器不会执行缺少隔离性能夹具的用例。

到期备份工具：`python3 scripts/daily_backup.py --config <私有配置路径> --watch`。独立进程每分钟检查23小时到期条件，成功后执行30天受管保留；失败保留上次成功归档，口令不写入日志。使用 `python3 tests/daily_backup_acceptance.py` 验证真实加密、保留与新空库恢复（先构建 `tests/persistence`）。本机同盘开发备份已运行，独立磁盘、重启启动和家长状态仍待完成。配置与恢复说明见 `docs/runbooks/local.md`。
