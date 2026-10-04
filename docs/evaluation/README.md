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
