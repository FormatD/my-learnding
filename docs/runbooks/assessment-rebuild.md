# 家长后台评估重建

家长在证据页“重新评估已有记录”填写依据，提交后显示真实队列状态并自动查询进度。完成前保留当前评估；成功提交后刷新评估版本与证据，相同输入复用已有世代。新入口 POST /students/{id}/mastery:rebuild 返回202及请求/任务/预定目标，GET /students/{id}/rebuild-requests 列出最近50批，/{requestId}查询单批。孩子403、其他家庭404，依据不能为空且最多4000字。同学生有活动重建时复用该请求，数据库部分唯一索引另防重复活动任务。

AssessmentRebuildRequest与BackgroundJob及排队审计同请求事务保存。不可修改的请求保存家长、理由、原时区、摘要算法/评估规则版本、预定目标、申请时已有世代信息及原始描述摘要。InputMode=LatestCommittedUnderLock表示执行时在家庭锁内读取最新已提交的作答和更正；申请时的BaseGeneration不是冻结全部作答的输入快照。排队后的新作答会被追平，学生时区或规则配置变化明确失败，不能静默使用新的配置。世代实际InputHash与Cursor保存在独立不可修改的AssessmentRebuildResult，不能把请求描述摘要当作实际计算摘要。

任务使用统一持久化领取、独立心跳与提交围栏，最多4次实际执行，包括中断。结果、证据/上下文/撤销、活动指针、每事件ConsumerReceipt、Outbox完成、实际AssessmentApplied事件以及任务/领取成功同事务提交；旧进程失去租约后不能提交。相同输入核对允许实际GenerationId与预定TargetGenerationId不同，结果明确记录ReusedGeneration，不伪造新世代。重复处理核对原结果与事件引用，不改写历史回执。

未知执行异常有界退避重试，达到上限停止。原家长权限撤销、请求/规则/学生配置不一致明确失败。家长可对REBUILD_PROCESSING_FAILED、JOB_ATTEMPTS_EXHAUSTED或JOB_REQUESTER_FORBIDDEN填写恢复依据，通过 POST /students/{id}/rebuild-requests/{requestId}:retry 开人工新轮次；已有结果或非失败状态409，需更换规则/配置的错误必须重新准备。恢复保存实际家长、原请求/任务/目标/描述摘要及本轮依据，执行器要求本轮授权匹配且家长当前仍有权限。原请求人和输入不改，旧领取保留。学生/家庭导出保留请求、实际结果与任务/事件关联，隐私删除和最新删除清单恢复一并清理。

新世代InputVersion=assessment-input/2。实际输入摘要纳入学生时区，避免时区变更误用旧复习日期。旧世代新增列保持NULL，不回填、重写旧摘要或修改旧日程；受控跨午夜案例验证新世代采用新时区日期，原世代保持不变。评估数学规则版本未改变。

验证入口：rebuild_job_persistence_acceptance.py验证排队零假完成、活动请求去重、新作答追平、相同输入复用、不可修改/删除、时区变化和权限边界；人工恢复数据库夹具明确为受控持久化授权，并非HTTP恢复接口测试。rebuild_job_fault_acceptance.py真正终止结果提交前进程，等待数据库租约到期后双消费者竞争，一份固定结果且旧领取保留。openapi_acceptance.py --rebuild-regression使用独立服务验证202/进度/实际结果与事件/导出/权限，保留真实认证限流。assessment-context.spec.ts实际浏览器验证页面提交、完成刷新及导出。共享评估消费与更正故障另做回归。

旧同步 POST /students/{id}:rebuild 保留兼容，新页面与设计入口已迁入后台。当前学生有Queued/Running/Retrying请求，或最新请求已Failed/Cancelled时，旧入口409 REBUILD_JOB_REQUIRED，不更新世代、证据、日程、来源或审计；应查询原请求，明确恢复或重新准备。最新请求Succeeded还需实际结果/检查点/应用事件引用有效，否则422 REBUILD_RESULT_INVALID。明确新请求真实完成后可重新使用同步兼容；相同输入复用原世代，未来新输入仍按原同步契约计算。没有后台请求的学生保留原同步契约，同家庭另一学生不继承他人的停止状态。此保护不代表所有兼容路径已后台化。独立增量状态已接入；普通家长重建可复用原前缀，只计算后缀但保留新固定世代。在线新作答在活动世代追加，源前缀或配置改变时受控切换新世代；独立评估顺序进度已接入，全量源读取优化及非评估消费者继续开放。测试家庭、合成日期与本机临时恢复不是正式教材审核、实体平板或四周试用证据。


可选任务取消及明确恢复已接入，实际停止与提供者费用分别核对，见[取消手册](job-cancellation.md)。必需学习结果同步不会被取消。


独立增量状态及明确完整源重算已接入：普通请求可复用不变输入/前缀；状态异常时不会静默覆盖。家长填写依据点击“从原始记录完整重建”，对应POST /students/{id}/mastery:full-rebuild，202排队并冻结ForceFull=true，实际完整计算并保存新目标，原快照保留。仍核对来源、规则、时区和权限；已有普通活动请求409，先取消原请求。详见[增量状态手册](incremental-assessment.md)，同一活动世代在线追加已验证，历史结果关联当时实际状态，独立评估消费进度已接入，详见[顺序消费](assessment-consumption.md)，源读取优化继续开放。

旧入口状态保护验收：tests/rebuild_compatibility_acceptance.py验证Queued、真实领取Running、通过实际领取提交的受控Retrying、真实取消及执行器因家长权限撤销产生的Failed；拒绝请求前后19张业务表所有原字段相同。明确重新准备后真实后台完成、同输入原字节复用及另一学生的原兼容入口通过。tests/rebuild_compatibility_api_acceptance.py实际HTTP验证排队409、真实取消后409、明确新请求202/实际成功/原取消保留、同输入200复用、家庭404及孩子403。重复导出比较仅排除动态exportedAt，其他实际字段保留；必需作答同步独立完成，不以可选重建取消阻止它。受控退避并不是外部服务真实故障证明。
