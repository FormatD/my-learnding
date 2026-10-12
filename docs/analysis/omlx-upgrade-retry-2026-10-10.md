# oMLX 升级与 EmbeddingGemma 2 重试

日期：2026-10-10。承接 [首次联调报告](embeddinggemma-2-trial-2026-10-10.md)。

更新（2026-10-11）：用户已部署 0.7.1.dev1，实际向量接口与完整对照成功。详见 [成功联调结果](embeddinggemma-2-results-2026-10-11.md)。以下保留升级当天的阻碍与记录。

## 当前结果

已从 0.4.4rc1 升级到官方 0.7.0 稳定版。安装包摘要、应用签名和 macOS 安全评估通过；运行中的 `/openapi.json` 确认版本为 0.7.0。

再次调用 EmbeddingGemma 2 返回 HTTP 409：

```text
Model 'embeddinggemma-2-bf16' failed to load:
Model type embedding_gemma2 not supported.
```

因此稳定版升级没有解决架构支持问题。返回状态由原来的 500 变为 409，不表示向量已经成功。现有 Qwen3.5-9B-MLX-4bit 实际调用通过，返回 `OK`、finish_reason=stop，墙钟耗时 13.9 秒；这是升级后的简单可用性检查，不是质量或吞吐评测。学习系统的正式召回未切换。

## 支持版本与系统阻碍

官方 [0.7.1.dev1 发布说明](https://github.com/jundot/omlx/releases/tag/v0.7.1.dev1)明确新增 EmbeddingGemma 2 支持。已下载适配 macOS 26/27 的官方开发版，并核验安装包摘要与 GitHub 发布元数据一致；应用 `codesign --verify --deep --strict` 通过。

但 `spctl --assess --type execute` 拒绝该应用，结果为 `Unnotarized Developer ID`。签名有效和通过 Apple 公证是不同检查。这是 macOS 的实际安全评估结果，不是模型接口错误，也不是自动审批工具拒绝。

开发版未安装、未运行；没有删除隔离属性、关闭 Gatekeeper 或修改系统安全设置。要继续用开发版完成实际向量实验，需要用户明确确认接受这个特定应用的系统安全提示。即使确认后，也应仅采用系统针对该应用的正常允许流程，保持系统全局防护。

## 可回退材料

- 当前应用：`/Applications/oMLX.app`，0.7.0。
- 原应用保存在项目私有目录：`.local/omlx-upgrade-070/oMLX-0.4.4rc1.app`。
- 升级前和稳定版启动后的全局/模型设置私有快照：`.local/omlx-upgrade-070/`。包含配置数据，不提交或公开这些文件。
- 开发版下载包：`/tmp/oMLX-0.7.1.dev1-macos26-27.dmg`，已只读挂载在 `/Volumes/oMLX`，挂载本身不表示已经安装。

官方稳定版 DMG SHA-256：`2e3bb06ac6ee7f50986ba1417e909d432ccd2be471db752a4a2d3b5651e3bce0`。

官方开发版 DMG SHA-256：`04fbff0b54bd8656a6684e3051879d7039cb5f4514777527614c54d6d00e0ca2`。

首次稳定版安装后启动返回系统 -600；随后使用正常的新实例启动成功，并通过实际 API 验证。模型文件和已有学习数据没有迁移或删除。

## 后续实验已准备

`scripts/compare_embeddings.py` 增加实际运行版本记录及显式 `--unverified-artifact` 参数。若 CLI 仍无法读取模型目录，可用每次独立的临时空间做对照，同时明确记录真实权重摘要未知；该版本格式无法通过生产持久索引校验，不能用它冒充固定权重身份。默认仍要求真实权重清单。

确认开发版可以运行后，先验证两条文本实际返回完整、有限、非零的 768 维向量，再执行同一组 16 能力/24 查询的原输入与 STS 对照，以及 Qwen 对照。当前没有新增 EmbeddingGemma 2 准确率、速度或运行内存结果。上一次 Qwen 的 24/24 结果来自升级前的独立样本，不能宣称升级后仍具有同一质量结果。

本轮 Python 语法和变更空白检查通过；没有再次运行整套 CI 或 V1 验收。
