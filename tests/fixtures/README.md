# 可重放的交接样例

这些文件只含原创流程题和合成记录，没有真实学生、账号、口令或私有附件。它们对应设计§16.5交接要求，不是教材原题、正式内容质量金标或家庭试用记录。

- `minimal-unit.json`：由现有 `Content.Fixture()` 导出并固定保存的20题、9能力、资源和教材/单元目录快照。具体印次仍未知；身份随此文件固定，不在每次测试时重新生成。
- `replay-cases.json`：五个独立预期案例，覆盖D0/D2/D7/D30、迟到按实际通过日排期、首次判分更正与重试隔离、Pending首次答案、同一时间的两个已提交并发作答序列。日期是合成的，不代表实际学习间隔。

案例的 `sequence` 是提交后的稳定重放顺序，不用时间戳打破并发排序；`sessionKey` 固定同次遇题，`attemptNumber` 区分首次和重试。`gradingRevisions` 只声明有效判分选择，最后一个为当前解释，不能覆盖原始答案。预期的Alpha/Beta、证据数、阶段和到期日期单独保存在文件中；验收器按这些预期核对，并反转输入顺序再次验证确定性。

这些输入直接测试证据和日程重放，不经过API判分/持久化。并发案例表示提交后的序列，不能单独证明事务竞态正确；实际并发写入见 `tests/concurrency_api_acceptance.py`、消费者竞态/崩溃见 `tests/worker_fault_acceptance.py`。判分API及更正版本另由相关集成检查验证。

运行 `dotnet run --project tests/acceptance`，或项目常规 `sh scripts/check.sh`。项目会把JSON复制到验收程序的 `Fixtures` 目录，避免依赖当前工作目录；缺失、内容引用错误、预期不符都会失败。不要把这些案例写入实际家庭数据库。

更新最小单元时可显式运行 `dotnet run --project tests/acceptance -- --export-flow-fixture <临时路径>`，先核对身份/定义、案例引用和预期，再替换快照并检查。导出会创建新的样例身份，不得将其冒充旧快照的同一发布或默默覆盖正式内容。160题单元草稿另由 `--export-unit-pack` 导出，不能替代本目录的最小回放内容。
