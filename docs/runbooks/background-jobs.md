# 后台任务领取、心跳与中断恢复

BackgroundJob采用设计§13.3的任务字段：Type/InputRef/IdempotencyKey、固定InputPayload/Hash、Queued/Running/Succeeded/Retrying/Failed/Cancelled、AttemptCount/MaxAttempts、NextRunAt、LeaseOwner/LeaseExpiresAt/HeartbeatAt和LastErrorCode。当前建库候选、本地PDF解析及作答Outbox/评估追平已实际接入；评估独立消费回执及固定目标见[评估消费手册](projection-consumption.md)。明确事件日志、家长重建、映射建议及可选任务取消已接入；独立事件游标/增量和旧同步兼容入口仍开放。

## 领取与提交

等待执行的BuilderRun按原排队时间建立任务，Type分别为BuilderCandidates和ParsePDF，InputRef为原运行，IdempotencyKey以原运行身份固定。CreatedAt沿用原任务排队时间以保留顺序；这不是物理插入时间或已领取的证明。只有真实领取才建立JobLeaseAttempt并保存数据库实际开始时间。已完成旧运行不会补造任务或领取；旧排队运行建立当前描述快照，继续受原输入兼容/校验限制，不补造旧模型配置。唯一键(FamilyId,Type,InputRef)和(FamilyId,Type,IdempotencyKey)防重复建立。

独立事务用数据库时间、FOR UPDATE SKIP LOCKED领取一条到期Queued/Retrying或已过期Running，生成新的LeaseOwner。领取事务先提交，结果事务尚未开始时进程退出也留下记录。默认保护30秒、每5秒心跳；策略保存在任务中，数据库约束保护尝试上限1～10、保护3～300秒、心跳至少1秒且不超过保护期的一半。当前固定默认值，不宣称租约策略已经纳入BuilderRun.ModelConfigHash。

心跳使用独立连接，仅在Running、相同领取人且尚未过期时续期，不能让已失效领取人复活。续期失败/领取人变化/已到期会取消本次工作。执行前按同家庭读取原运行，核对任务固定描述/摘要及原来源、许可和模型配置。提交前在结果事务锁住仍未过期且领取人相同的任务行；旧领取人无法通过该围栏。任务终态、领取回执和候选/运行/BuilderAttempt同一事务提交。领取之前及处理期间发生的BuilderCall事实仍独立持久化。

到期接替把旧Running领取记录标记LeaseExpired并保留错误及结束时间，再建立新领取记录。旧领取人即使仍活着，也不能提交新的结果。数据库家庭锁继续保护同家庭结果；租约增加持久化领取、超时接替和提交权核对，不代替来源许可、调用预算或发布审核。

## 重试与失败

初次领取和自动重试共最多4次，进程中断也计入。普通处理故障保留原2/4/8秒退避；未到时间不会领取，其他任务可继续。达到上限的过期任务只做失败清理，运行进入JOB_ATTEMPTS_EXHAUSTED，不再进入提供者。终态失败不自动重领。负责人/有内容编辑权限的家长使用原建库任务的人工恢复入口，必须填写依据，验证原输入/许可，开启新的重试轮次；保留全部旧JobLeaseAttempt和BuilderAttempt。新轮次恢复原任务描述，不能借重试换入最新内容或另一提供者。

取消等待、进程终止及租约到期不能证明外部提供者已经停止。Started/Failed/Cancelled调用仍按调用预算占名额；接替可能明确停止于BUILDER_CONCURRENCY_LIMIT，必须先实际核对并追加调用结束记录。已经返回但结果事务未提交时，可接替重新生成，账本保留不同物理调用，最终只有一份候选；不宣称物理只调用一次。

Cancelled状态在模型/数据库/领取器中受保护且不再领取，可选任务支持带依据的[用户取消](job-cancellation.md)，实际执行停止另行确认。正常服务停止取消工作并回滚结果，持久化Running保留到过期后接替；不伪装成已经结束的外部调用。

## 页面与数据

GET /background-jobs给ContentEditor分页读取本家庭任务及领取记录，默认20/最多50，孩子403，列表按家庭隔离。辅助建库页面显示当前状态、最近续期、保护时间、轮次/上限和旧中断记录。原/jobs仍是原作答投影待处理队列及人工重试入口，接口语义不改。

BackgroundJob和JobLeaseAttempt随全家ZIP/加密数据库快照导出，家庭删除及恢复最新删除清单闭合；领取记录用同家庭外键关联任务。任务状态、尝试范围、Running必须有领取人/到期/心跳及其他状态清除领取人/到期，由数据库检查约束保护。InputRef是按Type解释的通用引用，当前执行器另用FamilyId+运行Id验证，不凭全局Id读取其他家庭输入。

## 已验证与开放项

job_lease_persistence_acceptance.py实际两连接领取竞争、心跳跨完整保护周期、结果与成功回执事务提交、到期换人/旧人真实写入结果后围栏拒绝且事务回滚、耗尽尝试仅失败清理、未来退避/Cancelled不领取、数据库缺领取人及重复任务约束。builder_call_crash_acceptance.py真实终止进程，在3秒/1秒短策略夹具下等待实际数据库保护期到期，不改写到期时间，再验证调用事实、两次领取与一份候选。原建库保存故障、有界退避/人工重试、全家导出及加密恢复/删除不复活继续通过。

明确事件日志、[映射准备](mapping-background.md)与[家长重建](assessment-rebuild.md)已接入；仍开放：独立事件游标/增量及全量核对、旧同步兼容接口收敛、生产设备/负载及跨机器故障测试，真实付费提供者价格/请求Token与账单联调。当前本地一次性数据库与模拟提供者不能关闭这些条件。


可选任务取消及明确恢复已接入，实际停止与提供者费用分别核对，见[取消手册](job-cancellation.md)。必需学习结果同步不会被取消。

## 任务历史稳定翻页（2026-10-05）

家长维护页面使用GET `/api/v1/background-jobs/window?pageSize=20`。响应保留jobs与attempts，另有pageSize、total、可空nextCursor；后续请求回传URL编码后的cursor和同样页大小。默认20、最大50，按CreatedAt降序/Id升序取原边界后记录。新任务插入顶部不会挤动原后续页；上一页重新读取原位置，刷新回第一页以查看最新记录。读取失败保留当前页，离页迟到读取不写回。旧GET `/background-jobs?page=1`继续原页码语义和字段。

一次页读取的计数、任务、领取记录与家庭版本来自同一RepeatableRead事务，领取记录仅包含当页任务；查询传递请求取消。StableReadPage固定background-jobs/1读取范围、家庭、过滤值与页大小，未知版本/范围/家庭/大小、畸形或超长游标422，孩子403。游标没有签名，不是来源证明；权限和家庭隔离由当前会话/数据库筛选执行。跨页不是历史快照，状态、心跳、领取和总数可以继续变化，家庭ETag不是所有任务状态的独立版本。没有更改Worker领取、续期、取消或提交围栏。

`tests/background_cursor_acceptance.py`接入实际隔离接口检查：55条Cancelled终态分页夹具使用有效输入摘要、独立InputRef/命令键及相同创建时间，没有实际执行或伪造领取；页间插入顶部记录后原夹具各出现一次，并检查UUID排序、领取所属当页、错误范围/家庭/大小和孩子权限，结束清除夹具。BACKGROUND_CURSOR_BROWSER=1时再运行实际隔离浏览器分页/刷新及受控503保留原页专项。
