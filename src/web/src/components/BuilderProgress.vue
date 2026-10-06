<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref} from 'vue';
type Item=Record<string,any>;
const props=defineProps<{builder:Item;content:Item;jobs:Item[];disabled:boolean}>();
const emit=defineEmits<{refresh:[];content:[];step:[id:string]}>();
let timer:ReturnType<typeof setInterval>|undefined;
onMounted(()=>{timer=setInterval(()=>{if(!props.disabled&&document.visibilityState==='visible')emit('refresh')},10000)});
onUnmounted(()=>clearInterval(timer));
const expanded=ref(false);
const sources=computed<Item[]>(()=>props.builder.sources||[]);
const runs=computed<Item[]>(()=>props.builder.runs||[]);
const realRuns=computed(()=>runs.value.filter(r=>r.type==='Candidates'&&r.provider!=='Mock'));
const realCandidates=computed<Item[]>(()=> (props.builder.candidates||[]).filter((c:Item)=>realRuns.value.some(r=>r.id===c.runId)));
const reviewed=computed(()=>realCandidates.value.filter(c=>c.status!=='Pending'));
const accepted=computed(()=>realCandidates.value.filter(c=>c.status==='Accepted'));
const completedSources=computed(()=>sources.value.filter(s=>realRuns.value.some(r=>r.sourceId===s.id&&r.status==='Completed')));
function published(c:Item){const id=c.createdKCId||c.existingKCId;if(!id)return false;return (props.content.releases||[]).some((r:Item)=>{if(r.withdrawn)return false;try{return JSON.parse(r.payload).kcs.some((k:Item)=>k.id===id)}catch{return false}})}
const usable=computed(()=>accepted.value.filter(published));
const drafts=computed(()=>accepted.value.filter(c=>c.createdDraftId));
function job(r:Item){return props.jobs.find(j=>j.inputRef===r.id&&['BuilderCandidates','ParsePDF'].includes(j.type))}
function state(r:Item){if(r.status!=='Queued')return r.status;const j=job(r);return j?.status|| (r.nextAttemptAt?'Retrying':'Queued')}
const labels:Record<string,string>={Queued:'排队等待',Running:'正在处理',Retrying:'等待自动重试',Completed:'生成完成，待人工审核',Succeeded:'处理完成',Failed:'已停止，需要核对',Cancelled:'已取消',NeedsOCR:'需要文字识别'};
const active=computed(()=>realRuns.value.filter(r=>['Queued','Running','Retrying'].includes(state(r))));
const failed=computed(()=>realRuns.value.filter(r=>r.status==='Failed'));
const taskRows=computed(()=>[...active.value,...realRuns.value.filter(r=>!active.value.includes(r))]);
function next(r:Item){if(r.status==='Failed')return r.error==='BUILDER_NEEDS_REPAIR'?'候选结构或来源校验未通过。对照原页校对文本；修正来源后再生成。':'查看下方运行记录中的失败原因和重新处理条件。';if(r.status==='Completed'){const cs=realCandidates.value.filter(c=>c.runId===r.id);return cs.length?`生成 ${cs.length} 项，已审核 ${cs.filter(c=>c.status!=='Pending').length} 项。继续核对来源、行为与边界。`:'未生成候选；核对来源是否包含可测能力。'}return '模型处理没有可核对的逐字进度，完成后才统计候选数量。'}
</script>
<template>
<section class="card builder-progress" aria-label="建库流程与进度">
 <div class="section-heading"><h2>建库总览</h2><button :disabled="disabled" @click="emit('refresh')">更新进度</button></div>
 <p>先校对教材 → 生成候选 → 人工审核 → 补齐题目与草稿 → 审核发布。发布后还要在“内容与发布”绑定学生，在“进度与计划”发布今日安排，孩子才能使用。</p>
 <ol class="builder-steps">
  <li><strong>1 · 准备来源</strong><span>已保存 {{sources.length}} 份</span><small>保存不代表已校对。扫描教材须核对除号、竖式与阅读顺序。</small><button @click="emit('step','builder-import')">导入与校对</button></li>
  <li><strong>2 · 生成候选</strong><span>{{completedSources.length}} / {{sources.length}} 份来源生成成功</span><small>本机模型提出能力定义；模拟任务只用于验证流程。</small><button @click="emit('step','builder-sources')">查看来源与任务</button></li>
  <li><strong>3 · 人工审核</strong><span>{{reviewed.length}} / {{realCandidates.length}} 项已审核</span><small>核对原文、能力边界；接受为草稿、关联已有能力或拒绝。</small><button @click="emit('step','builder-review')">审核候选</button></li>
  <li><strong>4 · 整理内容</strong><span>已建立 {{drafts.length}} 份候选草稿</span><small>接受候选只建立能力草稿，仍需补充测量题、答案、资源与课时。</small><button @click="emit('content')">编辑内容草稿</button></li>
  <li><strong>5 · 发布使用</strong><span>{{usable.length}} 项已审核能力进入可用正式版本</span><small>另行审核整份内容并发布；已有原创样例不计入这批教材成果。</small><button @click="emit('content')">查看正式版本</button></li>
 </ol>
 <div class="builder-overall" role="status"><strong>整体完成度：{{usable.length?'已有部分审核能力进入正式版本，仍须核对教材覆盖范围':'这批教材尚未形成可用的正式内容'}}</strong><p>统计范围为当前家庭已保存的来源与本机模型候选。{{realCandidates.length-reviewed.length}} 项待审核 · {{drafts.length}} 份候选草稿 · {{usable.length}} 项已进入可用版本。尚未设定全书或单元的应覆盖能力清单，不能据此计算教材整体完成百分比。</p></div>
 <h3>当前任务</h3><p>{{active.length?`正在处理或排队 ${active.length} 项`:'当前没有正在处理或排队的本机模型任务'}} · 生成成功 {{realRuns.filter(r=>r.status==='Completed').length}} 次 · 失败 {{failed.length}} 次</p>
 <p class="muted">任务完成度按实际状态显示；运行中不估算百分比。候选生成完成后，人工审核和正式发布仍待完成。页面每 10 秒检查更新；获取失败时保留上次结果并提示错误。</p>
 <article v-for="r in expanded?taskRows:taskRows.slice(0,3)" :key="r.id" class="builder-task"><div><strong>{{sources.find(s=>s.id===r.sourceId)?.title||'原来源'}}</strong><p>{{labels[state(r)]||state(r)}} · {{r.model}}</p><p>{{next(r)}}</p><button v-if="r.status==='Failed'" @click="emit('step','builder-run-records')">查看失败详情</button></div><span class="tag">{{r.status==='Completed'?'模型阶段完成':r.status==='Failed'?'需处理':labels[state(r)]||state(r)}}</span></article>
 <button v-if="taskRows.length>3" @click="expanded=!expanded">{{expanded?'收起任务':`查看全部 ${taskRows.length} 个本机模型任务`}}</button>
 <p class="muted">模拟任务 {{runs.filter(r=>r.type==='Candidates'&&r.provider==='Mock').length}} 次、模拟候选 {{(builder.candidates||[]).length-realCandidates.length}} 项，单独保留在下方记录，不计入教材建库成果。来源生成成功数按来源去重；失败次数保留历史，因此两者不能直接相加。</p>
</section>
</template>
<style scoped>
.builder-progress p{overflow-wrap:anywhere}.builder-steps{display:grid;grid-template-columns:repeat(auto-fit,minmax(210px,1fr));gap:12px;padding:0;list-style:none;margin:22px 0}.builder-steps li{background:#f6f8f1;border:1px solid #e2e8da;border-radius:12px;padding:18px;display:flex;flex-direction:column;gap:10px;min-width:0}.builder-steps span{color:#22624d;font-size:13px}.builder-steps small{color:#70816d;flex:1}.builder-steps button{align-self:flex-start;padding:8px 12px}.builder-overall{background:#edf3eb;border-radius:12px;padding:18px;margin:20px 0}.builder-overall p{margin-bottom:0;font-size:13px}.builder-task{display:flex;align-items:flex-start;gap:12px;padding:16px 0;border-bottom:1px solid #e5ebdf}.builder-task>div{flex:1;min-width:0}.builder-task p{font-size:13px;margin:5px 0}.builder-task .tag{flex-shrink:0}@media(max-width:760px){.builder-task{flex-wrap:wrap}.builder-task>div{flex-basis:100%}.builder-steps{grid-template-columns:minmax(0,1fr)}}
</style>
