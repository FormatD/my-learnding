#!/usr/bin/env python3
"""Read PostgreSQL catalog metadata only; never export application rows or credentials."""
import argparse
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[1]
PURPOSES = {
    "AssessmentConsumerCursor": "实际顺序消费进度，关联原事件、回执与不可修改应用记录；旧数据不补造游标",
    "AssessmentCheckpoint": "实际评估的增量状态、原输入前缀摘要与不可修改状态负载；旧世代不补造。",
    "AssessmentRebuildRequest": "家长后台重建的不可修改请求，固定学生、规则版本、时区和目标世代；基线为提交时已完成结果，处理时追平最新已提交输入。",
    "AssessmentRebuildResult": "实际后台重建结果回执，关联原请求、任务、实际世代和应用事件；与评估及事件消费同事务提交。",
    "MappingPreparation": "后台映射准备的不可变原输入、请求人、固定输出运行标识及统一任务引用；建议成功提交后才产生运行和待审结果，不补造历史领取。",
    "DomainEvent": "关键领域事件的不可修改日志，保存服务器顺序、明确类型与版本、原始负载摘要及实际分派目标；历史缺失不补造。",
    "ConsumerReceipt": "按消费者/事件唯一的实际评估消费回执，与结果、队列及领取终态同事务提交，不补造旧历史。",
    "BackgroundJob": "后台工作固定输入、领取人/到期/心跳、尝试上限与当前状态；建库及PDF已接入。",
    "JobLeaseAttempt": "每次实际领取及中断/到期历史；结果与最终领取回执在同一事务提交。",
    "EvidenceRevocation": "已确认更正导致的错误历史证据撤销；原证据不改写，同一证据最多撤销一次。",
    "AssessmentContext": "每世代每作答的固定判分、映射、规则和准入状态；历史缺失不回填。",
    "Accounts": "家长账号；口令仅保存哈希。",
    "Alias": "人工审核的能力别名。",
    "Attempts": "原始作答及不可覆盖的提交身份、全局事件序号。",
    "Audits": "操作审计；StudentId 为空的旧摘要不推定学生归属。",
    "AuthSessions": "家长/孩子认证会话；仅存令牌哈希，恢复时清除。",
    "Availabilities": "学生日期学习预算与学校作业预留。",
    "BuilderBudgetPolicy": "家庭当前调用费用/Token上限与并发名额；独立预算锁保护预留与结算。",
    "BuilderBudgetReconciliation": "家庭负责人追加的调用结束和费用/用量核对，原调用事实不改写。",
    "BuilderCall": "独立提交的逐次调用事实、执行身份、实际用量/费用或未知状态；候选回滚不删除，旧历史不补造。",
    "BuilderAttempt": "建库运行每次处理的状态、错误与重试轮次。",
    "BuilderRuns": "辅助建库运行及冻结的输入、模型和提示版本。",
    "Candidates": "建库候选、片段引用及人工审核决定。",
    "Chunks": "本地来源分段及原文位置。",
    "Commands": "持久幂等请求、原始请求摘要及响应缓存；恢复时清除。",
    "ContentIdentity": "家庭内内容稳定身份及测量定义签名。",
    "ContentRevision": "稳定身份下不可变内容修订。",
    "CorrectionBatch": "学生历史映射更正的预览、依据与确认。",
    "CorrectionItem": "更正批次内首次作答及生效映射版本。",
    "Drafts": "可编辑内容目录、审核状态与审核人。",
    "Embedding": "内容修订向量及空间；当前为 Mock，不表示真实模型质量。",
    "Evidence": "评估世代内由有效作答/判分产生的能力证据。",
    "Families": "家庭、负责人及并发版本。",
    "FamilyMembership": "家庭账号与显式角色。",
    "Generations": "整学生重放的评估世代及事件游标。",
    "GoalChange": "固定目标的版本与设置变更历史。",
    "Goals": "固定目标范围、日期、频率与优先级。",
    "Gradings": "作答判分及追加更正；原始作答不覆盖。",
    "KCChangeProposal": "能力拆分/合并/替换的人工提案。",
    "KCChangeProposalItem": "提案中的来源/目标能力与固定修订。",
    "KCProposalEvent": "提案审核和状态变更历史。",
    "KnowledgeMigration": "能力变更关系元数据；不迁移概率、证据或复习。",
    "Masteries": "指定评估世代的能力状态、Beta 参数与覆盖。",
    "ContentReviewRecord": "整份内容人工审核；冻结草稿版本、原内容与审核者，区分明确备注与命令确认并关联实际发布。",
    "MappingRun": "映射建议运行；冻结原草稿、对象版本和正式能力库输入。",
    "MappingSuggestion": "题目/课时/资源原始建议与排序，仅经人工审核生成草稿。",
    "MappingReviewDecision": "不可覆盖的映射审核依据、原始/校正摘要、审核人及所建草稿。",
    "IndependentMappingDraft": "独立映射草稿的固定来源、能力库、提交人及维护原因；创建不代表审核。",
    "MappingSetRevision": "统一映射修订；待审草稿无审核来源，已审来源二选一，固定对象及能力修订。",
    "ReleaseMappingSet": "新发布对实际映射容器的不可变选择；旧无容器的快照不补造。",
    "MappingSetItem": "映射修订的固定能力版本、教学覆盖和独立证据份额。",
    "Outbox": "事务内写入的后台重放队列、处理及重试状态。",
    "PaperWrong": "纸质错题、原图、人工归因及正式代录关联。",
    "ParentBurdenRecord": "人工填写投入及不可覆盖的更正链；不是停留时间。",
    "Placements": "固定任务在计划修订中的位置和锁定状态。",
    "PlanRevisions": "计划草稿/发布修订、预算与规则版本。",
    "Plans": "学生日期计划及当前草稿/发布绑定。",
    "PrivateFile": "私有附件字节、类型、摘要及生命周期。",
    "ProgressChange": "学校进度确认、更正和撤回历史。",
    "Progresses": "学生日期课时进度及当时内容版本。",
    "ReleaseItem": "发布版本对稳定身份和不可变修订的固定引用。",
    "Releases": "不可变内容目录快照、发布序号及撤回标记。",
    "Reviews": "评估世代内错题/能力复习阶段与到期日。",
    "Sessions": "任务领取时固定的内容版本和作答会话。",
    "Sources": "私有来源正文、摘要、使用范围及外部 AI 许可标记。",
    "Students": "学生、当地时区、预算、内容与活动评估世代绑定。",
    "Tasks": "固定任务身份、执行状态、实际计时及目标快照。",
    "__EFMigrationsHistory": "EF Core 已应用的结构迁移版本；不是家庭业务数据。",
}

SQL = r'''
BEGIN TRANSACTION ISOLATION LEVEL REPEATABLE READ READ ONLY;
SET LOCAL statement_timeout = '30s';
WITH tables AS (
 SELECT c.oid, c.relname FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
 WHERE n.nspname='public' AND c.relkind='r'
)
SELECT json_build_object(
 'formatVersion', 1,
 'migrations', (SELECT coalesce(json_agg(json_build_object('id',"MigrationId",'productVersion',"ProductVersion") ORDER BY "MigrationId"),'[]') FROM public."__EFMigrationsHistory"),
 'tables', (SELECT coalesce(json_agg(json_build_object(
  'name',t.relname,
  'columns',(SELECT coalesce(json_agg(json_build_object('name',a.attname,'type',format_type(a.atttypid,a.atttypmod),'nullable',NOT a.attnotnull,'identity',a.attidentity,'generated',a.attgenerated,'default',pg_get_expr(d.adbin,d.adrelid)) ORDER BY a.attnum),'[]') FROM pg_attribute a LEFT JOIN pg_attrdef d ON d.adrelid=a.attrelid AND d.adnum=a.attnum WHERE a.attrelid=t.oid AND a.attnum>0 AND NOT a.attisdropped),
  'constraints',(SELECT coalesce(json_agg(json_build_object('name',k.conname,'kind',k.contype,'definition',pg_get_constraintdef(k.oid,true),'validated',k.convalidated,'deferrable',k.condeferrable,'initiallyDeferred',k.condeferred) ORDER BY k.conname),'[]') FROM pg_constraint k WHERE k.conrelid=t.oid),
  'indexes',(SELECT coalesce(json_agg(json_build_object('name',i.relname,'unique',x.indisunique,'valid',x.indisvalid,'definition',pg_get_indexdef(x.indexrelid)) ORDER BY i.relname),'[]') FROM pg_index x JOIN pg_class i ON i.oid=x.indexrelid WHERE x.indrelid=t.oid)
 ) ORDER BY t.relname),'[]') FROM tables t)
);
COMMIT;
'''


def cell(value):
    return str(value).replace("|", "\\|").replace("\r", " ").replace("\n", " ").replace("`", "&#96;")


def render(schema):
    names = {t["name"] for t in schema["tables"]}
    unknown = names - PURPOSES.keys()
    if unknown:
        raise ValueError("新增表需补充用途说明：" + ", ".join(sorted(unknown)))
    lines = ["# 实际数据库数据字典", "",
             "由 `scripts/schema_dictionary.py` 从 PostgreSQL public 目录的只读事务生成。仅包含结构及迁移版本，不包含家庭记录、来源正文、附件、口令或连接配置。", "",
             f"当前 {len(names)} 张表；列类型、数据库默认值、主键、外键删除规则、唯一性及索引均以实际数据库为准。对应机器可读快照：[schema.json](data/schema.json)。", "",
             "## 使用边界", "",
             "- `FamilyId` 是家庭范围，外键约束之外仍必须通过服务端授权；内容快照中的 UUID 不等于独立数据库外键。",
             "- `Id` 是稳定行身份；`CreatedAt` 是创建时间。应用初始化值不等于数据库默认值；下表的“无”表示数据库没有默认表达式。",
             "- `Version`、修订号、事件序号和家庭 ETag 各有作用，不能互换。身份列按数据库定义生成。",
             "- 目录和部分状态存于 text JSON 快照；能力、题目、课时、映射的嵌套字段见 `src/server/Domain/Models.cs` 的 Catalog 等记录类型。此字典不虚构独立实体表。",
             "- 当前投影通过学生活动世代读取；保留历史世代不表示其证据同时生效。更正追加新记录，原始作答与领取版本保留。",
             "- 全家 ZIP 与私有恢复有专用范围和删除清单规则；不要用此结构清单替代隐私导出/恢复工具。", "",
             "## 已应用迁移", "", "| 迁移 | EF 版本 |", "|---|---|"]
    for migration in schema["migrations"]:
        lines.append(f"| {cell(migration['id'])} | {cell(migration['productVersion'])} |")
    for table in schema["tables"]:
        lines += ["", f"## {table['name']}", "", PURPOSES[table["name"]], "",
                  "| 字段 | PostgreSQL 类型 | 可空 | 数据库默认值/生成规则 |", "|---|---|---|---|"]
        for column in table["columns"]:
            default = column["default"] if column["default"] is not None else "无"
            if column["identity"]:
                default = "IDENTITY " + {"a": "ALWAYS", "d": "BY DEFAULT"}[column["identity"]]
            if column["generated"]:
                default = "GENERATED " + str(default)
            lines.append(f"| {cell(column['name'])} | {cell(column['type'])} | {'是' if column['nullable'] else '否'} | {cell(default)} |")
        lines += ["", "约束：", ""]
        for item in table["constraints"]:
            flags = []
            if not item["validated"]:
                flags.append("未验证")
            if item["deferrable"]:
                flags.append("可延迟；" + ("初始延迟" if item["initiallyDeferred"] else "初始即时"))
            suffix = "（" + "；".join(flags) + "）" if flags else ""
            lines.append(f"- `{cell(item['name'])}`：`{cell(item['definition'])}`{suffix}")
        lines += ["", "索引（包含约束自动创建的索引）：", ""]
        for item in table["indexes"]:
            lines.append(f"- `{cell(item['definition'])}`" + ("（无效）" if not item["valid"] else ""))
    return "\n".join(lines) + "\n"


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--psql", default=os.environ.get("PSQL", "psql"))
    parser.add_argument("--output-dir", type=Path, default=ROOT / "docs")
    parser.add_argument("--check", action="store_true", help="Compare saved artifacts; never write files.")
    args = parser.parse_args()
    # libpq uses PGHOST/PGPORT/PGUSER/PGDATABASE/PGPASSFILE. Never put secrets in argv.
    try:
        result = subprocess.run([args.psql, "-X", "-q", "-A", "-t", "--set=ON_ERROR_STOP=1"],
                                input=SQL, text=True, capture_output=True, timeout=45)
    except (OSError, subprocess.TimeoutExpired):
        sys.exit("结构读取失败；客户端不可用或已超时，未输出连接信息。")
    if result.returncode:
        # stderr may contain connection data. Keep it out of generated documentation and logs.
        sys.exit("结构读取失败；请核对本地数据库连接和迁移，未输出连接信息。")
    try:
        schema = json.loads(result.stdout)
        markdown = render(schema)
    except ValueError:
        sys.exit("结构元数据无效或存在未说明的新表；请核对生成器用途清单，未改写文档。")
    artifacts = {
        args.output_dir / "data" / "schema.json": json.dumps(schema, ensure_ascii=False, indent=2) + "\n",
        args.output_dir / "data-dictionary.md": markdown,
    }
    if args.check:
        mismatches = [p.name for p, expected in artifacts.items() if not p.exists() or p.read_text() != expected]
        if mismatches:
            sys.exit("结构文档与数据库不同：" + ", ".join(mismatches))
        print(f"结构文档一致：{len(schema['tables'])} 表 / {len(schema['migrations'])} 迁移。")
    else:
        for path, value in artifacts.items():
            path.parent.mkdir(parents=True, exist_ok=True)
            path.write_text(value)
        print(f"已生成结构文档：{len(schema['tables'])} 表 / {len(schema['migrations'])} 迁移。")


if __name__ == "__main__":
    main()
