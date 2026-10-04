# 混合运算固定评测准备

[打开人工标注工作页](mixed-operations-review-v1.html)。这是北师大版第一单元《混合运算》的原创开发题包，具体教材印次及正式教材内容仍待核对。160题、9个能力定义、15个已记录变式组；开发集104题，独立检查集56题。全部初始标签为Pending，没有已批准的人工金标准。工作页不发布内容，不连接模型。

请逐题独立核对题目是否有效、是否可测、主能力及判断依据，填写审核人后记录该题。草稿答案与原关联仅作参考。浏览器尝试保存进度；即使正常保存，也请下载标签文件并按私有资料保管。导入文件须对应固定题包和原输入摘要。下载文件不意味着完成正式审核，也不会改变题包或数据库。

## 固定版本与隔离

`mixed-operations-draft-v1.json`固定题目输入、库定义、原题摘要及分组。SHA-256校验标识：`bfb877f7036ea5cd614a27675b6b56539aa83e30cc1846a84d3eb1dd9463837c`。相同VariantGroupId或NFKC规范化、数字替换后的完全相同题面按连通分组，整组划入同一集合；不能保证所有语义改写已识别。独立检查集不要参与提示词调优。更改题包须生成新版本，不覆盖已有固定文件。

模型输入导出会去掉原题mappings，并不包含人工标签或草稿主能力参考。答案与讲解仍作为题目资料提供。人工标签参考文件与模型输入必须分开使用。

```sh
python3 scripts/k1_evaluation.py inputs --dataset docs/evaluation/mixed-operations-draft-v1.json --partition Holdout --output .local/evaluation/new-holdout-inputs.json
python3 scripts/k1_evaluation.py score --dataset docs/evaluation/mixed-operations-draft-v1.json --labels /path/to/reviewed-labels.json --results /path/to/results.json --output .local/evaluation/new-report.json
```

输出路径必须尚不存在。标签模板为`mixed-operations-labels-template-v1.json`；初始待标注报告为`pending-reference-report-v1.json`，状态NotEvaluated，不含真实模型结果或人工耗时。

## 结果记录约定

结果文件格式`k1-results/1`，必填datasetHash、partition、producer.kind（Controlled、Mock或UserSuppliedProvider）和predictions。每条预测必填id、inputHash、status（Mapped、Rejected、Skipped）、primaryKCIds及mappedKCIds。主能力是全部映射的子集；拒绝或跳过必须两个列表均为空。有效不可测题允许只有上下文映射、没有主能力。所有能力标识必须来自固定库。

可选candidates记录questionId和citations；每条引用须具有inputHash、locator=`question:<题目id>`和题干中真实存在的quote。该本地适配格式只检查题干引用，不能冒充已验证实际Builder片段链。可选tasks记录唯一id、Completed/Failed、firstSchemaPassed和repairedSchemaPassed，两个通过标志不能同时为真；完成任务至少通过其一。可选prediction.review记录reviewer、带时区recordedAt、initialSeconds及correctionSeconds。时间必须实际测量，人工金标准标注时间不代替映射审核时间。

可选libraryAudit记录审核人、带时区时间、依据及nodes；节点记录id、nodeKind（Measurable/Directory）、published、redundantDuplicate、formalAssociations（Question/Lesson/Resource及标识）。这是明确提供的审查记录，工具不从数据库独立核实。

准确率分母为已明确批准且有效、可测的题数，缺失、拒绝或跳过仍计错；必须精确匹配主能力集合。覆盖率分母为全部已批准有效题，包括不可测活动，要求实际Mapped。未标注题数另列，部分标注不能视为完整金标准。首轮及修复Schema比例分别以完成任务为分母，失败任务另列；应同时检查失败数，不能只看通过比例。引用追溯以全部候选为分母。重复与孤儿比例只计已发布Measurable节点，排除Directory和草稿。全部有效题都有明确首次审核及更正耗时时，才计算每100题分钟数。

默认目标为主能力准确率≥85%、首轮Schema≥95%、引用追溯100%、重复≤5%、审核≤30分钟/100题。输入记录齐全时，仅报告SuppliedRecordsMeetTargets或BelowTargets；缺少标签、候选、任务、库审查或耗时时NotEvaluated。孤儿率独立报告，无额外捏造阈值。所有报告始终`formalV1ExitProven=false`；Controlled/Mock或自行提供记录达标，不能关闭真实模型、正式人工金标准及完整V1验收。

## 验证

```sh
python3 tests/k1_evaluation_acceptance.py
# 设置已安装浏览器的CHROMIUM_PATH后，增加 --browser 验证独立工作页。
```

测试标签明确为受控夹具，保存在临时目录/隔离浏览器，不写入正式标签模板。此工具尚未接入真实模型结果采集或独立人工签署流程。

## 从实际映射运行导出评测结果

`scripts/k1_mapping_results.py`读取本机维护接口`GET /api/v1/builder/mapping-runs/{id}`保存的完整JSON响应。工具只读取文件，不登录、不请求网络、不改库、不调用模型。目前只接受现有Mock提供者、固定模拟空间及mapping-suggestion/1；Manual直接复制作者关联，不能作为模型预测输入，因此拒绝。实际模型接入后须增加明确的新适配版本，不能更名冒充。

评测任务应只选择同一冻结集合中的Question。混有课时、资源、其他集合、重复题、修改后的题目或不同能力修订时明确拒绝，不自动过滤或重绑。保存响应含家庭和审核资料，请按私有数据保管；规范化文件只保留预测与必要引用/摘要，不带完整源题包或审核人。

```sh
python3 scripts/k1_mapping_results.py --dataset docs/evaluation/mixed-operations-draft-v1.json --detail /path/to/saved-mapping-run-detail.json --stage OriginalSuggestion --output .local/evaluation/new-original-results.json
python3 scripts/k1_mapping_results.py --dataset docs/evaluation/mixed-operations-draft-v1.json --detail /path/to/saved-mapping-run-detail.json --stage ReviewedMapping --output .local/evaluation/new-reviewed-results.json
```

两份结果分别计分，不能把校正后的准确率作为模型原始效果。OriginalSuggestion始终使用原始建议，即使后来接受、校正或拒绝；其无效映射记Rejected。ReviewedMapping只使用明确接受且校正摘要有效的映射，拒绝记Rejected，待处理记Skipped，不用原建议填补。遗漏题由计分工具保留在金标准分母中。Context/Prerequisite不计主测量能力，只有Primary、WholeItem/StepObserved且正证据份额进入主能力集合。

工具核对原来源字节摘要、各题输入摘要/修订、完整冻结能力定义、建议运行/家庭、审核决定/状态及原始/校正负载摘要；映射结构、份额、角色、步骤、能力修订与来源须满足明确条件。每题保存runId、runInputHash、suggestionId、原建议摘要、审核决定引用、阶段及冻结对象来源；capture保存完整响应的规范化摘要。保存文件可以由持有者修改，这些校验不构成独立服务端签署或数据库审计证明；捕获说明始终标为LocalSavedResponseChecksNotIndependentServerAttestation。能力库完整发布负载不在此接口响应中，libraryHash仅保留原值，不声称独立复算该发布摘要或请求配方。

映射运行接口没有实际审核时长、候选Schema逐次调用、题干引文和库重复审查，因此导出不补造这些指标。ReviewedAt是时间点，不能推算耗时。正式人工金标准缺失时仍NotEvaluated。当前适配只覆盖题目映射，不将其当作KC候选抽取或真实模型质量证明。

专项运行：`python3 tests/k1_mapping_results_acceptance.py`。实际后台/审核接口验收：`python3 tests/k1_mapping_api_acceptance.py`，使用独立临时数据库、隔离测试家庭和明确标注的受控发布；验证56题真实后台输出、校正/拒绝/待审核分离、CLI读文件与摘要、跨家庭404/孩子403以及零证据/掌握副作用，结束清理，不修改正式标签。

## 候选任务结构与来源统计

`scripts/k1_builder_report.py`读取已有`GET /api/v1/builder`完整响应文件，用重复的`--run-id`明确选择任务；只接受现有Mock/fixture/1的Candidates及kc-candidate/1或/2，不混入PDF解析任务。

```sh
python3 scripts/k1_builder_report.py --builder /path/to/saved-builder-response.json --run-id <候选任务id> --run-id <另一任务id> --output .local/evaluation/new-builder-report.json
```

输出为独立`k1-builder-report/1`，不能伪装为题目映射结果或自动合并为固定检查集质量报告。Completed、Failed、Queued及尝试数分别列出；失败原因保留。结构比例使用完成任务为分母，仅当实际保留的成功BuilderProtocolResult、Calls、Repaired、版本与全部候选ProtocolPayload对应时统计首轮/修复通过。多份成功记录、输出混合版本/字段或运行/家庭冲突明确拒绝。这里读取服务端保存的成功校验事实，没有重新校验模型首轮原始响应，也没有将调用账本Returned当作Schema通过。

旧任务缺少成功ProtocolResult时，完成任务仍在分母，未知数单列，首轮/修复比例显示null；旧候选缺少ProtocolPayload时同样保留候选分母并显示来源比例未知，不从展示Quote补造结构引用。已记录来源不合法时计入分母并计为无效引用。每个引用检查实际片段存在、来源/家庭对应、真实非空引文、主引用及原片段包含于来源文本；输出片段定位、片段/引文摘要，不带全文引文。文本上传核对原文本SHA-256；PDF来源原Hash对应文件，响应没有文件字节，明确标记OriginalFileHashNotRecomputed，不能宣称复算原PDF摘要。

输出保留选择与完整响应摘要、原任务输入摘要、协议版本、成功尝试引用及协议摘要。这些是本地保存响应的交叉检查，不构成独立服务端签署。Mock来源引用通过不表示语义提取正确，更不能作为正式100%追溯验收；许可、正式内容及独立证据仍需核对。语义质量固定NotEvaluated，formalV1ExitProven始终false；不推算金标准、映射准确率、审核耗时或费用。

边界专项：`python3 tests/k1_builder_report_acceptance.py`（首轮/修复分列、失败/排队、旧缺失未知、错误引用保留分母、记录冲突拒绝）。实际接口：`python3 tests/k1_builder_api_acceptance.py`使用临时数据库，执行真实首轮成功两候选、101片段超限失败、实际记录/引用与CLI核对；修复统计分支本轮由明确受控文件夹具覆盖，未声称实际修复后成功的持久任务验收。文件不会覆盖已有报告，测试完成自动清理。
