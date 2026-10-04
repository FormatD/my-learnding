# 评估队列、固定重建目标与消费回执

作答/判分/映射更正的Outbox追平已接入BackgroundJob，任务类型AssessmentProjection，InputRef为原事件Id，StudentId明确同家庭学生，InputPayload/Hash固定家庭/学生/作答/事件/消费者描述。消费者名称assessment/1，ConsumerReceipt唯一键(ConsumerName,EventId)。旧已处理事件没有补造任务或消费回执。

## 领取与固定目标

任务首次建立时独立保存TargetGenerationId，重试、租约到期接替和人工新轮次沿用这一标识。只有需要生成新的结果时才使用该标识；如果完整输入摘要等于当前活动世代，复用已有活动世代，并在消费回执中保存实际GenerationId，不能强迫制造无变化的新世代。已有目标出现不一致结果则GENERATION_TARGET_CONFLICT，不覆盖已有结果。

评估消费者沿用30秒保护/5秒心跳、独立领取、到期换人与提交围栏。每轮最多3次领取，包括中断；到期且耗尽时只停止失败，零新结果/消费回执，Outbox进入人工失败队列。普通故障保留2/4秒等待，长期失败停止，不因启动时扫描自动重置。人工POST /jobs/{id}:retry仍要求Parent并核对本家庭事件，增加明确重试轮次，保留旧领取历史及固定目标。未到NextAttemptAt或Retries已经耗尽时不会借任务领取绕过原队列限制。

## 输入与合并

这是“追平该学生当前已提交事件”的任务，固定的是事件/学生描述和目标身份，不是永久冻结第一次尝试时的全部判分。每次执行在家庭锁内加载最新已提交作答、有效判分、固定题目/映射引用及教学记录，原始引用校验仍执行。期间正常家庭写入等待同一锁；当前批次覆盖的所有未处理事件分别有回执。本次家庭锁读取之后提交的新事件留给下一处理，不由早期描述推算已处理。

相同学生的多个待处理事件可一次重放追平，每个事件各保存消费回执，均指向这次实际执行Job和实际结果/输入摘要；不能将只有一条队列完成标记当成全部消费证明。另一领取如果发现已被合并处理，复用原回执并结束自己的领取，不再重建。已有真实回执但完成标记缺失时，可按原回执时间恢复标记，不改原回执或源作答。旧完成标记没有回执不能反推旧消费者/时间/结果，不补造历史。

ConsumerReceipt、证据/上下文/掌握/复习、活动世代绑定、Outbox完成标记以及本次任务/领取终态在同一结果事务提交；独立持久化的是领取和固定目标。进程在结果写入后、提交前终止时这些结果全部回滚，原固定目标与Running领取保留。保护期到期后接替仍用同一个目标，旧领取保留LeaseExpired。真正更正撤销的批次、撤销记录及新旧上下文继续受原事务保护。

## 读取、导出与删除

GET /students/{id}/consumer-receipts供Parent分页读取，默认20/最多50，跨家庭404、孩子403、无效分页422。后台任务页面用“学习评估”显示新类型。学生learning-export/2追加consumerReceipts/outbox/backgroundJobs/jobLeaseAttempts字段，原字段及格式继续兼容；全家ZIP同样包含。学生删除以同家庭学生外键清除评估任务、领取和消费回执，家庭删除及加密恢复最新删除清单也闭合。TargetGenerationId是尚未产生结果的预定标识，不能用它代替实际GenerationId引用；消费回执另有实际世代外键。

旧Outbox.RetryRound保持NULL，兼容读取视为未另开人工轮次；只有实际人工恢复才记录新轮次。旧Builder/PDF任务StudentId及TargetGenerationId保持NULL，不据名称或历史结果猜测所属学生/目标。

## 验证与开放项

projection_receipt_persistence_acceptance.py验证合并两事件各自回执/同一个固定目标、另一领取及受控完成标记缺口不再建世代、原回执全部字段不改、数据库唯一键及学生删除闭合、受控耗尽状态零结果/回执及人工新轮次沿原目标恢复。worker_fault_acceptance.py实际终止进程，在短策略夹具下等待真实数据库租约到期后让两个消费者竞争，固定目标、一份回执及一组结果收敛。evidence_revocation_fault_acceptance.py实际中断判分更正，旧证据/上下文/撤销仍原子恢复。mastery_snapshot_acceptance.py继续核对活动结果与积压为同一数据库快照。

目前仍为全学生重放算法，并未完成独立增量算法或增量/全量对照。新DomainEvent日志及Outbox/回执的明确引用已补齐，详见[事件说明](domain-events.md)；日志序号尚未用于独立增量消费，非评估事件目前只记录事实。手动重建API仍同步，映射建议后台化及用户取消协议仍开放；这些不因新增消费回执而关闭。真实模型/教材/实体设备/长期试用仍按完整V1条件核对。

## 回执历史稳定翻页（2026-10-05）

GET `/api/v1/students/{id}/consumer-receipts/window`返回pageSize、total、receipts和可空nextCursor。默认20、最大50；按CreatedAt降序/Id升序，回传URL编码游标及同样页大小读取后续页。游标绑定consumer-receipts/1、家庭和该学生；更换学生、事件游标或页大小时422，孩子403，跨家庭学生404。旧GET `/students/{id}/consumer-receipts?page=1`继续原字段与页码行为。

一次读取的计数、回执和家庭版本在同一数据库快照中；请求取消传递给游标查询。跨页状态不冻结，刷新第一页看最新回执。只读取既有真实回执，不重标ProcessedAt、不创建回执、不推进消费边界，也不重算掌握。实际隔离检验见`tests/event_cursor_acceptance.py`及[事件历史说明](domain-events.md)；同时间UUID边界由共享StableReadPage的后台任务实际数据库专项验证。
