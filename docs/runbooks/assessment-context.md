# 评估上下文与历史依据

“证据与复习”的评估版本记录显示当前评估，以及最近50个历史评估的选择入口。每页20条，允许核对题目、实际判分、原发布、归因发布和映射修订。版本更正后原始作答保持不变；历史记录是该世代当时实际采用的依据，不代表它仍同时生效。

## 保存与切换

AssessmentContext按每世代、每作答保存实际GradingRevisionId、MappingSetRevisionId、QuestionRevisionId、ContentReleaseId、MappingReleaseId、EvidenceRuleVersion和CorrectionBatchId，并分别保留实际GradingCorrectionBatchId与MappingCorrectionBatchId。后续限额因更正而变化时，通用CorrectionBatchId指向本次重放原因，两个来源字段仍指向该作答实际采用的判分与映射更正。完整重建采用每次作答的最新有效判分及最后确认的映射更正；判分更正不替换原始答案，发布更好的映射也不自动重解释旧作答。

每条上下文明确MappingSource：FixedContainer使用实际固定容器；LegacySnapshot使用原发布快照，不猜测旧映射版本。AdmissionStatus为Pending、RetryExcluded、NoEvidence、ObservedSteps或Eligible。ObservedSteps仍只按已观察结果产生证据，未知步骤不产生正负证据；Eligible也不保证有正权重，重复窗口/提示等规则继续适用。重试不取代首次作答。

上下文版本字段固定；ActivationStatus跟随世代Shadow/Active/Retired切换。后台在同一家庭事务写新上下文、证据、掌握和复习，退休旧上下文并切换活动指针和Outbox回执。重复相同输入复用活动世代及原上下文身份，提交前崩溃不留下半套记录。当前仍是完整学生重放，不称作已完成独立增量算法。

唯一键包含GenerationId、AttemptId、GradingRevisionId、MappingSetRevisionId、EvidenceRuleVersion；额外唯一(GenerationId,AttemptId)保证当前完整世代内一条实际判分，也防止旧NULL映射绕过唯一性。Evidence.ContextId可空；新证据必须引用本次上下文，复合外键核对家庭、世代、学生、作答和判分。能力、观察点、方向的唯一性以ContextId/KCId/Part/Positive约束。

## 历史兼容与隐私

迁移只新增上下文表和可空Evidence.ContextId，不回填旧证据或补造历史上下文。旧世代有未记录上下文的证据时页面明确显示缺口。以后真实重放可为旧作答创建一个新世代的LegacySnapshot上下文；旧世代证据仍保持原样和NULL引用。

消费记录时区分有依据的现代引用与旧未记录形态。带明确QuestionRevisionId的会话/更正，如果对应发布有mapping-container/1记录，却缺少MappingSetRevisionId，则报422/MAPPING_SNAPSHOT_UNKNOWN；不会退回内嵌映射。原会话和生效更正都分别核对题目修订、容器、发布绑定及投影，有效的新更正不能掩盖错误原引用。会话复用、提示、提交、判分读取/预览和完整重放采用相同边界；失败的写操作在家庭事务内回滚。

旧会话两个引用均NULL时没有依据证明当时固定过容器，继续读取该会话原发布快照。原作答和更正的旧NULL不回填；新重放明确LegacySnapshot。只有映射指针存在时才按已记录的容器/绑定验证，不能自动追随最新发布。若题目修订和映射两项都没有留下记录，单靠NULL无法区分旧历史缺口与后来完全丢失的元数据，页面/导出保留未知，不声称能够恢复不存在的历史依据。

学生JSON导出新增assessmentContexts，保留当前和退休上下文及其实际映射引用；全家ZIP和加密备份包含完整私有表。导出及版本查询使用一致读取快照。删除学生/家庭级联删除上下文，旧备份恢复继续应用最新删除清单。JSON导出不代替数据库恢复。

## 查询接口

GET `/api/v1/students/{id}/assessment-contexts`：仅家长，孩子403，跨家庭学生404。generation可选，默认活动世代；明确指定的世代必须属于当前学生，未知/其他学生版本404。offset默认为0，必须非负；limit默认为50，范围1～100。返回所选generation、最近50个generations、分页contexts、total、legacyEvidenceCount、offset、limit。未有活动评估时generation为NULL、上下文为空。现有mastery详情保持原mastery/evidence结构，Evidence追加可空contextId。

## 验证入口

先完成前后端构建及持久测试项目构建，再执行：

```sh
.tools/dotnet/dotnet run --project tests/acceptance --no-restore
.tools/dotnet/dotnet build tests/persistence --no-restore
python3 tests/openapi_acceptance.py --regression
python3 tests/openapi_acceptance.py --paper-regression
python3 tests/assessment_context_migration_acceptance.py
python3 tests/worker_fault_acceptance.py
npm run test:e2e --prefix src/web -- assessment-context.spec.ts
```

接口/迁移/故障验收使用自动清理的临时数据库，浏览器用专门标记的验收家庭。合成记录、构造更正和临时恢复耗时不代替正式教材审核、实体设备完整一天或四周真实家庭使用。[证据撤销](evidence-revocations.md)已接入同一事务；[设计独立映射创建入口](independent-mappings.md)已提供待审创建/真实审核发布；本轮原始/生效学习引用的主要消费入口已统一核对；未知旧历史仍保留缺口。独立增量/全量比较与通用Job仍需继续开发。
