# 已确认更正与证据撤销

“证据与复习”的撤销记录按每页20条显示原证据、家长确认原因和更正批次。新判分或映射更正确认后，后台完整重放学生历史；在同一家庭事务内创建新评估、撤销记录、上下文、掌握、复习、活动指针和Outbox回执。原始作答及已保存的历史证据不改写。

## 撤销口径

世代退休不等于旧证据错误。只有新确认、原因已记录的Grading/Mapping批次触发撤销核对；相同结果再次判分或相同输入重建不增加撤销。Equivalent核对作答、稳定能力、观察点、正负方向、原始/有效权重及作答时间。权重按PostgreSQL numeric(16,6)实际精度、六位小数舍入比较，避免高精度重放值与数据库存储值不同导致误撤销。只换能力描述修订、评估身份或判分元数据，不算数学证据改变。

只比较同EvidenceRuleVersion和ModelVersion的历史世代。直接改动涉及的实际作答标记DirectCorrection；后续重复/同题型限额造成的证据变化标记ReplayDependency，并且须能对应本次原活动世代中确实变化的证据。无关的历史差异不归给新批次。各历史副本是不同Evidence行，每行最多追加一次撤销；不能把多副本条数当成独立错题数量。

多个批次合并处理时，先排除全部尚待处理的更正，并把已到达的所有作答与教学活动保留在每次核对中；再按CreatedAt、Id的确定顺序逐一纳入最终实际采用的更正。被后续判分或映射替代的中间批次不进入核对。只有某一步确实把原证据从等价变为不等价，才给该旧证据记录这一步的原因；若中途恢复等价、以后又变化，则记录最后一次离开等价的步骤。无关的较晚更正或同结果复核不冒领原因，也不把新到达作答造成的差异记为更正。

新证据部分关联最后一次实际产生该最终数学结果的步骤；上下文通用CorrectionBatchId概括该作答最后一次实际数学变化，两个明确的判分/映射来源仍保存实际采用的记录。因此撤销原因、新证据原因与上下文概括可能不同，各自指向其真实核对步骤，不表示某批次是所有联合效应的唯一解释。这里只证明确定序列下的变更追溯，不声称完成独立增量算法或最小因果集合分析。普通新作答或重试引发重放时，只为数学等价的已有证据继承原原因；不会用最新同结果复核覆盖实际原因。上下文判分/映射来源仍采用本次实际输入。临时核对结果不保存为额外世代，不改历史证据、撤销或确认记录。多批次时额外全学生核对随批次数增加，正式设备规模性能仍需验收。

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
python3 tests/multiple_correction_acceptance.py
npm run test:e2e --prefix src/web -- assessment-context.spec.ts
```

接口专项涵盖普通退休不撤销、首次错误更正导致直接/下游限额变化、六个历史副本撤销、等价再次判分/重建不重复、真实小数存储不误撤销、映射更正、导出及权限。合并更正专项在一个真实事务中固定同时待处理的多个判分/映射批次，核对无关更正、同结果复核、被替代结果和原始作答；不会靠等待后台碰巧合并来证明这一分支。故障专项用独立读者核对提交前无半套记录，实际终止工作进程后重试只提交一次。浏览器核对真实更正、原作答、历史上下文、撤销提示及390像素布局。构造测试家庭不代替教材人工核对或正式四周使用；完整学生重放不声称是独立增量算法。
