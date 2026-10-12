# 本地数学能力检索对照实验

原创小样本：16 个能力、24 条查询，作者给出预期匹配，尚未经独立人工复核。全部能力类型为 Procedure，无人工别名、无拒绝匹配样本，不能代表教材整体质量。实际复用服务端当前检索算法，不写学习数据库。

先构建：

```sh
.tools/dotnet/dotnet build tests/embedding-comparison/Learning.EmbeddingComparison.csproj
```

本地 oMLX 运行在 127.0.0.1:8000；密钥放在仅本人可读写的 `.local/omlx-api-key.txt`，不要提交密钥。输出默认保存于忽略的 `.local/`。

当前检索与 Qwen 对照：

```sh
python3 scripts/compare_embeddings.py --baseline-only --output .local/embedding-comparison-clean
```

EmbeddingGemma 2 后端兼容、权重目录可读取后：

```sh
python3 scripts/compare_embeddings.py --model embeddinggemma-2-bf16
```

工具核验真实权重文件清单后，比较原输入和 STS 对称前缀两个独立空间，每批返回 768 维完整向量，再分别计算纯余弦与现有 BM25/RRF 混合召回。不启用降维或截断。输入长度仍由服务端限制，fixture 只验证短文本，未验证长教材文本上限。

Qwen 只看到能力定义和查询定义，预期答案不发送给模型。输出中的库外 ID 单独列出，不能静默替换。其耗时是整批 24 条显式选择查询的耗时，不能直接与单条 embedding 的耗时比较。

`--baseline-only` 不表示 EmbeddingGemma 已成功调用。初次带答案标签的 Qwen 请求已作废；只使用 clean 输出进行质量统计。当前后端报不支持模型架构时，不能获得 EmbeddingGemma 的质量、耗时或运行内存结论。

若运行后端已兼容，但 CLI 仍无法读取模型文件，可显式添加 `--unverified-artifact` 进行一次性对照。该模式记录未知工件身份，使用每次独立的 `unverified-experiment/…` 临时空间版本；没有 sha256 权重身份，因此不能通过生产持久索引的版本校验。仅用于测量当前进程实际返回的向量，不是可复现的固定权重验收。
