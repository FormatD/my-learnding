# 已确认更正与证据撤销

“证据与复习”的撤销记录按每页20条显示原证据、家长确认原因和更正批次。新判分或映射更正确认后，后台完整重放学生历史；在同一家庭事务内创建新评估、撤销记录、上下文、掌握、复习、活动指针和Outbox回执。原始作答及已保存的历史证据不改写。

## 撤销口径

世代退休不等于旧证据错误。只有新确认、原因已记录的Grading/Mapping批次触发撤销核对；相同结果再次判分或相同输入重建不增加撤销。Equivalent核对作答、稳定能力、观察点、正负方向、原始/有效权重及作答时间。权重按PostgreSQL numeric(16,6)实际精度、六位小数舍入比较，避免高精度重放值与数据库存储值不同导致误撤销。只换能力描述修订、评估身份或判分元数据，不算数学证据改变。

只比较同EvidenceRuleVersion和ModelVersion的历史世代。直接改动涉及的实际作答标记DirectCorrection；后续重复/同题型限额造成的证据变化标记ReplayDependency，并且须能对应本次原活动世代中确实变化的证据。无关的历史差异不归给新批次。各历史副本是不同Evidence行，每行最多追加一次撤销；不能把多副本条数当成独立错题数量。

CorrectionBatch记录实际Cause、AffectedAttemptIds、原判分SourceGradingRevisionId（若适用）、确认人、原因及预览依据。后台实际采用的批次从Confirmed变为Applied；未被新上下文采用的尚待处理批次变为Superseded。Applied表示实际采用，不表示一定有错误证据可撤销：首次确认Pending判分、等价重审均可能没有撤销。此前已Applied的历史批次保留原状态。

AssessmentContext分别保留GradingCorrectionBatchId和MappingCorrectionBatchId；通用CorrectionBatchId在后续证据受影响时表示此次重放的原因。两个明确来源字段不被下游原因替换。EvidenceRevocation记录实际原EvidenceId、CorrectionBatchId、ReplacementGenerationId、Reason及Effect；唯一键限制EvidenceId重复，家庭外键关联原证据、批次、学生及替换世代。

## 历史、查询与数据控制

迁移20261002204255仅增加可空来源字段和新撤销表，不填写历史批次Cause，不制造旧撤销或更正确认。旧来源不明、未知规则/模型间的差异不推断。旧AssessmentContext/Evidence数据保持原字段值；旧库以后实际重放产生的新世代记录与旧记录并存。

GET `/api/v1/students/{id}/evidence-revocations`仅家长，孩子403、其他家庭学生404。offset默认为0且非负；limit默认为20，范围1～100。RepeatableRead返回分页revocations及其实际evidence、batches、total、offset、limit。没有撤销时返回空集合。家长页面显示原证据方向/权重及直接/后续影响，不把所有退休证据叫错误。

学生JSON导出包含evidenceRevocations及对应历史证据/批次；全家ZIP和加密数据库备份包含新表。学生/家庭删除级联删除相应记录，旧备份恢复应用最新删除清单。数据库恢复演练不代替独立物理盘、持续RPO和正式灾难应用切换。

## 验证

完成服务和持久验收项目构建后执行：

```sh
.tools/dotnet/dotnet run --project tests/acceptance --no-restore
python3 tests/openapi_acceptance.py --regression
python3 tests/openapi_acceptance.py --paper-regression
python3 tests/assessment_context_migration_acceptance.py
python3 tests/evidence_revocation_fault_acceptance.py
npm run test:e2e --prefix src/web -- assessment-context.spec.ts
```

接口专项涵盖普通退休不撤销、首次错误更正导致直接/下游限额变化、六个历史副本撤销、等价再次判分/重建不重复、真实小数存储不误撤销、映射更正、导出及权限。故障专项用独立读者核对提交前无半套记录，实际终止工作进程后重试只提交一次。浏览器核对真实更正、原作答、历史上下文、撤销提示及390像素布局。构造测试家庭不代替教材人工核对或正式四周使用；完整学生重放不声称是独立增量算法。
