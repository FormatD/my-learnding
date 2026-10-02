# 实际数据库数据字典

由 `scripts/schema_dictionary.py` 从 PostgreSQL public 目录的只读事务生成。仅包含结构及迁移版本，不包含家庭记录、来源正文、附件、口令或连接配置。

当前 57 张表；列类型、数据库默认值、主键、外键删除规则、唯一性及索引均以实际数据库为准。对应机器可读快照：[schema.json](data/schema.json)。

## 使用边界

- `FamilyId` 是家庭范围，外键约束之外仍必须通过服务端授权；内容快照中的 UUID 不等于独立数据库外键。
- `Id` 是稳定行身份；`CreatedAt` 是创建时间。应用初始化值不等于数据库默认值；下表的“无”表示数据库没有默认表达式。
- `Version`、修订号、事件序号和家庭 ETag 各有作用，不能互换。身份列按数据库定义生成。
- 目录和部分状态存于 text JSON 快照；能力、题目、课时、映射的嵌套字段见 `src/server/Domain/Models.cs` 的 Catalog 等记录类型。此字典不虚构独立实体表。
- 当前投影通过学生活动世代读取；保留历史世代不表示其证据同时生效。更正追加新记录，原始作答与领取版本保留。
- 全家 ZIP 与私有恢复有专用范围和删除清单规则；不要用此结构清单替代隐私导出/恢复工具。

## 已应用迁移

| 迁移 | EF 版本 |
|---|---|
| 20261001170036_Initial | 10.0.4 |
| 20261001171031_ContentIdentityAndPrivacy | 10.0.4 |
| 20261001171408_PrivateFiles | 10.0.4 |
| 20261001172958_RetrievalAndAliases | 10.0.4 |
| 20261001173351_BackgroundParsingAndRetries | 10.0.4 |
| 20261001173636_HistoricalMappingCorrections | 10.0.4 |
| 20261001173842_RetrySessionCookies | 10.0.4 |
| 20261001174922_TaskLearningAnchors | 10.0.4 |
| 20261001175553_TaskDuration | 10.0.4 |
| 20261001180259_ResourceLinksAndModelVersion | 10.0.4 |
| 20261001184723_AuditedProgressCorrections | 10.0.4 |
| 20261001185312_LegacyModelVersionMetadata | 10.0.4 |
| 20261001193034_BuilderInputAndProvenance | 10.0.4 |
| 20261001201322_ConfirmedPaperResults | 10.0.4 |
| 20261001203019_ScheduledLearningGoals | 10.0.4 |
| 20261002014148_GoalLegacyDefaults | 10.0.4 |
| 20261002022950_FamilyMemberships | 10.0.4 |
| 20261002025728_CatalogGoalScopes | 10.0.4 |
| 20261002035824_PlanRuleVersion | 10.0.4 |
| 20261002041538_KnowledgeChangeProposals | 10.0.4 |
| 20261002050245_BuilderRetryAttempts | 10.0.4 |
| 20261002095324_ParentBurdenRecords | 10.0.4 |
| 20261002105257_StudentScopedAudit | 10.0.4 |
| 20261002171057_MappingSuggestionReview | 10.0.4 |
| 20261002184306_ContentReviewSnapshots | 10.0.4 |
| 20261002190022_PublishedMappingContainers | 10.0.4 |
| 20261002191928_LearningMappingReferences | 10.0.4 |
| 20261002201644_AssessmentContexts | 10.0.4 |
| 20261002204255_EvidenceRevocations | 10.0.4 |
| 20261002211632_IndependentMappingDrafts | 10.0.4 |
| 20261002222837_BuilderStructuredProtocol | 10.0.4 |
| 20261002224110_BuilderFrozenConfiguration | 10.0.4 |
| 20261002225131_BuilderCallLedger | 10.0.4 |

## Accounts

家长账号；口令仅保存哈希。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| UserName | text | 否 | 无 |
| PasswordHash | text | 否 | 无 |
| Roles | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_Accounts_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Accounts_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Accounts`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Accounts_FamilyId_Id" ON public."Accounts" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Accounts_FamilyId" ON public."Accounts" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_Accounts_UserName" ON public."Accounts" USING btree ("UserName")`
- `CREATE UNIQUE INDEX "PK_Accounts" ON public."Accounts" USING btree ("Id")`

## Alias

人工审核的能力别名。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| KCId | uuid | 否 | 无 |
| CandidateId | uuid | 否 | 无 |
| Text | text | 否 | 无 |
| Normalized | text | 否 | 无 |
| ReviewedBy | uuid | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_Alias_Candidates_FamilyId_CandidateId`：`FOREIGN KEY ("FamilyId", "CandidateId") REFERENCES "Candidates"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Alias_ContentIdentity_FamilyId_KCId`：`FOREIGN KEY ("FamilyId", "KCId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Alias_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Alias`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Alias_FamilyId" ON public."Alias" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Alias_FamilyId_CandidateId" ON public."Alias" USING btree ("FamilyId", "CandidateId")`
- `CREATE UNIQUE INDEX "IX_Alias_FamilyId_KCId_Normalized" ON public."Alias" USING btree ("FamilyId", "KCId", "Normalized")`
- `CREATE UNIQUE INDEX "PK_Alias" ON public."Alias" USING btree ("Id")`

## AssessmentContext

每世代每作答的固定判分、映射、规则和准入状态；历史缺失不回填。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| GenerationId | uuid | 否 | 无 |
| AttemptId | uuid | 否 | 无 |
| GradingRevisionId | uuid | 否 | 无 |
| MappingSetRevisionId | uuid | 是 | 无 |
| QuestionRevisionId | uuid | 否 | 无 |
| ContentReleaseId | uuid | 否 | 无 |
| MappingReleaseId | uuid | 否 | 无 |
| CorrectionBatchId | uuid | 是 | 无 |
| EvidenceRuleVersion | text | 否 | 无 |
| ActivationStatus | text | 否 | 无 |
| MappingSource | text | 否 | 无 |
| EvidencePolicy | text | 否 | 无 |
| AdmissionStatus | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| GradingCorrectionBatchId | uuid | 是 | 无 |
| MappingCorrectionBatchId | uuid | 是 | 无 |

约束：

- `AK_AssessmentContext_FamilyId_Id_GenerationId_StudentId_Attemp~`：`UNIQUE ("FamilyId", "Id", "GenerationId", "StudentId", "AttemptId", "GradingRevisionId")`
- `FK_AssessmentContext_Attempts_FamilyId_AttemptId`：`FOREIGN KEY ("FamilyId", "AttemptId") REFERENCES "Attempts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_ContentRevision_FamilyId_QuestionRevision~`：`FOREIGN KEY ("FamilyId", "QuestionRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_CorrectionBatch_FamilyId_CorrectionBatchId`：`FOREIGN KEY ("FamilyId", "CorrectionBatchId") REFERENCES "CorrectionBatch"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_CorrectionBatch_FamilyId_GradingCorrectio~`：`FOREIGN KEY ("FamilyId", "GradingCorrectionBatchId") REFERENCES "CorrectionBatch"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_CorrectionBatch_FamilyId_MappingCorrectio~`：`FOREIGN KEY ("FamilyId", "MappingCorrectionBatchId") REFERENCES "CorrectionBatch"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_AssessmentContext_Generations_FamilyId_GenerationId`：`FOREIGN KEY ("FamilyId", "GenerationId") REFERENCES "Generations"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_Gradings_FamilyId_GradingRevisionId`：`FOREIGN KEY ("FamilyId", "GradingRevisionId") REFERENCES "Gradings"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_MappingSetRevision_FamilyId_MappingSetRev~`：`FOREIGN KEY ("FamilyId", "MappingSetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_Releases_FamilyId_ContentReleaseId`：`FOREIGN KEY ("FamilyId", "ContentReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_Releases_FamilyId_MappingReleaseId`：`FOREIGN KEY ("FamilyId", "MappingReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_AssessmentContext_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_AssessmentContext`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_AssessmentContext_FamilyId_Id_GenerationId_StudentId_Attemp~" ON public."AssessmentContext" USING btree ("FamilyId", "Id", "GenerationId", "StudentId", "AttemptId", "GradingRevisionId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId" ON public."AssessmentContext" USING btree ("FamilyId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_AttemptId" ON public."AssessmentContext" USING btree ("FamilyId", "AttemptId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_ContentReleaseId" ON public."AssessmentContext" USING btree ("FamilyId", "ContentReleaseId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_CorrectionBatchId" ON public."AssessmentContext" USING btree ("FamilyId", "CorrectionBatchId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_GenerationId" ON public."AssessmentContext" USING btree ("FamilyId", "GenerationId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_GradingCorrectionBatchId" ON public."AssessmentContext" USING btree ("FamilyId", "GradingCorrectionBatchId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_GradingRevisionId" ON public."AssessmentContext" USING btree ("FamilyId", "GradingRevisionId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_MappingCorrectionBatchId" ON public."AssessmentContext" USING btree ("FamilyId", "MappingCorrectionBatchId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_MappingReleaseId" ON public."AssessmentContext" USING btree ("FamilyId", "MappingReleaseId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_MappingSetRevisionId" ON public."AssessmentContext" USING btree ("FamilyId", "MappingSetRevisionId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_QuestionRevisionId" ON public."AssessmentContext" USING btree ("FamilyId", "QuestionRevisionId")`
- `CREATE INDEX "IX_AssessmentContext_FamilyId_StudentId" ON public."AssessmentContext" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_AssessmentContext_GenerationId_AttemptId" ON public."AssessmentContext" USING btree ("GenerationId", "AttemptId")`
- `CREATE UNIQUE INDEX "IX_AssessmentContext_GenerationId_AttemptId_GradingRevisionId_~" ON public."AssessmentContext" USING btree ("GenerationId", "AttemptId", "GradingRevisionId", "MappingSetRevisionId", "EvidenceRuleVersion")`
- `CREATE UNIQUE INDEX "PK_AssessmentContext" ON public."AssessmentContext" USING btree ("Id")`

## Attempts

原始作答及不可覆盖的提交身份、全局事件序号。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| SessionId | uuid | 否 | 无 |
| ClientSubmissionId | uuid | 否 | 无 |
| Number | integer | 否 | 无 |
| Sequence | bigint | 否 | IDENTITY ALWAYS |
| Answer | text | 否 | 无 |
| AnswerSource | text | 否 | 无 |
| HintLevel | integer | 否 | 无 |
| AnswerShown | boolean | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| MappingSetRevisionId | uuid | 是 | 无 |
| QuestionRevisionId | uuid | 是 | 无 |

约束：

- `AK_Attempts_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Attempts_ContentRevision_FamilyId_QuestionRevisionId`：`FOREIGN KEY ("FamilyId", "QuestionRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Attempts_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Attempts_MappingSetRevision_FamilyId_MappingSetRevisionId`：`FOREIGN KEY ("FamilyId", "MappingSetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Attempts_Sessions_FamilyId_SessionId`：`FOREIGN KEY ("FamilyId", "SessionId") REFERENCES "Sessions"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Attempts_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Attempts`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Attempts_FamilyId_Id" ON public."Attempts" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Attempts_FamilyId" ON public."Attempts" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Attempts_FamilyId_MappingSetRevisionId" ON public."Attempts" USING btree ("FamilyId", "MappingSetRevisionId")`
- `CREATE INDEX "IX_Attempts_FamilyId_QuestionRevisionId" ON public."Attempts" USING btree ("FamilyId", "QuestionRevisionId")`
- `CREATE INDEX "IX_Attempts_FamilyId_SessionId" ON public."Attempts" USING btree ("FamilyId", "SessionId")`
- `CREATE INDEX "IX_Attempts_FamilyId_StudentId" ON public."Attempts" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Attempts_SessionId_Number" ON public."Attempts" USING btree ("SessionId", "Number")`
- `CREATE UNIQUE INDEX "IX_Attempts_StudentId_ClientSubmissionId" ON public."Attempts" USING btree ("StudentId", "ClientSubmissionId")`
- `CREATE UNIQUE INDEX "PK_Attempts" ON public."Attempts" USING btree ("Id")`

## Audits

操作审计；StudentId 为空的旧摘要不推定学生归属。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ActorId | uuid | 否 | 无 |
| Action | text | 否 | 无 |
| Details | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| StudentId | uuid | 是 | 无 |

约束：

- `FK_Audits_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Audits_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Audits`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Audits_FamilyId" ON public."Audits" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Audits_FamilyId_StudentId" ON public."Audits" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_Audits" ON public."Audits" USING btree ("Id")`

## AuthSessions

家长/孩子认证会话；仅存令牌哈希，恢复时清除。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| AccountId | uuid | 是 | 无 |
| StudentId | uuid | 是 | 无 |
| Role | text | 否 | 无 |
| TokenHash | text | 否 | 无 |
| ExpiresAt | timestamp with time zone | 否 | 无 |
| Revoked | boolean | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_AuthSessions_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_AuthSessions`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_AuthSessions_FamilyId" ON public."AuthSessions" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_AuthSessions_TokenHash" ON public."AuthSessions" USING btree ("TokenHash")`
- `CREATE UNIQUE INDEX "PK_AuthSessions" ON public."AuthSessions" USING btree ("Id")`

## Availabilities

学生日期学习预算与学校作业预留。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| Date | date | 否 | 无 |
| Minutes | integer | 否 | 无 |
| Reserved | integer | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_Availabilities_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Availabilities_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Availabilities`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Availabilities_FamilyId" ON public."Availabilities" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Availabilities_FamilyId_StudentId" ON public."Availabilities" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Availabilities_StudentId_Date" ON public."Availabilities" USING btree ("StudentId", "Date")`
- `CREATE UNIQUE INDEX "PK_Availabilities" ON public."Availabilities" USING btree ("Id")`

## BuilderAttempt

建库运行每次处理的状态、错误与重试轮次。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| RunId | uuid | 否 | 无 |
| RetryRound | integer | 否 | 无 |
| Number | integer | 否 | 无 |
| StartedAt | timestamp with time zone | 否 | 无 |
| FinishedAt | timestamp with time zone | 否 | 无 |
| Status | text | 否 | 无 |
| ErrorCode | text | 是 | 无 |
| NextAttemptAt | timestamp with time zone | 是 | 无 |
| InputSnapshot | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| ProtocolResult | text | 是 | 无 |

约束：

- `FK_BuilderAttempt_BuilderRuns_FamilyId_RunId`：`FOREIGN KEY ("FamilyId", "RunId") REFERENCES "BuilderRuns"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_BuilderAttempt_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_BuilderAttempt`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_BuilderAttempt_FamilyId" ON public."BuilderAttempt" USING btree ("FamilyId")`
- `CREATE INDEX "IX_BuilderAttempt_FamilyId_RunId" ON public."BuilderAttempt" USING btree ("FamilyId", "RunId")`
- `CREATE UNIQUE INDEX "IX_BuilderAttempt_RunId_RetryRound_Number" ON public."BuilderAttempt" USING btree ("RunId", "RetryRound", "Number")`
- `CREATE UNIQUE INDEX "PK_BuilderAttempt" ON public."BuilderAttempt" USING btree ("Id")`

## BuilderCall

独立提交的逐次调用事实、执行身份、实际用量/费用或未知状态；候选回滚不删除，旧历史不补造。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| RunId | uuid | 否 | 无 |
| ExecutionId | uuid | 否 | 无 |
| RetryRound | integer | 否 | 无 |
| AttemptNumber | integer | 否 | 无 |
| CallNumber | integer | 否 | 无 |
| Repair | boolean | 否 | 无 |
| Provider | text | 否 | 无 |
| Model | text | 否 | 无 |
| InputHash | text | 否 | 无 |
| ModelConfigHash | text | 是 | 无 |
| Status | text | 否 | 无 |
| StartedAt | timestamp with time zone | 否 | 无 |
| FinishedAt | timestamp with time zone | 是 | 无 |
| ElapsedMilliseconds | bigint | 是 | 无 |
| InputTokens | bigint | 是 | 无 |
| OutputTokens | bigint | 是 | 无 |
| ChargedCost | numeric(16,6) | 是 | 无 |
| Currency | text | 是 | 无 |
| BillingStatus | text | 否 | 无 |
| OutputHash | text | 是 | 无 |
| ErrorCode | text | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_BuilderCall_BuilderRuns_FamilyId_RunId`：`FOREIGN KEY ("FamilyId", "RunId") REFERENCES "BuilderRuns"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_BuilderCall_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_BuilderCall`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "IX_BuilderCall_ExecutionId_CallNumber" ON public."BuilderCall" USING btree ("ExecutionId", "CallNumber")`
- `CREATE INDEX "IX_BuilderCall_FamilyId" ON public."BuilderCall" USING btree ("FamilyId")`
- `CREATE INDEX "IX_BuilderCall_FamilyId_RunId" ON public."BuilderCall" USING btree ("FamilyId", "RunId")`
- `CREATE UNIQUE INDEX "PK_BuilderCall" ON public."BuilderCall" USING btree ("Id")`

## BuilderRuns

辅助建库运行及冻结的输入、模型和提示版本。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| SourceId | uuid | 否 | 无 |
| Status | text | 否 | 无 |
| Provider | text | 否 | 无 |
| Model | text | 否 | 无 |
| PromptVersion | text | 否 | 无 |
| InputHash | text | 否 | 无 |
| Error | text | 是 | 无 |
| Retries | integer | 否 | 无 |
| CompletedAt | timestamp with time zone | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| Type | text | 否 | ''::text |
| InputVersion | text | 否 | 'builder-input/1'::text |
| LibraryReleaseId | uuid | 是 | 无 |
| NextAttemptAt | timestamp with time zone | 是 | 无 |
| RetryRound | integer | 否 | 0 |
| ModelConfigHash | text | 是 | 无 |
| ModelConfigPayload | text | 是 | 无 |

约束：

- `AK_BuilderRuns_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_BuilderRuns_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_BuilderRuns_Releases_FamilyId_LibraryReleaseId`：`FOREIGN KEY ("FamilyId", "LibraryReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_BuilderRuns_Sources_FamilyId_SourceId`：`FOREIGN KEY ("FamilyId", "SourceId") REFERENCES "Sources"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_BuilderRuns`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_BuilderRuns_FamilyId_Id" ON public."BuilderRuns" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_BuilderRuns_FamilyId" ON public."BuilderRuns" USING btree ("FamilyId")`
- `CREATE INDEX "IX_BuilderRuns_FamilyId_LibraryReleaseId" ON public."BuilderRuns" USING btree ("FamilyId", "LibraryReleaseId")`
- `CREATE INDEX "IX_BuilderRuns_FamilyId_SourceId" ON public."BuilderRuns" USING btree ("FamilyId", "SourceId")`
- `CREATE UNIQUE INDEX "PK_BuilderRuns" ON public."BuilderRuns" USING btree ("Id")`

## Candidates

建库候选、片段引用及人工审核决定。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| RunId | uuid | 否 | 无 |
| ChunkId | uuid | 否 | 无 |
| Name | text | 否 | 无 |
| Behavior | text | 否 | 无 |
| Boundary | text | 否 | 无 |
| Quote | text | 否 | 无 |
| Type | text | 否 | 无 |
| Status | text | 否 | 无 |
| SuggestedAction | text | 否 | 无 |
| Matches | text | 否 | 无 |
| ExistingKCId | uuid | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| CreatedDraftId | uuid | 是 | 无 |
| CreatedKCId | uuid | 是 | 无 |
| Decision | text | 否 | ''::text |
| ReviewReason | text | 否 | ''::text |
| ReviewedAt | timestamp with time zone | 是 | 无 |
| ReviewedBy | uuid | 是 | 无 |
| ProtocolPayload | text | 是 | 无 |

约束：

- `AK_Candidates_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Candidates_BuilderRuns_FamilyId_RunId`：`FOREIGN KEY ("FamilyId", "RunId") REFERENCES "BuilderRuns"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Candidates_Chunks_FamilyId_ChunkId`：`FOREIGN KEY ("FamilyId", "ChunkId") REFERENCES "Chunks"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Candidates_Drafts_FamilyId_CreatedDraftId`：`FOREIGN KEY ("FamilyId", "CreatedDraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Candidates_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Candidates`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Candidates_FamilyId_Id" ON public."Candidates" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Candidates_FamilyId" ON public."Candidates" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Candidates_FamilyId_ChunkId" ON public."Candidates" USING btree ("FamilyId", "ChunkId")`
- `CREATE INDEX "IX_Candidates_FamilyId_CreatedDraftId" ON public."Candidates" USING btree ("FamilyId", "CreatedDraftId")`
- `CREATE INDEX "IX_Candidates_FamilyId_RunId" ON public."Candidates" USING btree ("FamilyId", "RunId")`
- `CREATE UNIQUE INDEX "PK_Candidates" ON public."Candidates" USING btree ("Id")`

## Chunks

本地来源分段及原文位置。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| SourceId | uuid | 否 | 无 |
| Locator | text | 否 | 无 |
| Text | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_Chunks_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Chunks_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Chunks_Sources_FamilyId_SourceId`：`FOREIGN KEY ("FamilyId", "SourceId") REFERENCES "Sources"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Chunks`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Chunks_FamilyId_Id" ON public."Chunks" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Chunks_FamilyId" ON public."Chunks" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Chunks_FamilyId_SourceId" ON public."Chunks" USING btree ("FamilyId", "SourceId")`
- `CREATE UNIQUE INDEX "PK_Chunks" ON public."Chunks" USING btree ("Id")`

## Commands

持久幂等请求、原始请求摘要及响应缓存；恢复时清除。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ActorId | uuid | 否 | 无 |
| Scope | text | 否 | 无 |
| Key | text | 否 | 无 |
| Hash | text | 否 | 无 |
| Response | text | 否 | 无 |
| StatusCode | integer | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| CookieCipher | text | 是 | 无 |

约束：

- `FK_Commands_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Commands`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Commands_FamilyId" ON public."Commands" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_Commands_FamilyId_ActorId_Scope_Key" ON public."Commands" USING btree ("FamilyId", "ActorId", "Scope", "Key")`
- `CREATE UNIQUE INDEX "PK_Commands" ON public."Commands" USING btree ("Id")`

## ContentIdentity

家庭内内容稳定身份及测量定义签名。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| EntityType | text | 否 | 无 |
| Code | text | 否 | 无 |
| Status | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_ContentIdentity_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_ContentIdentity_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_ContentIdentity`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_ContentIdentity_FamilyId_Id" ON public."ContentIdentity" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_ContentIdentity_FamilyId" ON public."ContentIdentity" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_ContentIdentity_FamilyId_EntityType_Code" ON public."ContentIdentity" USING btree ("FamilyId", "EntityType", "Code")`
- `CREATE UNIQUE INDEX "PK_ContentIdentity" ON public."ContentIdentity" USING btree ("Id")`

## ContentReviewRecord

整份内容人工审核；冻结草稿版本、原内容与审核者，区分明确备注与命令确认并关联实际发布。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| DraftId | uuid | 否 | 无 |
| DraftVersion | bigint | 否 | 无 |
| SourceTitle | text | 否 | 无 |
| SourcePayload | text | 否 | 无 |
| PayloadHash | text | 否 | 无 |
| ReviewerId | uuid | 否 | 无 |
| ReviewedAt | timestamp with time zone | 否 | 无 |
| Scope | text | 否 | 无 |
| Reason | text | 否 | 无 |
| ReasonSource | text | 否 | 无 |
| PublishedReleaseId | uuid | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| PublishedMappingVersion | text | 是 | 无 |

约束：

- `AK_ContentReviewRecord_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_ContentReviewRecord_Accounts_FamilyId_ReviewerId`：`FOREIGN KEY ("FamilyId", "ReviewerId") REFERENCES "Accounts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ContentReviewRecord_Drafts_FamilyId_DraftId`：`FOREIGN KEY ("FamilyId", "DraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ContentReviewRecord_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_ContentReviewRecord_Releases_FamilyId_PublishedReleaseId`：`FOREIGN KEY ("FamilyId", "PublishedReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_ContentReviewRecord`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_ContentReviewRecord_FamilyId_Id" ON public."ContentReviewRecord" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_ContentReviewRecord_DraftId_DraftVersion" ON public."ContentReviewRecord" USING btree ("DraftId", "DraftVersion")`
- `CREATE INDEX "IX_ContentReviewRecord_FamilyId" ON public."ContentReviewRecord" USING btree ("FamilyId")`
- `CREATE INDEX "IX_ContentReviewRecord_FamilyId_DraftId" ON public."ContentReviewRecord" USING btree ("FamilyId", "DraftId")`
- `CREATE INDEX "IX_ContentReviewRecord_FamilyId_PublishedReleaseId" ON public."ContentReviewRecord" USING btree ("FamilyId", "PublishedReleaseId")`
- `CREATE INDEX "IX_ContentReviewRecord_FamilyId_ReviewerId" ON public."ContentReviewRecord" USING btree ("FamilyId", "ReviewerId")`
- `CREATE UNIQUE INDEX "PK_ContentReviewRecord" ON public."ContentReviewRecord" USING btree ("Id")`

## ContentRevision

稳定身份下不可变内容修订。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| IdentityId | uuid | 否 | 无 |
| EntityType | text | 否 | 无 |
| Hash | text | 否 | 无 |
| Definition | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_ContentRevision_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_ContentRevision_ContentIdentity_FamilyId_IdentityId`：`FOREIGN KEY ("FamilyId", "IdentityId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ContentRevision_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_ContentRevision`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_ContentRevision_FamilyId_Id" ON public."ContentRevision" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_ContentRevision_FamilyId" ON public."ContentRevision" USING btree ("FamilyId")`
- `CREATE INDEX "IX_ContentRevision_FamilyId_IdentityId" ON public."ContentRevision" USING btree ("FamilyId", "IdentityId")`
- `CREATE UNIQUE INDEX "PK_ContentRevision" ON public."ContentRevision" USING btree ("Id")`

## CorrectionBatch

学生历史映射更正的预览、依据与确认。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| PreviewHash | text | 否 | 无 |
| Reason | text | 否 | 无 |
| ConfirmedBy | uuid | 否 | 无 |
| Status | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| AffectedAttemptIds | text | 是 | 无 |
| Cause | text | 是 | 无 |
| SourceGradingRevisionId | uuid | 是 | 无 |

约束：

- `AK_CorrectionBatch_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_CorrectionBatch_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_CorrectionBatch_Gradings_FamilyId_SourceGradingRevisionId`：`FOREIGN KEY ("FamilyId", "SourceGradingRevisionId") REFERENCES "Gradings"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_CorrectionBatch_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_CorrectionBatch_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_CorrectionBatch`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_CorrectionBatch_FamilyId_Id" ON public."CorrectionBatch" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_CorrectionBatch_FamilyId" ON public."CorrectionBatch" USING btree ("FamilyId")`
- `CREATE INDEX "IX_CorrectionBatch_FamilyId_ReleaseId" ON public."CorrectionBatch" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_CorrectionBatch_FamilyId_SourceGradingRevisionId" ON public."CorrectionBatch" USING btree ("FamilyId", "SourceGradingRevisionId")`
- `CREATE INDEX "IX_CorrectionBatch_FamilyId_StudentId" ON public."CorrectionBatch" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_CorrectionBatch" ON public."CorrectionBatch" USING btree ("Id")`

## CorrectionItem

更正批次内首次作答及生效映射版本。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Sequence | bigint | 否 | IDENTITY ALWAYS |
| BatchId | uuid | 否 | 无 |
| AttemptId | uuid | 否 | 无 |
| MappingReleaseId | uuid | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| MappingSetRevisionId | uuid | 是 | 无 |
| QuestionRevisionId | uuid | 是 | 无 |

约束：

- `FK_CorrectionItem_Attempts_FamilyId_AttemptId`：`FOREIGN KEY ("FamilyId", "AttemptId") REFERENCES "Attempts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_CorrectionItem_ContentRevision_FamilyId_QuestionRevisionId`：`FOREIGN KEY ("FamilyId", "QuestionRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_CorrectionItem_CorrectionBatch_FamilyId_BatchId`：`FOREIGN KEY ("FamilyId", "BatchId") REFERENCES "CorrectionBatch"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_CorrectionItem_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_CorrectionItem_MappingSetRevision_FamilyId_MappingSetRevisi~`：`FOREIGN KEY ("FamilyId", "MappingSetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_CorrectionItem_Releases_FamilyId_MappingReleaseId`：`FOREIGN KEY ("FamilyId", "MappingReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_CorrectionItem`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "IX_CorrectionItem_BatchId_AttemptId" ON public."CorrectionItem" USING btree ("BatchId", "AttemptId")`
- `CREATE INDEX "IX_CorrectionItem_FamilyId" ON public."CorrectionItem" USING btree ("FamilyId")`
- `CREATE INDEX "IX_CorrectionItem_FamilyId_AttemptId" ON public."CorrectionItem" USING btree ("FamilyId", "AttemptId")`
- `CREATE INDEX "IX_CorrectionItem_FamilyId_BatchId" ON public."CorrectionItem" USING btree ("FamilyId", "BatchId")`
- `CREATE INDEX "IX_CorrectionItem_FamilyId_MappingReleaseId" ON public."CorrectionItem" USING btree ("FamilyId", "MappingReleaseId")`
- `CREATE INDEX "IX_CorrectionItem_FamilyId_MappingSetRevisionId" ON public."CorrectionItem" USING btree ("FamilyId", "MappingSetRevisionId")`
- `CREATE INDEX "IX_CorrectionItem_FamilyId_QuestionRevisionId" ON public."CorrectionItem" USING btree ("FamilyId", "QuestionRevisionId")`
- `CREATE UNIQUE INDEX "PK_CorrectionItem" ON public."CorrectionItem" USING btree ("Id")`

## Drafts

可编辑内容目录、审核状态与审核人。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Title | text | 否 | 无 |
| Payload | text | 否 | 无 |
| Status | text | 否 | 无 |
| Version | bigint | 否 | 无 |
| ReviewedBy | uuid | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_Drafts_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Drafts_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Drafts`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Drafts_FamilyId_Id" ON public."Drafts" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Drafts_FamilyId" ON public."Drafts" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "PK_Drafts" ON public."Drafts" USING btree ("Id")`

## Embedding

内容修订向量及空间；当前为 Mock，不表示真实模型质量。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| EntityRevisionId | uuid | 否 | 无 |
| EntityType | text | 否 | 无 |
| Space | text | 否 | 无 |
| Dimensions | integer | 否 | 无 |
| TextHash | text | 否 | 无 |
| Vector | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_Embedding_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Embedding`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Embedding_FamilyId" ON public."Embedding" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_Embedding_FamilyId_EntityRevisionId_Space" ON public."Embedding" USING btree ("FamilyId", "EntityRevisionId", "Space")`
- `CREATE UNIQUE INDEX "PK_Embedding" ON public."Embedding" USING btree ("Id")`

## Evidence

评估世代内由有效作答/判分产生的能力证据。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| GenerationId | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| AttemptId | uuid | 否 | 无 |
| GradingId | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| KCId | uuid | 否 | 无 |
| KCRevisionId | uuid | 否 | 无 |
| Part | text | 否 | 无 |
| Positive | boolean | 否 | 无 |
| RawWeight | numeric(16,6) | 否 | 无 |
| Weight | numeric(16,6) | 否 | 无 |
| Factors | text | 否 | 无 |
| OccurredAt | timestamp with time zone | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| CorrectionBatchId | uuid | 是 | 无 |
| MappingReleaseId | uuid | 是 | 无 |
| MappingSetRevisionId | uuid | 是 | 无 |
| ContextId | uuid | 是 | 无 |

约束：

- `AK_Evidence_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Evidence_AssessmentContext_FamilyId_ContextId_GenerationId_~`：`FOREIGN KEY ("FamilyId", "ContextId", "GenerationId", "StudentId", "AttemptId", "GradingId") REFERENCES "AssessmentContext"("FamilyId", "Id", "GenerationId", "StudentId", "AttemptId", "GradingRevisionId") ON DELETE CASCADE`
- `FK_Evidence_Attempts_FamilyId_AttemptId`：`FOREIGN KEY ("FamilyId", "AttemptId") REFERENCES "Attempts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Evidence_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Evidence_Generations_FamilyId_GenerationId`：`FOREIGN KEY ("FamilyId", "GenerationId") REFERENCES "Generations"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Evidence_Gradings_FamilyId_GradingId`：`FOREIGN KEY ("FamilyId", "GradingId") REFERENCES "Gradings"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Evidence_MappingSetRevision_FamilyId_MappingSetRevisionId`：`FOREIGN KEY ("FamilyId", "MappingSetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Evidence_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Evidence`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Evidence_FamilyId_Id" ON public."Evidence" USING btree ("FamilyId", "Id")`
- `CREATE UNIQUE INDEX "IX_Evidence_ContextId_KCId_Part_Positive" ON public."Evidence" USING btree ("ContextId", "KCId", "Part", "Positive")`
- `CREATE INDEX "IX_Evidence_FamilyId" ON public."Evidence" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Evidence_FamilyId_AttemptId" ON public."Evidence" USING btree ("FamilyId", "AttemptId")`
- `CREATE INDEX "IX_Evidence_FamilyId_ContextId_GenerationId_StudentId_AttemptI~" ON public."Evidence" USING btree ("FamilyId", "ContextId", "GenerationId", "StudentId", "AttemptId", "GradingId")`
- `CREATE INDEX "IX_Evidence_FamilyId_GenerationId" ON public."Evidence" USING btree ("FamilyId", "GenerationId")`
- `CREATE INDEX "IX_Evidence_FamilyId_GradingId" ON public."Evidence" USING btree ("FamilyId", "GradingId")`
- `CREATE INDEX "IX_Evidence_FamilyId_MappingSetRevisionId" ON public."Evidence" USING btree ("FamilyId", "MappingSetRevisionId")`
- `CREATE INDEX "IX_Evidence_FamilyId_StudentId" ON public."Evidence" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Evidence_GenerationId_AttemptId_KCId_Part" ON public."Evidence" USING btree ("GenerationId", "AttemptId", "KCId", "Part")`
- `CREATE UNIQUE INDEX "PK_Evidence" ON public."Evidence" USING btree ("Id")`

## EvidenceRevocation

已确认更正导致的错误历史证据撤销；原证据不改写，同一证据最多撤销一次。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| EvidenceId | uuid | 否 | 无 |
| CorrectionBatchId | uuid | 否 | 无 |
| ReplacementGenerationId | uuid | 否 | 无 |
| Reason | text | 否 | 无 |
| Effect | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_EvidenceRevocation_CorrectionBatch_FamilyId_CorrectionBatch~`：`FOREIGN KEY ("FamilyId", "CorrectionBatchId") REFERENCES "CorrectionBatch"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_EvidenceRevocation_Evidence_FamilyId_EvidenceId`：`FOREIGN KEY ("FamilyId", "EvidenceId") REFERENCES "Evidence"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_EvidenceRevocation_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_EvidenceRevocation_Generations_FamilyId_ReplacementGenerati~`：`FOREIGN KEY ("FamilyId", "ReplacementGenerationId") REFERENCES "Generations"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_EvidenceRevocation_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_EvidenceRevocation`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "IX_EvidenceRevocation_EvidenceId" ON public."EvidenceRevocation" USING btree ("EvidenceId")`
- `CREATE INDEX "IX_EvidenceRevocation_FamilyId" ON public."EvidenceRevocation" USING btree ("FamilyId")`
- `CREATE INDEX "IX_EvidenceRevocation_FamilyId_CorrectionBatchId" ON public."EvidenceRevocation" USING btree ("FamilyId", "CorrectionBatchId")`
- `CREATE INDEX "IX_EvidenceRevocation_FamilyId_EvidenceId" ON public."EvidenceRevocation" USING btree ("FamilyId", "EvidenceId")`
- `CREATE INDEX "IX_EvidenceRevocation_FamilyId_ReplacementGenerationId" ON public."EvidenceRevocation" USING btree ("FamilyId", "ReplacementGenerationId")`
- `CREATE INDEX "IX_EvidenceRevocation_FamilyId_StudentId" ON public."EvidenceRevocation" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_EvidenceRevocation" ON public."EvidenceRevocation" USING btree ("Id")`

## Families

家庭、负责人及并发版本。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Name | text | 否 | 无 |
| Version | bigint | 否 | 无 |
| OwnerAccountId | uuid | 是 | 无 |

约束：

- `FK_Families_Accounts_Id_OwnerAccountId`：`FOREIGN KEY ("Id", "OwnerAccountId") REFERENCES "Accounts"("FamilyId", "Id") ON DELETE RESTRICT`
- `PK_Families`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Families_Id_OwnerAccountId" ON public."Families" USING btree ("Id", "OwnerAccountId")`
- `CREATE UNIQUE INDEX "PK_Families" ON public."Families" USING btree ("Id")`

## FamilyMembership

家庭账号与显式角色。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| AccountId | uuid | 否 | 无 |
| Roles | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_FamilyMembership_Accounts_FamilyId_AccountId`：`FOREIGN KEY ("FamilyId", "AccountId") REFERENCES "Accounts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_FamilyMembership_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_FamilyMembership`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_FamilyMembership_FamilyId" ON public."FamilyMembership" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_FamilyMembership_FamilyId_AccountId" ON public."FamilyMembership" USING btree ("FamilyId", "AccountId")`
- `CREATE UNIQUE INDEX "PK_FamilyMembership" ON public."FamilyMembership" USING btree ("Id")`

## Generations

整学生重放的评估世代及事件游标。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| Status | text | 否 | 无 |
| InputHash | text | 否 | 无 |
| RuleVersion | text | 否 | 无 |
| Cursor | bigint | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| ModelVersion | text | 否 | ''::text |

约束：

- `AK_Generations_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Generations_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Generations_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Generations`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Generations_FamilyId_Id" ON public."Generations" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Generations_FamilyId" ON public."Generations" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Generations_FamilyId_StudentId" ON public."Generations" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_Generations" ON public."Generations" USING btree ("Id")`

## GoalChange

固定目标的版本与设置变更历史。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| GoalId | uuid | 否 | 无 |
| Version | bigint | 否 | 无 |
| Before | text | 否 | 无 |
| After | text | 否 | 无 |
| Reason | text | 否 | 无 |
| ConfirmedBy | uuid | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_GoalChange_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_GoalChange_Goals_FamilyId_GoalId`：`FOREIGN KEY ("FamilyId", "GoalId") REFERENCES "Goals"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_GoalChange_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_GoalChange`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_GoalChange_FamilyId" ON public."GoalChange" USING btree ("FamilyId")`
- `CREATE INDEX "IX_GoalChange_FamilyId_GoalId" ON public."GoalChange" USING btree ("FamilyId", "GoalId")`
- `CREATE INDEX "IX_GoalChange_FamilyId_StudentId" ON public."GoalChange" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_GoalChange_GoalId_Version" ON public."GoalChange" USING btree ("GoalId", "Version")`
- `CREATE UNIQUE INDEX "PK_GoalChange" ON public."GoalChange" USING btree ("Id")`

## Goals

固定目标范围、日期、频率与优先级。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| Title | text | 否 | 无 |
| Minutes | integer | 否 | 无 |
| PaperReference | text | 否 | 无 |
| Active | boolean | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| EndDate | date | 是 | 无 |
| GoalType | text | 否 | 'Activity'::text |
| KCId | uuid | 是 | 无 |
| Period | text | 否 | 'Daily'::text |
| Priority | integer | 否 | 3 |
| ScheduleRule | text | 否 | '[1,2,3,4,5,6,0]'::text |
| StartDate | date | 是 | 无 |
| Subject | text | 否 | 'Unspecified'::text |
| TargetValue | integer | 否 | 1 |
| Version | bigint | 否 | 1 |
| CourseId | uuid | 是 | 无 |
| UnitId | uuid | 是 | 无 |

约束：

- `AK_Goals_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Goals_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Goals_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Goals`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Goals_FamilyId_Id" ON public."Goals" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Goals_FamilyId" ON public."Goals" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Goals_FamilyId_StudentId" ON public."Goals" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_Goals" ON public."Goals" USING btree ("Id")`

## Gradings

作答判分及追加更正；原始作答不覆盖。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| AttemptId | uuid | 否 | 无 |
| Number | integer | 否 | 无 |
| Result | text | 否 | 无 |
| Method | text | 否 | 无 |
| Reason | text | 否 | 无 |
| GradedBy | uuid | 是 | 无 |
| Steps | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| CorrectionBatchId | uuid | 是 | 无 |

约束：

- `AK_Gradings_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Gradings_Attempts_FamilyId_AttemptId`：`FOREIGN KEY ("FamilyId", "AttemptId") REFERENCES "Attempts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Gradings_CorrectionBatch_FamilyId_CorrectionBatchId`：`FOREIGN KEY ("FamilyId", "CorrectionBatchId") REFERENCES "CorrectionBatch"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Gradings_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Gradings`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Gradings_FamilyId_Id" ON public."Gradings" USING btree ("FamilyId", "Id")`
- `CREATE UNIQUE INDEX "IX_Gradings_AttemptId_Number" ON public."Gradings" USING btree ("AttemptId", "Number")`
- `CREATE INDEX "IX_Gradings_FamilyId" ON public."Gradings" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Gradings_FamilyId_AttemptId" ON public."Gradings" USING btree ("FamilyId", "AttemptId")`
- `CREATE INDEX "IX_Gradings_FamilyId_CorrectionBatchId" ON public."Gradings" USING btree ("FamilyId", "CorrectionBatchId")`
- `CREATE UNIQUE INDEX "PK_Gradings" ON public."Gradings" USING btree ("Id")`

## IndependentMappingDraft

独立映射草稿的固定来源、能力库、提交人及维护原因；创建不代表审核。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| SetRevisionId | uuid | 否 | 无 |
| SourceDraftId | uuid | 否 | 无 |
| SourceDraftVersion | bigint | 否 | 无 |
| SourcePayload | text | 否 | 无 |
| SourceHash | text | 否 | 无 |
| LibraryReleaseId | uuid | 否 | 无 |
| LibraryHash | text | 否 | 无 |
| SubmittedBy | uuid | 否 | 无 |
| Provider | text | 否 | 无 |
| Reason | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_IndependentMappingDraft_Accounts_FamilyId_SubmittedBy`：`FOREIGN KEY ("FamilyId", "SubmittedBy") REFERENCES "Accounts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_IndependentMappingDraft_Drafts_FamilyId_SourceDraftId`：`FOREIGN KEY ("FamilyId", "SourceDraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_IndependentMappingDraft_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_IndependentMappingDraft_MappingSetRevision_FamilyId_SetRevi~`：`FOREIGN KEY ("FamilyId", "SetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_IndependentMappingDraft_Releases_FamilyId_LibraryReleaseId`：`FOREIGN KEY ("FamilyId", "LibraryReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_IndependentMappingDraft`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_IndependentMappingDraft_FamilyId" ON public."IndependentMappingDraft" USING btree ("FamilyId")`
- `CREATE INDEX "IX_IndependentMappingDraft_FamilyId_LibraryReleaseId" ON public."IndependentMappingDraft" USING btree ("FamilyId", "LibraryReleaseId")`
- `CREATE INDEX "IX_IndependentMappingDraft_FamilyId_SetRevisionId" ON public."IndependentMappingDraft" USING btree ("FamilyId", "SetRevisionId")`
- `CREATE INDEX "IX_IndependentMappingDraft_FamilyId_SourceDraftId" ON public."IndependentMappingDraft" USING btree ("FamilyId", "SourceDraftId")`
- `CREATE INDEX "IX_IndependentMappingDraft_FamilyId_SubmittedBy" ON public."IndependentMappingDraft" USING btree ("FamilyId", "SubmittedBy")`
- `CREATE UNIQUE INDEX "IX_IndependentMappingDraft_SetRevisionId" ON public."IndependentMappingDraft" USING btree ("SetRevisionId")`
- `CREATE UNIQUE INDEX "PK_IndependentMappingDraft" ON public."IndependentMappingDraft" USING btree ("Id")`

## KCChangeProposal

能力拆分/合并/替换的人工提案。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ProposalType | text | 否 | 无 |
| Rationale | text | 否 | 无 |
| Status | text | 否 | 无 |
| Version | bigint | 否 | 无 |
| FromReleaseId | uuid | 否 | 无 |
| EffectiveReleaseId | uuid | 否 | 无 |
| CreatedBy | uuid | 否 | 无 |
| ReviewedBy | uuid | 是 | 无 |
| ReviewedAt | timestamp with time zone | 是 | 无 |
| AppliedBy | uuid | 是 | 无 |
| AppliedAt | timestamp with time zone | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_KCChangeProposal_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_KCChangeProposal_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_KCChangeProposal_Releases_FamilyId_EffectiveReleaseId`：`FOREIGN KEY ("FamilyId", "EffectiveReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KCChangeProposal_Releases_FamilyId_FromReleaseId`：`FOREIGN KEY ("FamilyId", "FromReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_KCChangeProposal`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_KCChangeProposal_FamilyId_Id" ON public."KCChangeProposal" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_KCChangeProposal_FamilyId" ON public."KCChangeProposal" USING btree ("FamilyId")`
- `CREATE INDEX "IX_KCChangeProposal_FamilyId_EffectiveReleaseId" ON public."KCChangeProposal" USING btree ("FamilyId", "EffectiveReleaseId")`
- `CREATE INDEX "IX_KCChangeProposal_FamilyId_FromReleaseId" ON public."KCChangeProposal" USING btree ("FamilyId", "FromReleaseId")`
- `CREATE UNIQUE INDEX "PK_KCChangeProposal" ON public."KCChangeProposal" USING btree ("Id")`

## KCChangeProposalItem

提案中的来源/目标能力与固定修订。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ProposalId | uuid | 否 | 无 |
| Side | text | 否 | 无 |
| KCId | uuid | 否 | 无 |
| ProposedRevisionId | uuid | 否 | 无 |
| Weight | numeric | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_KCChangeProposalItem_ContentIdentity_FamilyId_KCId`：`FOREIGN KEY ("FamilyId", "KCId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KCChangeProposalItem_ContentRevision_FamilyId_ProposedRevis~`：`FOREIGN KEY ("FamilyId", "ProposedRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KCChangeProposalItem_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_KCChangeProposalItem_KCChangeProposal_FamilyId_ProposalId`：`FOREIGN KEY ("FamilyId", "ProposalId") REFERENCES "KCChangeProposal"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_KCChangeProposalItem`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_KCChangeProposalItem_FamilyId" ON public."KCChangeProposalItem" USING btree ("FamilyId")`
- `CREATE INDEX "IX_KCChangeProposalItem_FamilyId_KCId" ON public."KCChangeProposalItem" USING btree ("FamilyId", "KCId")`
- `CREATE INDEX "IX_KCChangeProposalItem_FamilyId_ProposalId" ON public."KCChangeProposalItem" USING btree ("FamilyId", "ProposalId")`
- `CREATE INDEX "IX_KCChangeProposalItem_FamilyId_ProposedRevisionId" ON public."KCChangeProposalItem" USING btree ("FamilyId", "ProposedRevisionId")`
- `CREATE UNIQUE INDEX "IX_KCChangeProposalItem_ProposalId_Side_KCId" ON public."KCChangeProposalItem" USING btree ("ProposalId", "Side", "KCId")`
- `CREATE UNIQUE INDEX "PK_KCChangeProposalItem" ON public."KCChangeProposalItem" USING btree ("Id")`

## KCProposalEvent

提案审核和状态变更历史。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ProposalId | uuid | 否 | 无 |
| Version | bigint | 否 | 无 |
| ActorId | uuid | 否 | 无 |
| Action | text | 否 | 无 |
| Reason | text | 否 | 无 |
| Snapshot | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_KCProposalEvent_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_KCProposalEvent_KCChangeProposal_FamilyId_ProposalId`：`FOREIGN KEY ("FamilyId", "ProposalId") REFERENCES "KCChangeProposal"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_KCProposalEvent`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_KCProposalEvent_FamilyId" ON public."KCProposalEvent" USING btree ("FamilyId")`
- `CREATE INDEX "IX_KCProposalEvent_FamilyId_ProposalId" ON public."KCProposalEvent" USING btree ("FamilyId", "ProposalId")`
- `CREATE UNIQUE INDEX "IX_KCProposalEvent_ProposalId_Version" ON public."KCProposalEvent" USING btree ("ProposalId", "Version")`
- `CREATE UNIQUE INDEX "PK_KCProposalEvent" ON public."KCProposalEvent" USING btree ("Id")`

## KnowledgeMigration

能力变更关系元数据；不迁移概率、证据或复习。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ProposalId | uuid | 否 | 无 |
| FromKCId | uuid | 否 | 无 |
| ToKCId | uuid | 否 | 无 |
| FromRevisionId | uuid | 否 | 无 |
| ToRevisionId | uuid | 否 | 无 |
| MigrationType | text | 否 | 无 |
| Weight | numeric | 是 | 无 |
| EffectiveReleaseId | uuid | 否 | 无 |
| EvidencePolicy | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_KnowledgeMigration_ContentIdentity_FamilyId_FromKCId`：`FOREIGN KEY ("FamilyId", "FromKCId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KnowledgeMigration_ContentIdentity_FamilyId_ToKCId`：`FOREIGN KEY ("FamilyId", "ToKCId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KnowledgeMigration_ContentRevision_FamilyId_FromRevisionId`：`FOREIGN KEY ("FamilyId", "FromRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KnowledgeMigration_ContentRevision_FamilyId_ToRevisionId`：`FOREIGN KEY ("FamilyId", "ToRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KnowledgeMigration_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_KnowledgeMigration_KCChangeProposal_FamilyId_ProposalId`：`FOREIGN KEY ("FamilyId", "ProposalId") REFERENCES "KCChangeProposal"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_KnowledgeMigration_Releases_FamilyId_EffectiveReleaseId`：`FOREIGN KEY ("FamilyId", "EffectiveReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_KnowledgeMigration`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_KnowledgeMigration_FamilyId" ON public."KnowledgeMigration" USING btree ("FamilyId")`
- `CREATE INDEX "IX_KnowledgeMigration_FamilyId_EffectiveReleaseId" ON public."KnowledgeMigration" USING btree ("FamilyId", "EffectiveReleaseId")`
- `CREATE INDEX "IX_KnowledgeMigration_FamilyId_FromKCId" ON public."KnowledgeMigration" USING btree ("FamilyId", "FromKCId")`
- `CREATE INDEX "IX_KnowledgeMigration_FamilyId_FromRevisionId" ON public."KnowledgeMigration" USING btree ("FamilyId", "FromRevisionId")`
- `CREATE INDEX "IX_KnowledgeMigration_FamilyId_ProposalId" ON public."KnowledgeMigration" USING btree ("FamilyId", "ProposalId")`
- `CREATE INDEX "IX_KnowledgeMigration_FamilyId_ToKCId" ON public."KnowledgeMigration" USING btree ("FamilyId", "ToKCId")`
- `CREATE INDEX "IX_KnowledgeMigration_FamilyId_ToRevisionId" ON public."KnowledgeMigration" USING btree ("FamilyId", "ToRevisionId")`
- `CREATE UNIQUE INDEX "IX_KnowledgeMigration_ProposalId_FromKCId_ToKCId" ON public."KnowledgeMigration" USING btree ("ProposalId", "FromKCId", "ToKCId")`
- `CREATE UNIQUE INDEX "PK_KnowledgeMigration" ON public."KnowledgeMigration" USING btree ("Id")`

## MappingReviewDecision

不可覆盖的映射审核依据、原始/校正摘要、审核人及所建草稿。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| SuggestionId | uuid | 否 | 无 |
| Decision | text | 否 | 无 |
| ReviewerId | uuid | 否 | 无 |
| ReviewedAt | timestamp with time zone | 否 | 无 |
| Reason | text | 否 | 无 |
| OriginalPayloadHash | text | 否 | 无 |
| CorrectedPayload | text | 否 | 无 |
| CorrectedPayloadHash | text | 否 | 无 |
| CreatedDraftId | uuid | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_MappingReviewDecision_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_MappingReviewDecision_Accounts_FamilyId_ReviewerId`：`FOREIGN KEY ("FamilyId", "ReviewerId") REFERENCES "Accounts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingReviewDecision_Drafts_FamilyId_CreatedDraftId`：`FOREIGN KEY ("FamilyId", "CreatedDraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingReviewDecision_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_MappingReviewDecision_MappingSuggestion_FamilyId_Suggestion~`：`FOREIGN KEY ("FamilyId", "SuggestionId") REFERENCES "MappingSuggestion"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_MappingReviewDecision`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_MappingReviewDecision_FamilyId_Id" ON public."MappingReviewDecision" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_MappingReviewDecision_FamilyId" ON public."MappingReviewDecision" USING btree ("FamilyId")`
- `CREATE INDEX "IX_MappingReviewDecision_FamilyId_CreatedDraftId" ON public."MappingReviewDecision" USING btree ("FamilyId", "CreatedDraftId")`
- `CREATE INDEX "IX_MappingReviewDecision_FamilyId_ReviewerId" ON public."MappingReviewDecision" USING btree ("FamilyId", "ReviewerId")`
- `CREATE INDEX "IX_MappingReviewDecision_FamilyId_SuggestionId" ON public."MappingReviewDecision" USING btree ("FamilyId", "SuggestionId")`
- `CREATE UNIQUE INDEX "IX_MappingReviewDecision_SuggestionId" ON public."MappingReviewDecision" USING btree ("SuggestionId")`
- `CREATE UNIQUE INDEX "PK_MappingReviewDecision" ON public."MappingReviewDecision" USING btree ("Id")`

## MappingRun

映射建议运行；冻结原草稿、对象版本和正式能力库输入。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| SourceDraftId | uuid | 否 | 无 |
| SourceDraftVersion | bigint | 否 | 无 |
| SourceTitle | text | 否 | 无 |
| SourcePayload | text | 否 | 无 |
| SourceHash | text | 否 | 无 |
| LibraryReleaseId | uuid | 否 | 无 |
| LibraryHash | text | 否 | 无 |
| InputHash | text | 否 | 无 |
| Provider | text | 否 | 无 |
| Model | text | 否 | 无 |
| PromptVersion | text | 否 | 无 |
| Status | text | 否 | 无 |
| CompletedAt | timestamp with time zone | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_MappingRun_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_MappingRun_Drafts_FamilyId_SourceDraftId`：`FOREIGN KEY ("FamilyId", "SourceDraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingRun_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_MappingRun_Releases_FamilyId_LibraryReleaseId`：`FOREIGN KEY ("FamilyId", "LibraryReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_MappingRun`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_MappingRun_FamilyId_Id" ON public."MappingRun" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_MappingRun_FamilyId" ON public."MappingRun" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "IX_MappingRun_FamilyId_InputHash" ON public."MappingRun" USING btree ("FamilyId", "InputHash")`
- `CREATE INDEX "IX_MappingRun_FamilyId_LibraryReleaseId" ON public."MappingRun" USING btree ("FamilyId", "LibraryReleaseId")`
- `CREATE INDEX "IX_MappingRun_FamilyId_SourceDraftId" ON public."MappingRun" USING btree ("FamilyId", "SourceDraftId")`
- `CREATE UNIQUE INDEX "PK_MappingRun" ON public."MappingRun" USING btree ("Id")`

## MappingSetItem

映射修订的固定能力版本、教学覆盖和独立证据份额。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| SetRevisionId | uuid | 否 | 无 |
| KCId | uuid | 否 | 无 |
| KCRevisionId | uuid | 否 | 无 |
| Role | text | 否 | 无 |
| CoverageWeight | numeric(16,6) | 否 | 无 |
| EvidenceShare | numeric(16,6) | 否 | 无 |
| EvidenceMode | text | 否 | 无 |
| Step | text | 是 | 无 |
| Sequence | integer | 否 | 无 |
| ModelScore | numeric | 是 | 无 |
| SourceRefs | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_MappingSetItem_ContentIdentity_FamilyId_KCId`：`FOREIGN KEY ("FamilyId", "KCId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingSetItem_ContentRevision_FamilyId_KCRevisionId`：`FOREIGN KEY ("FamilyId", "KCRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingSetItem_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_MappingSetItem_MappingSetRevision_FamilyId_SetRevisionId`：`FOREIGN KEY ("FamilyId", "SetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_MappingSetItem`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_MappingSetItem_FamilyId" ON public."MappingSetItem" USING btree ("FamilyId")`
- `CREATE INDEX "IX_MappingSetItem_FamilyId_KCId" ON public."MappingSetItem" USING btree ("FamilyId", "KCId")`
- `CREATE INDEX "IX_MappingSetItem_FamilyId_KCRevisionId" ON public."MappingSetItem" USING btree ("FamilyId", "KCRevisionId")`
- `CREATE INDEX "IX_MappingSetItem_FamilyId_SetRevisionId" ON public."MappingSetItem" USING btree ("FamilyId", "SetRevisionId")`
- `CREATE UNIQUE INDEX "IX_MappingSetItem_SetRevisionId_Sequence" ON public."MappingSetItem" USING btree ("SetRevisionId", "Sequence")`
- `CREATE UNIQUE INDEX "PK_MappingSetItem" ON public."MappingSetItem" USING btree ("Id")`

## MappingSetRevision

统一映射修订；待审草稿无审核来源，已审来源二选一，固定对象及能力修订。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| DraftId | uuid | 否 | 无 |
| ReviewDecisionId | uuid | 是 | 无 |
| OwnerType | text | 否 | 无 |
| OwnerId | uuid | 否 | 无 |
| OwnerRevisionId | uuid | 否 | 无 |
| OriginalOwnerRevisionId | uuid | 否 | 无 |
| OwnerDefinitionHash | text | 否 | 无 |
| RevisionNo | integer | 否 | 无 |
| EvidencePolicy | text | 否 | 无 |
| ReviewStatus | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| ContentReviewRecordId | uuid | 是 | 无 |
| CoverageOrigin | text | 否 | 'HumanReviewed'::text |

约束：

- `AK_MappingSetRevision_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `CK_MappingSetRevision_ReviewSource`：`CHECK ("ReviewStatus" = 'Draft'::text AND "ReviewDecisionId" IS NULL AND "ContentReviewRecordId" IS NULL OR ("ReviewStatus" = ANY (ARRAY['ReviewedDraft'::text, 'ReviewedCatalog'::text])) AND ("ReviewDecisionId" IS NOT NULL) <> ("ContentReviewRecordId" IS NOT NULL))`
- `FK_MappingSetRevision_ContentReviewRecord_FamilyId_ContentRevi~`：`FOREIGN KEY ("FamilyId", "ContentReviewRecordId") REFERENCES "ContentReviewRecord"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingSetRevision_Drafts_FamilyId_DraftId`：`FOREIGN KEY ("FamilyId", "DraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_MappingSetRevision_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_MappingSetRevision_MappingReviewDecision_FamilyId_ReviewDec~`：`FOREIGN KEY ("FamilyId", "ReviewDecisionId") REFERENCES "MappingReviewDecision"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_MappingSetRevision`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_MappingSetRevision_FamilyId_Id" ON public."MappingSetRevision" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_MappingSetRevision_FamilyId" ON public."MappingSetRevision" USING btree ("FamilyId")`
- `CREATE INDEX "IX_MappingSetRevision_FamilyId_ContentReviewRecordId" ON public."MappingSetRevision" USING btree ("FamilyId", "ContentReviewRecordId")`
- `CREATE INDEX "IX_MappingSetRevision_FamilyId_DraftId" ON public."MappingSetRevision" USING btree ("FamilyId", "DraftId")`
- `CREATE INDEX "IX_MappingSetRevision_FamilyId_OwnerType_OwnerRevisionId" ON public."MappingSetRevision" USING btree ("FamilyId", "OwnerType", "OwnerRevisionId")`
- `CREATE INDEX "IX_MappingSetRevision_FamilyId_ReviewDecisionId" ON public."MappingSetRevision" USING btree ("FamilyId", "ReviewDecisionId")`
- `CREATE UNIQUE INDEX "PK_MappingSetRevision" ON public."MappingSetRevision" USING btree ("Id")`

## MappingSuggestion

题目/课时/资源原始建议与排序，仅经人工审核生成草稿。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| RunId | uuid | 否 | 无 |
| OwnerType | text | 否 | 无 |
| OwnerId | uuid | 否 | 无 |
| OwnerRevisionId | uuid | 否 | 无 |
| OwnerTitle | text | 否 | 无 |
| EvidencePolicy | text | 否 | 无 |
| SuggestedItems | text | 否 | 无 |
| Matches | text | 否 | 无 |
| ValidationFlags | text | 否 | 无 |
| Status | text | 否 | 无 |
| Version | bigint | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_MappingSuggestion_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_MappingSuggestion_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_MappingSuggestion_MappingRun_FamilyId_RunId`：`FOREIGN KEY ("FamilyId", "RunId") REFERENCES "MappingRun"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_MappingSuggestion`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_MappingSuggestion_FamilyId_Id" ON public."MappingSuggestion" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_MappingSuggestion_FamilyId" ON public."MappingSuggestion" USING btree ("FamilyId")`
- `CREATE INDEX "IX_MappingSuggestion_FamilyId_RunId" ON public."MappingSuggestion" USING btree ("FamilyId", "RunId")`
- `CREATE UNIQUE INDEX "IX_MappingSuggestion_RunId_OwnerType_OwnerId" ON public."MappingSuggestion" USING btree ("RunId", "OwnerType", "OwnerId")`
- `CREATE UNIQUE INDEX "PK_MappingSuggestion" ON public."MappingSuggestion" USING btree ("Id")`

## Masteries

指定评估世代的能力状态、Beta 参数与覆盖。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| GenerationId | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| KCId | uuid | 否 | 无 |
| Alpha | numeric(16,6) | 否 | 无 |
| Beta | numeric(16,6) | 否 | 无 |
| Probability | numeric(16,6) | 否 | 无 |
| EffectiveEvidence | numeric(16,6) | 否 | 无 |
| Confidence | text | 否 | 无 |
| Status | text | 否 | 无 |
| NeedsRecheck | boolean | 否 | 无 |
| DistinctQuestions | integer | 否 | 无 |
| Gaps | text | 否 | 无 |
| Reason | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_Masteries_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Masteries_Generations_FamilyId_GenerationId`：`FOREIGN KEY ("FamilyId", "GenerationId") REFERENCES "Generations"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Masteries_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Masteries`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Masteries_FamilyId" ON public."Masteries" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Masteries_FamilyId_GenerationId" ON public."Masteries" USING btree ("FamilyId", "GenerationId")`
- `CREATE INDEX "IX_Masteries_FamilyId_StudentId" ON public."Masteries" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Masteries_GenerationId_KCId" ON public."Masteries" USING btree ("GenerationId", "KCId")`
- `CREATE UNIQUE INDEX "PK_Masteries" ON public."Masteries" USING btree ("Id")`

## Outbox

事务内写入的后台重放队列、处理及重试状态。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| AttemptId | uuid | 否 | 无 |
| ProcessedAt | timestamp with time zone | 是 | 无 |
| Retries | integer | 否 | 无 |
| Error | text | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| NextAttemptAt | timestamp with time zone | 是 | 无 |

约束：

- `FK_Outbox_Attempts_FamilyId_AttemptId`：`FOREIGN KEY ("FamilyId", "AttemptId") REFERENCES "Attempts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Outbox_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Outbox_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Outbox`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Outbox_FamilyId" ON public."Outbox" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Outbox_FamilyId_AttemptId" ON public."Outbox" USING btree ("FamilyId", "AttemptId")`
- `CREATE INDEX "IX_Outbox_FamilyId_StudentId" ON public."Outbox" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_Outbox" ON public."Outbox" USING btree ("Id")`

## PaperWrong

纸质错题、原图、人工归因及正式代录关联。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| FileId | uuid | 是 | 无 |
| Stem | text | 否 | 无 |
| Answer | text | 否 | 无 |
| Status | text | 否 | 无 |
| ErrorType | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| AttemptId | uuid | 是 | 无 |
| ConfirmationReason | text | 否 | ''::text |
| ConfirmedAt | timestamp with time zone | 是 | 无 |
| ConfirmedBy | uuid | 是 | 无 |
| DraftId | uuid | 是 | 无 |
| QuestionId | uuid | 是 | 无 |
| ReleaseId | uuid | 是 | 无 |

约束：

- `FK_PaperWrong_Attempts_FamilyId_AttemptId`：`FOREIGN KEY ("FamilyId", "AttemptId") REFERENCES "Attempts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_PaperWrong_Drafts_FamilyId_DraftId`：`FOREIGN KEY ("FamilyId", "DraftId") REFERENCES "Drafts"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_PaperWrong_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_PaperWrong_PrivateFile_FamilyId_FileId`：`FOREIGN KEY ("FamilyId", "FileId") REFERENCES "PrivateFile"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_PaperWrong_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_PaperWrong_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_PaperWrong`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "IX_PaperWrong_AttemptId" ON public."PaperWrong" USING btree ("AttemptId")`
- `CREATE INDEX "IX_PaperWrong_FamilyId" ON public."PaperWrong" USING btree ("FamilyId")`
- `CREATE INDEX "IX_PaperWrong_FamilyId_AttemptId" ON public."PaperWrong" USING btree ("FamilyId", "AttemptId")`
- `CREATE INDEX "IX_PaperWrong_FamilyId_DraftId" ON public."PaperWrong" USING btree ("FamilyId", "DraftId")`
- `CREATE INDEX "IX_PaperWrong_FamilyId_FileId" ON public."PaperWrong" USING btree ("FamilyId", "FileId")`
- `CREATE INDEX "IX_PaperWrong_FamilyId_ReleaseId" ON public."PaperWrong" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_PaperWrong_FamilyId_StudentId" ON public."PaperWrong" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_PaperWrong" ON public."PaperWrong" USING btree ("Id")`

## ParentBurdenRecord

人工填写投入及不可覆盖的更正链；不是停留时间。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| RecordedBy | uuid | 否 | 无 |
| Date | date | 否 | 无 |
| Category | text | 否 | 无 |
| Minutes | numeric(16,6) | 否 | 无 |
| Note | text | 否 | 无 |
| SupersedesId | uuid | 是 | 无 |
| CorrectionReason | text | 否 | 无 |
| Method | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_ParentBurdenRecord_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_ParentBurdenRecord_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_ParentBurdenRecord_ParentBurdenRecord_FamilyId_SupersedesId`：`FOREIGN KEY ("FamilyId", "SupersedesId") REFERENCES "ParentBurdenRecord"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ParentBurdenRecord_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_ParentBurdenRecord`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_ParentBurdenRecord_FamilyId_Id" ON public."ParentBurdenRecord" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_ParentBurdenRecord_FamilyId" ON public."ParentBurdenRecord" USING btree ("FamilyId")`
- `CREATE INDEX "IX_ParentBurdenRecord_FamilyId_StudentId" ON public."ParentBurdenRecord" USING btree ("FamilyId", "StudentId")`
- `CREATE INDEX "IX_ParentBurdenRecord_FamilyId_SupersedesId" ON public."ParentBurdenRecord" USING btree ("FamilyId", "SupersedesId")`
- `CREATE INDEX "IX_ParentBurdenRecord_StudentId_Date" ON public."ParentBurdenRecord" USING btree ("StudentId", "Date")`
- `CREATE UNIQUE INDEX "IX_ParentBurdenRecord_SupersedesId" ON public."ParentBurdenRecord" USING btree ("SupersedesId")`
- `CREATE UNIQUE INDEX "PK_ParentBurdenRecord" ON public."ParentBurdenRecord" USING btree ("Id")`

## Placements

固定任务在计划修订中的位置和锁定状态。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| RevisionId | uuid | 否 | 无 |
| TaskId | uuid | 否 | 无 |
| Sequence | integer | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_Placements_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Placements_PlanRevisions_FamilyId_RevisionId`：`FOREIGN KEY ("FamilyId", "RevisionId") REFERENCES "PlanRevisions"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Placements_Tasks_FamilyId_TaskId`：`FOREIGN KEY ("FamilyId", "TaskId") REFERENCES "Tasks"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Placements`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Placements_FamilyId" ON public."Placements" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Placements_FamilyId_RevisionId" ON public."Placements" USING btree ("FamilyId", "RevisionId")`
- `CREATE INDEX "IX_Placements_FamilyId_TaskId" ON public."Placements" USING btree ("FamilyId", "TaskId")`
- `CREATE UNIQUE INDEX "IX_Placements_RevisionId_TaskId" ON public."Placements" USING btree ("RevisionId", "TaskId")`
- `CREATE UNIQUE INDEX "PK_Placements" ON public."Placements" USING btree ("Id")`

## PlanRevisions

计划草稿/发布修订、预算与规则版本。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| PlanId | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| Number | integer | 否 | 无 |
| Budget | integer | 否 | 无 |
| Reserved | integer | 否 | 无 |
| Overflow | integer | 否 | 无 |
| Status | text | 否 | 无 |
| InputHash | text | 否 | 无 |
| Candidates | text | 否 | 无 |
| Warnings | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| RuleVersion | text | 否 | 'legacy/unknown'::text |

约束：

- `AK_PlanRevisions_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_PlanRevisions_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_PlanRevisions_Plans_FamilyId_PlanId`：`FOREIGN KEY ("FamilyId", "PlanId") REFERENCES "Plans"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_PlanRevisions`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_PlanRevisions_FamilyId_Id" ON public."PlanRevisions" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_PlanRevisions_FamilyId" ON public."PlanRevisions" USING btree ("FamilyId")`
- `CREATE INDEX "IX_PlanRevisions_FamilyId_PlanId" ON public."PlanRevisions" USING btree ("FamilyId", "PlanId")`
- `CREATE UNIQUE INDEX "IX_PlanRevisions_PlanId_Number" ON public."PlanRevisions" USING btree ("PlanId", "Number")`
- `CREATE UNIQUE INDEX "PK_PlanRevisions" ON public."PlanRevisions" USING btree ("Id")`

## Plans

学生日期计划及当前草稿/发布绑定。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| Date | date | 否 | 无 |
| ActiveRevisionId | uuid | 是 | 无 |
| Status | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_Plans_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Plans_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Plans_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Plans`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Plans_FamilyId_Id" ON public."Plans" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Plans_FamilyId" ON public."Plans" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Plans_FamilyId_StudentId" ON public."Plans" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Plans_StudentId_Date" ON public."Plans" USING btree ("StudentId", "Date")`
- `CREATE UNIQUE INDEX "PK_Plans" ON public."Plans" USING btree ("Id")`

## PrivateFile

私有附件字节、类型、摘要及生命周期。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Name | text | 否 | 无 |
| MimeType | text | 否 | 无 |
| Hash | text | 否 | 无 |
| Bytes | bytea | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_PrivateFile_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_PrivateFile_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_PrivateFile`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_PrivateFile_FamilyId_Id" ON public."PrivateFile" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_PrivateFile_FamilyId" ON public."PrivateFile" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "PK_PrivateFile" ON public."PrivateFile" USING btree ("Id")`

## ProgressChange

学校进度确认、更正和撤回历史。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| OldProgressId | uuid | 否 | 无 |
| NewProgressId | uuid | 是 | 无 |
| ConfirmedBy | uuid | 否 | 无 |
| Reason | text | 否 | 无 |
| Before | text | 否 | 无 |
| After | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_ProgressChange_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_ProgressChange_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_ProgressChange`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_ProgressChange_FamilyId" ON public."ProgressChange" USING btree ("FamilyId")`
- `CREATE INDEX "IX_ProgressChange_FamilyId_StudentId" ON public."ProgressChange" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_ProgressChange" ON public."ProgressChange" USING btree ("Id")`

## Progresses

学生日期课时进度及当时内容版本。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| LessonId | uuid | 否 | 无 |
| Date | date | 否 | 无 |
| Source | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| ReleaseId | uuid | 是 | 无 |
| Status | text | 否 | 'Confirmed'::text |

约束：

- `FK_Progresses_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Progresses_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Progresses_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Progresses`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Progresses_FamilyId" ON public."Progresses" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Progresses_FamilyId_ReleaseId" ON public."Progresses" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_Progresses_FamilyId_StudentId" ON public."Progresses" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Progresses_StudentId_Date_LessonId" ON public."Progresses" USING btree ("StudentId", "Date", "LessonId")`
- `CREATE UNIQUE INDEX "PK_Progresses" ON public."Progresses" USING btree ("Id")`

## ReleaseItem

发布版本对稳定身份和不可变修订的固定引用。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| IdentityId | uuid | 否 | 无 |
| RevisionId | uuid | 否 | 无 |
| EntityType | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_ReleaseItem_ContentIdentity_FamilyId_IdentityId`：`FOREIGN KEY ("FamilyId", "IdentityId") REFERENCES "ContentIdentity"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ReleaseItem_ContentRevision_FamilyId_RevisionId`：`FOREIGN KEY ("FamilyId", "RevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ReleaseItem_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_ReleaseItem_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_ReleaseItem`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_ReleaseItem_FamilyId" ON public."ReleaseItem" USING btree ("FamilyId")`
- `CREATE INDEX "IX_ReleaseItem_FamilyId_IdentityId" ON public."ReleaseItem" USING btree ("FamilyId", "IdentityId")`
- `CREATE INDEX "IX_ReleaseItem_FamilyId_ReleaseId" ON public."ReleaseItem" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_ReleaseItem_FamilyId_RevisionId" ON public."ReleaseItem" USING btree ("FamilyId", "RevisionId")`
- `CREATE UNIQUE INDEX "IX_ReleaseItem_ReleaseId_EntityType_IdentityId" ON public."ReleaseItem" USING btree ("ReleaseId", "EntityType", "IdentityId")`
- `CREATE UNIQUE INDEX "PK_ReleaseItem" ON public."ReleaseItem" USING btree ("Id")`

## ReleaseMappingSet

新发布对实际映射容器的不可变选择；旧无容器的快照不补造。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| SetRevisionId | uuid | 否 | 无 |
| OwnerType | text | 否 | 无 |
| OwnerId | uuid | 否 | 无 |
| OwnerRevisionId | uuid | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_ReleaseMappingSet_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_ReleaseMappingSet_MappingSetRevision_FamilyId_SetRevisionId`：`FOREIGN KEY ("FamilyId", "SetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_ReleaseMappingSet_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_ReleaseMappingSet`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_ReleaseMappingSet_FamilyId" ON public."ReleaseMappingSet" USING btree ("FamilyId")`
- `CREATE INDEX "IX_ReleaseMappingSet_FamilyId_ReleaseId" ON public."ReleaseMappingSet" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_ReleaseMappingSet_FamilyId_SetRevisionId" ON public."ReleaseMappingSet" USING btree ("FamilyId", "SetRevisionId")`
- `CREATE UNIQUE INDEX "IX_ReleaseMappingSet_ReleaseId_OwnerType_OwnerId" ON public."ReleaseMappingSet" USING btree ("ReleaseId", "OwnerType", "OwnerId")`
- `CREATE UNIQUE INDEX "PK_ReleaseMappingSet" ON public."ReleaseMappingSet" USING btree ("Id")`

## Releases

不可变内容目录快照、发布序号及撤回标记。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Number | integer | 否 | 无 |
| Payload | text | 否 | 无 |
| Hash | text | 否 | 无 |
| PublishedBy | uuid | 否 | 无 |
| Withdrawn | boolean | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_Releases_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Releases_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Releases`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Releases_FamilyId_Id" ON public."Releases" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Releases_FamilyId" ON public."Releases" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "PK_Releases" ON public."Releases" USING btree ("Id")`

## Reviews

评估世代内错题/能力复习阶段与到期日。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| GenerationId | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| TargetId | uuid | 否 | 无 |
| KCId | uuid | 是 | 无 |
| TargetType | text | 否 | 无 |
| Stage | text | 否 | 无 |
| DueDate | date | 否 | 无 |
| Status | text | 否 | 无 |
| WrongCount | integer | 否 | 无 |
| ErrorType | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `FK_Reviews_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Reviews_Generations_FamilyId_GenerationId`：`FOREIGN KEY ("FamilyId", "GenerationId") REFERENCES "Generations"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Reviews_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Reviews`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE INDEX "IX_Reviews_FamilyId" ON public."Reviews" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Reviews_FamilyId_GenerationId" ON public."Reviews" USING btree ("FamilyId", "GenerationId")`
- `CREATE INDEX "IX_Reviews_FamilyId_StudentId" ON public."Reviews" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "IX_Reviews_GenerationId_TargetType_TargetId" ON public."Reviews" USING btree ("GenerationId", "TargetType", "TargetId")`
- `CREATE UNIQUE INDEX "PK_Reviews" ON public."Reviews" USING btree ("Id")`

## Sessions

任务领取时固定的内容版本和作答会话。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| TaskId | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| QuestionId | uuid | 否 | 无 |
| HintLevel | integer | 否 | 无 |
| AnswerShown | boolean | 否 | 无 |
| StartedAt | timestamp with time zone | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| MappingSetRevisionId | uuid | 是 | 无 |
| QuestionRevisionId | uuid | 是 | 无 |

约束：

- `AK_Sessions_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Sessions_ContentRevision_FamilyId_QuestionRevisionId`：`FOREIGN KEY ("FamilyId", "QuestionRevisionId") REFERENCES "ContentRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Sessions_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Sessions_MappingSetRevision_FamilyId_MappingSetRevisionId`：`FOREIGN KEY ("FamilyId", "MappingSetRevisionId") REFERENCES "MappingSetRevision"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Sessions_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Sessions_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Sessions_Tasks_FamilyId_TaskId`：`FOREIGN KEY ("FamilyId", "TaskId") REFERENCES "Tasks"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Sessions`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Sessions_FamilyId_Id" ON public."Sessions" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Sessions_FamilyId" ON public."Sessions" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Sessions_FamilyId_MappingSetRevisionId" ON public."Sessions" USING btree ("FamilyId", "MappingSetRevisionId")`
- `CREATE INDEX "IX_Sessions_FamilyId_QuestionRevisionId" ON public."Sessions" USING btree ("FamilyId", "QuestionRevisionId")`
- `CREATE INDEX "IX_Sessions_FamilyId_ReleaseId" ON public."Sessions" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_Sessions_FamilyId_StudentId" ON public."Sessions" USING btree ("FamilyId", "StudentId")`
- `CREATE INDEX "IX_Sessions_FamilyId_TaskId" ON public."Sessions" USING btree ("FamilyId", "TaskId")`
- `CREATE UNIQUE INDEX "PK_Sessions" ON public."Sessions" USING btree ("Id")`

## Sources

私有来源正文、摘要、使用范围及外部 AI 许可标记。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Title | text | 否 | 无 |
| Text | text | 否 | 无 |
| Hash | text | 否 | 无 |
| UsageScope | text | 否 | 无 |
| AllowExternalAI | boolean | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| FileId | uuid | 是 | 无 |

约束：

- `AK_Sources_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Sources_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Sources_PrivateFile_FamilyId_FileId`：`FOREIGN KEY ("FamilyId", "FileId") REFERENCES "PrivateFile"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Sources`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Sources_FamilyId_Id" ON public."Sources" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Sources_FamilyId" ON public."Sources" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Sources_FamilyId_FileId" ON public."Sources" USING btree ("FamilyId", "FileId")`
- `CREATE UNIQUE INDEX "IX_Sources_FamilyId_Hash" ON public."Sources" USING btree ("FamilyId", "Hash")`
- `CREATE UNIQUE INDEX "PK_Sources" ON public."Sources" USING btree ("Id")`

## Students

学生、当地时区、预算、内容与活动评估世代绑定。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| Name | text | 否 | 无 |
| Grade | integer | 否 | 无 |
| TimeZone | text | 否 | 无 |
| DailyMinutes | integer | 否 | 无 |
| ActiveReleaseId | uuid | 是 | 无 |
| ActiveGenerationId | uuid | 是 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |

约束：

- `AK_Students_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Students_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `PK_Students`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Students_FamilyId_Id" ON public."Students" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Students_FamilyId" ON public."Students" USING btree ("FamilyId")`
- `CREATE UNIQUE INDEX "PK_Students" ON public."Students" USING btree ("Id")`

## Tasks

固定任务身份、执行状态、实际计时及目标快照。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| Id | uuid | 否 | 无 |
| StudentId | uuid | 否 | 无 |
| ReleaseId | uuid | 否 | 无 |
| Type | text | 否 | 无 |
| Title | text | 否 | 无 |
| ReasonCode | text | 否 | 无 |
| Reason | text | 否 | 无 |
| KCId | uuid | 是 | 无 |
| QuestionId | uuid | 是 | 无 |
| ReviewTargetId | uuid | 是 | 无 |
| ResourceRef | text | 否 | 无 |
| Minutes | integer | 否 | 无 |
| ActualMinutes | integer | 是 | 无 |
| Locked | boolean | 否 | 无 |
| Mandatory | boolean | 否 | 无 |
| Status | text | 否 | 无 |
| FamilyId | uuid | 否 | 无 |
| CreatedAt | timestamp with time zone | 否 | 无 |
| CompletedAt | timestamp with time zone | 是 | 无 |
| StartedAt | timestamp with time zone | 是 | 无 |
| TrackedSeconds | integer | 否 | 0 |
| ResourceUrl | text | 是 | 无 |
| GoalSnapshots | text | 否 | '[]'::text |

约束：

- `AK_Tasks_FamilyId_Id`：`UNIQUE ("FamilyId", "Id")`
- `FK_Tasks_Families_FamilyId`：`FOREIGN KEY ("FamilyId") REFERENCES "Families"("Id") ON DELETE CASCADE`
- `FK_Tasks_Releases_FamilyId_ReleaseId`：`FOREIGN KEY ("FamilyId", "ReleaseId") REFERENCES "Releases"("FamilyId", "Id") ON DELETE CASCADE`
- `FK_Tasks_Students_FamilyId_StudentId`：`FOREIGN KEY ("FamilyId", "StudentId") REFERENCES "Students"("FamilyId", "Id") ON DELETE CASCADE`
- `PK_Tasks`：`PRIMARY KEY ("Id")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "AK_Tasks_FamilyId_Id" ON public."Tasks" USING btree ("FamilyId", "Id")`
- `CREATE INDEX "IX_Tasks_FamilyId" ON public."Tasks" USING btree ("FamilyId")`
- `CREATE INDEX "IX_Tasks_FamilyId_ReleaseId" ON public."Tasks" USING btree ("FamilyId", "ReleaseId")`
- `CREATE INDEX "IX_Tasks_FamilyId_StudentId" ON public."Tasks" USING btree ("FamilyId", "StudentId")`
- `CREATE UNIQUE INDEX "PK_Tasks" ON public."Tasks" USING btree ("Id")`

## __EFMigrationsHistory

EF Core 已应用的结构迁移版本；不是家庭业务数据。

| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |
|---|---|---|---|
| MigrationId | character varying(150) | 否 | 无 |
| ProductVersion | character varying(32) | 否 | 无 |

约束：

- `PK___EFMigrationsHistory`：`PRIMARY KEY ("MigrationId")`

索引（包含约束自动创建的索引）：

- `CREATE UNIQUE INDEX "PK___EFMigrationsHistory" ON public."__EFMigrationsHistory" USING btree ("MigrationId")`
