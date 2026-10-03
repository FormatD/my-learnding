<script setup lang="ts">
import {ref} from 'vue';
import {api} from '../api';
type Item=Record<string,any>;
const props=defineProps<{runs:Item[];attempts:Item[];sources:Item[];libraries:Item[];disabled:boolean}>(),emit=defineEmits<{refresh:[]}>();
const busy=ref(false),error=ref(''),notice=ref(''),reasons=ref<Record<string,string>>({});
const status:Record<string,string>={Queued:'等待处理',Completed:'已完成',Failed:'已停止',NeedsOCR:'需要文字识别',RetryScheduled:'已安排重试'};
function canRetry(run:Item){return run.status==='Failed'&&['BUILDER_PROCESSING_FAILED','BUILDER_CONCURRENCY_LIMIT','BUILDER_USAGE_RECONCILIATION_REQUIRED','BUILDER_CALL_BUDGET_LIMIT','BUILDER_DAILY_BUDGET_LIMIT'].includes(run.error)}
async function retry(run:Item){if(busy.value||props.disabled)return;busy.value=true;error.value='';notice.value='';try{await api(`/builder/runs/${run.id}:retry`,{reason:reasons.value[run.id]});notice.value='已请求使用原输入重新处理，旧失败记录继续保留。';emit('refresh');}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
function protocol(attempt:Item){try{return attempt.protocolResult?JSON.parse(attempt.protocolResult):null}catch{return null}}
function settings(run:Item){try{return run.modelConfigPayload?JSON.parse(run.modelConfigPayload).limits:null}catch{return null}}
function source(id:string){return props.sources.find(s=>s.id===id)?.title||'原来源';}
function scope(run:Item){if(run.type==='ParsePDF')return '本地解析原PDF';if(!['builder-input/2','builder-input/3'].includes(run.inputVersion))return '历史正式库输入未记录';return run.libraryReleaseId?'固定内容版本 '+(props.libraries.find(r=>r.id===run.libraryReleaseId)?.number||'原版本'):'生成时明确使用空库';}
</script>
<template>
<section class="card" aria-label="建库运行记录"><h2>建库运行记录</h2><p class="muted">初次处理失败后最多自动重试三次，依次等待2、4、8秒；持续失败则停止。原输入和配置保持固定，完成的任务不会重复生成。</p><button @click="emit('refresh')" :disabled="busy||disabled">刷新建库记录</button><p v-if="error" class="warning" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
<article v-for="run in runs" :key="run.id" class="content-editor-row"><h3>{{source(run.sourceId)}} · {{status[run.status]||run.status}}</h3><p>{{scope(run)}} · 本轮自动重试 {{run.retries}} 次</p><p v-if="run.type==='Candidates'&&settings(run)" class="muted">创建时处理上限：{{settings(run).maxFragments}} 个片段、{{settings(run).maxInputCharacters}} 字；{{settings(run).timeoutMilliseconds/1000}} 秒内完成；最多修复 {{settings(run).repairAttempts}} 次。重新处理沿用这组上限。</p><p v-else-if="run.type==='Candidates'" class="muted">旧任务未记录完整处理上限，使用明确的兼容流程；不会补造原配置。</p><p v-if="run.nextAttemptAt" class="muted">下次尝试不早于 {{new Date(run.nextAttemptAt).toLocaleString()}}</p><p v-if="run.error" class="muted">失败编号：{{run.error}}。需要修复来源或输入的任务不能直接重试。</p><template v-if="canRetry(run)"><label>重新处理依据<textarea v-model="reasons[run.id]" maxlength="4000" placeholder="确认故障已处理，并说明为什么再次尝试原输入"></textarea></label><button @click="retry(run)" :disabled="busy||disabled||!reasons[run.id]?.trim()">重新处理原建库任务</button></template>
<details><summary>查看每次尝试</summary><p v-if="!attempts.some(a=>a.runId===run.id)" class="muted">未记录处理尝试；旧历史不会推算补齐。</p><div v-for="attempt in attempts.filter(a=>a.runId===run.id)" :key="attempt.id"><p>人工重试轮次 {{attempt.retryRound}} · 第 {{attempt.number}} 次 · {{status[attempt.status]||attempt.status}}</p><p>{{new Date(attempt.startedAt).toLocaleString()}} → {{new Date(attempt.finishedAt).toLocaleString()}}</p><p v-if="protocol(attempt)" class="muted">结构与来源校验通过 · 本地模拟处理 {{protocol(attempt).calls}} 次 · {{protocol(attempt).repaired?'经一次修复通过':'首次通过'}}。不代表真实模型抽取质量。</p><p v-if="attempt.errorCode">失败编号：{{attempt.errorCode}}</p><p v-if="attempt.nextAttemptAt">下一次不早于 {{new Date(attempt.nextAttemptAt).toLocaleString()}}</p></div></details></article><p v-if="!runs.length" class="empty">还没有建库任务。</p></section>
</template>
