# 接口契约与自动检查

[OpenAPI快照](../api/openapi.json)由实际服务 `/api/v1/openapi.json` 生成，没有第二份手写路由定义。生成过程启动一次性 PostgreSQL 数据库，应用当前迁移，再启动隔离监听服务；使用临时验收家庭登录读取文档，成功或失败都终止服务并删除临时库，不使用实际学习库。

快照保留路径、方法、请求参数、类型和服务声明的响应元数据，去除说明/示例与运行时监听地址；业务字段即使名为 `summary`、`description` 也保留。当前85路径、104结构定义，仅包含结构，没有家庭作答、来源、密码或令牌。

## 本地检查

先完成锁文件依赖恢复并可连接本地PostgreSQL；默认127.0.0.1:55432、当前系统用户。其他机器通过PGHOST/PGPORT/PGUSER/PGPASSWORD等标准环境配置本地验收数据库，须有创建/删除临时库权限，不使用生产连接。

```sh
sh scripts/ci_check.sh
```

该入口依次完成前后端构建、规则验收、兼容检查专项、实际生成契约与当前快照逐字一致核对，并核对空库迁移后的46表/23迁移结构与数据字典。传入上一版快照，还会比较兼容性：

```sh
sh scripts/ci_check.sh /private/tmp/previous-openapi.json
```

独立比较两个已有文件可使用：

```sh
python3 scripts/openapi_contract.py /private/tmp/previous-openapi.json docs/api/openapi.json
```

路径/方法/已有字段/媒体类型删除、类型/引用变化、新必填参数或属性、约束/安全/枚举等结构变化会失败。新增路径、方法、结构、可选字段或可选参数可通过。无法明确判定的修改按潜在不兼容拒绝；不是完整的JSON Schema兼容性证明。数组位置变更不当作普通集合重排，只有required/enum的纯顺序改变忽略。

## 更新流程

先修改实际代码、补充对应接口行为验收，再明确重新导出当前结构：

```sh
python3 tests/openapi_acceptance.py --export docs/api/openapi.json
```

导出后运行完整检查并审阅差异。不能只更新快照就认为不兼容修改得到批准；需要保持已有接口兼容，或者按照独立版本迁移安排修改代码和调用方。自动化没有“跳过重大变更”开关。上一版本的独立快照仍作为比较基准。

## 自动化配置

`.github/workflows/contracts.yml` 使用只读仓库权限、PostgreSQL16临时服务和固定项目SDK/Node版本；拉取请求比较目标分支，main提交比较提交前版本，当前实际服务必须与已保存快照一致。第一次引入契约或无上一版的手动运行，会明确说明没有旧快照；仍执行当前一致核对。脚本不把执行环境里的分支文字拼入命令，旧提交使用合法摘要和单独参数读取。

本地已运行同一检查入口；当前项目未连接远程仓库，GitHub托管运行尚未执行。配置文件存在与本地成功不等于远程CI已通过。动作配置参考官方[checkout](https://github.com/actions/checkout)、[setup-dotnet](https://github.com/actions/setup-dotnet)、[setup-node](https://github.com/actions/setup-node)文档。

## 当前覆盖限制

服务端动态 `Results` 返回值的部分接口仍只生成默认200响应，实际接口可能返回201/202或不同结构；例如注册的实际201由接口验收证明，当前生成响应元数据尚未表达完整。鉴权、幂等、If-Match和业务错误也有中间件/领域校验，不能只凭这个结构比较证明这些运行行为全部兼容。各专项接口验收仍必需，完整响应契约声明需继续补齐。此检查完成结构同步与重大结构变化拦截，不代替全部接口契约退出条件或真实家庭试用。
