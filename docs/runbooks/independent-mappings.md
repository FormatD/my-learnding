# 独立映射草稿

“内容与发布”中的独立映射草稿可直接维护题目、课时或资源的关联。选择来源草稿、正式能力库和一个对象，载入实际原映射及覆盖权重，然后保存。创建不会审核、发布、改变学生绑定或重解释旧作答；模型按用户安排稍后接入，当前直接入口只接受Manual，AI/外部Provider明确拒绝。

## 版本、来源与审核

POST `/api/v1/content/mapping-sets`接受SourceDraftId、ExpectedDraftVersion、LibraryReleaseId、OwnerType、OwnerId、OwnerRevisionId、EvidencePolicy、Items、Reason和可选Provider。调用者须有ContentEditor；最多20条关联，能力修订必须来自明确选择的未撤回正式库，不能选择已停用能力。SourceRefs严格指向原草稿/对象修订；角色、观察点、份额总和、六位小数精度和教学覆盖分别验证，手工项不填ModelScore。正文缺少正确来源版本、未知对象、伪造引用、过期版本等不会保存半套记录。重复同幂等键返回同一201结果；同键不同正文仍409。

创建事务写待审核ContentDraft、MappingSetRevision、MappingSetItem和IndependentMappingDraft。后者固定原SourcePayload/SourceHash、原草稿版本、LibraryReleaseId/Hash、提交人和维护依据，不把维护依据叫审核理由。覆盖权重变化保持原对象修订；证据映射或策略变化创建新对象修订。任何保存均追加独立映射版次，旧映射条目及旧发布绑定不改写。

MappingSetRevision初始ReviewStatus=Draft、CoverageOrigin=UnreviewedDraft，两个审核来源字段均NULL。数据库允许这种明确的待审状态；已审状态仍要求逐项决定或整份内容记录二选一，不能在草稿附上审核或在已审状态缺少来源。旧已审记录没有改状态或补造独立草稿元数据。

整份内容人工审核只在当前对象定义、归因、能力修订与所有权重精确匹配原待审容器时，将其关联本次真实ContentReviewRecord并标记ReviewedCatalog/CatalogReviewed。独立维护不是逐项审核，不制造MappingReviewDecision。若生成内容草稿后来编辑，审核的是后来实际快照：原待审容器仍待审，发布选择与后来审核内容相符的新版容器。审核与发布时固定能力库撤回均阻止使用该独立容器，且发布事务不留下部分Release/Binding。直接编辑后的新内容修订按内容编辑既有流程明确重新审核。

发布只选择已审容器；创建草稿时的无审核版本不会进入新会话。旧发布、已领取会话和历史证据固定原引用，新发布也不自动重解释旧作答，仍须明确历史映射更正。

## 查询、页面与隐私

GET `/api/v1/content/mapping-sets`返回当前家庭独立创建的容器摘要与total、offset、limit，不混入所有旧发布映射。offset默认为0且非负；limit默认为20，范围1～100。GET `/api/v1/content/mapping-sets/{id}`返回固定来源、保存的set/items、当前生成内容draft、固定库的能力和当前草稿校验警告。draft可能经过后续编辑，固定source/set/items仍保留创建时事实。两种读取采用RepeatableRead；详情只针对有独立来源记录的容器，不将旧容器描述成来自这个新入口。

页面对象查找/选择与保存历史均每页20项，创建时捕获真实原草稿版本；后续别的读取更新全局ETag也不能绕过ExpectedDraftVersion。失败不清空输入。载入缺失于所选库的原能力会明确显示待校正，不能静默选择替代能力；覆盖与证据预算分开填写。保存的维护原因和内容使用文本转义。

孩子不能调用上述读写入口（403）；其他家庭来源/库/详情404，列表只返回自身记录。学生JSON导出包含实际学习所用容器对应的independentMappingDrafts；全家ZIP和加密数据库快照含全部私有表。家庭删除级联清理元数据、映射及审核；备份恢复应用最新删除清单。

## 验证入口与限制

```sh
npm run build --prefix src/web
.tools/dotnet/dotnet build src/server --no-restore
.tools/dotnet/dotnet build tests/persistence --no-restore
.tools/dotnet/dotnet run --project tests/acceptance --no-restore
python3 tests/openapi_acceptance.py --regression
python3 tests/openapi_acceptance.py --paper-regression
python3 tests/independent_mapping_migration_acceptance.py
npm run test:e2e --prefix src/web -- independent-mapping.spec.ts
```

隔离API核对三类对象、真实审核后绑定、后来编辑不误审旧容器、过期与撤回、错误整批回滚、导出及家庭/孩子权限。旧库专项逐列核对原映射及审核，验证数据库状态/审核来源约束与删除。浏览器验证实际资源权重维护、错误/过期输入保留、审核发布、绑定不自动变化与窄屏。测试家庭和构造教材内容不算正式人工审核、真实模型质量或四周试用；空库恢复耗时不证明独立盘或正式灾难应用切换。
