<script setup lang="ts">
import {computed,onMounted,onUnmounted,ref,watch} from 'vue';
import {api,apiWithVersion} from '../api';
type Item=Record<string,any>;
const props=defineProps<{drafts:Item[];releases:Item[];disabled:boolean}>();
const emit=defineEmits<{refresh:[];editDraft:[id:string]}>();
const busy=ref(false),error=ref(''),notice=ref(''),draftId=ref(''),libraryId=ref('');
const provider=ref('Mock');
const preparations=ref<Item[]>([]),openingPreparation=ref('');let poll:ReturnType<typeof setInterval>|undefined;const jobLabels:Record<string,string>={Queued:'等待处理',Running:'处理中',Retrying:'等待重试',Failed:'已停止，待核对',Succeeded:'已完成',Cancelled:'已取消'};
const selectedOwners=ref<string[]>([]),ownerType=ref('All'),search=ref(''),ownerPage=ref(0);
const runs=ref<Item[]>([]),detail=ref<Item|null>(null),reviewVersion=ref(''),rows=ref<Item[]>([]);
const selectedReviews=ref<string[]>([]),reviewPage=ref(0),acknowledged=ref(false),commonReason=ref(''),commonDecision=ref('Accept');
const mappingErrors:Record<string,string>={JOB_REQUESTER_FORBIDDEN:'内容维护权限已变化，请由有权限成员核对原输入后重新处理。',LIBRARY_WITHDRAWN:'本次能力库已撤回，请选择有效版本重新准备。',JOB_INPUT_SNAPSHOT_CHANGED:'固定输入不一致，请核对后重新准备。',INPUT_SNAPSHOT_UNKNOWN:'原能力库已不可用，请重新准备。',JOB_ATTEMPTS_EXHAUSTED:'多次处理中断，已停止自动处理；请核对原输入后重新处理。',MAPPING_PROCESSING_FAILED:'处理未能完成；可以核对原输入后重新处理。'};
const names:Record<string,string>={Question:'题目',Lesson:'课时',Resource:'资源'};
const collections:Record<string,string>={Question:'questions',Lesson:'lessons',Resource:'resources'};
const policies:Record<string,string>={NoEvidence:'不计能力证据',SingleKC:'整题测量一个能力',ObservedSteps:'家长逐步观察'};
const roles:Record<string,string>={Primary:'主要能力',Secondary:'次要能力',Prerequisite:'仅前置',Context:'仅上下文'};
const modes:Record<string,string>={None:'不计证据',WholeItem:'整题测量',StepObserved:'独立观察步骤'};
const blocked=computed(()=>busy.value||props.disabled);
const available=computed(()=>props.releases.filter(r=>!r.withdrawn));
const source=computed(()=>{const draft=props.drafts.find(d=>d.id===draftId.value);return draft?JSON.parse(draft.payload):null;});
const owners=computed<Item[]>(()=>source.value?[
  ...source.value.questions.map((q:Item,i:number)=>({type:'Question',id:q.id,revision:q.revisionId,title:`第 ${i+1} 题 · ${q.stem}`})),
  ...source.value.lessons.map((l:Item)=>({type:'Lesson',id:l.id,revision:l.revisionId,title:l.title})),
  ...source.value.resources.map((r:Item)=>({type:'Resource',id:r.id,revision:r.revisionId,title:r.title}))
]:[]);
const filtered=computed(()=>owners.value.filter(o=>(ownerType.value==='All'||o.type===ownerType.value)&&o.title.includes(search.value.trim())));
const visibleOwners=computed(()=>filtered.value.slice(ownerPage.value*20,(ownerPage.value+1)*20));
const pending=computed(()=>rows.value.filter(r=>r.status==='Pending'));
const visibleReviews=computed(()=>pending.value.slice(reviewPage.value*20,(reviewPage.value+1)*20));
const selected=computed(()=>pending.value.filter(r=>selectedReviews.value.includes(r.id)));
const canReview=computed(()=>selected.value.length>0&&selected.value.every(r=>r.decision!=='Later'&&r.reason.trim())&&acknowledged.value);
const frozen=computed(()=>detail.value?JSON.parse(detail.value.run.sourcePayload):null);
function key(o:Item){return o.type+':'+o.id;}
function resetOwners(){selectedOwners.value=[];ownerPage.value=0;}
watch([ownerType,search],()=>ownerPage.value=0);
watch([selectedReviews,rows],()=>acknowledged.value=false,{deep:true});
async function run(action:()=>Promise<void>){if(blocked.value)return;busy.value=true;error.value='';notice.value='';try{await action()}catch(e){error.value=(e as Error).message}finally{busy.value=false;}}
async function load(){runs.value=await api('/builder/mapping-runs');preparations.value=await api('/builder/mapping-preparations');}
async function refreshPreparation(){await load();const ready=preparations.value.find(p=>p.id===openingPreparation.value && p.status==='Succeeded');if(ready){openingPreparation.value='';await open(ready.runId);notice.value=detail.value!.run.provider==='Manual'?'手工维护已准备，只带入原关联并固定输入；覆盖权重默认值须逐项核对。':'模拟建议已准备，原草稿和能力库版本已固定；请逐项核对。';}}
async function open(id:string){const response=await apiWithVersion('/builder/mapping-runs/'+id);detail.value=response.data;reviewVersion.value=response.version;rows.value=detail.value!.suggestions.map((s:Item)=>({...s,proposal:{evidencePolicy:s.evidencePolicy,items:JSON.parse(s.suggestedItems)},decision:'Later',reason:''}));selectedReviews.value=[];reviewPage.value=0;acknowledged.value=false;}
function chooseOwner(o:Item,event:Event){const checked=(event.target as HTMLInputElement).checked;if(checked&&selectedOwners.value.length>=100){(event.target as HTMLInputElement).checked=false;error.value='一次最多选择100个对象，可以分批准备。';return;}selectedOwners.value=checked?[...selectedOwners.value,key(o)]:selectedOwners.value.filter(k=>k!==key(o));}
async function prepare(){await run(async()=>{const result=await api('/builder/mapping-preparations',{draftId:draftId.value,libraryReleaseId:libraryId.value,provider:provider.value,owners:owners.value.filter(o=>selectedOwners.value.includes(key(o))).map(o=>({ownerType:o.type,ownerId:o.id,ownerRevisionId:o.revision}))});openingPreparation.value=result.id;await refreshPreparation();if(openingPreparation.value)notice.value='已提交后台准备，可继续使用其他页面。原草稿、对象和能力库版本已固定。';});}
async function retry(p:Item){const reason=window.prompt('填写重新处理依据：原输入保持不变。');if(!reason?.trim())return;await run(async()=>{await api('/builder/mapping-preparations/'+p.id+':retry',{reason:reason.trim()});openingPreparation.value=p.id;await refreshPreparation();notice.value='已按原输入重新排队，旧领取记录保留。';});}
function owner(r:Item){const list=frozen.value?.[collections[r.ownerType]]||[];return list.find((o:Item)=>o.id===r.ownerId);}
function kcName(id:string){return detail.value?.library.find((k:Item)=>k.id===id)?.name||frozen.value?.kcs.find((k:Item)=>k.id===id)?.name||'原版本能力';}
function setKC(item:Item,id:string){item.kcId=id;item.kcRevisionId=detail.value!.library.find((k:Item)=>k.id===id).revisionId;item.modelScore=null;}
function normalizeItems(r:Item){r.proposal.items.forEach((i:Item,index:number)=>i.sequence=index+1);}
function addItem(r:Item){const k=detail.value!.library[0];if(!k||r.proposal.items.length>=20)return;r.proposal.items.push({kcId:k.id,kcRevisionId:k.revisionId,role:r.ownerType==='Question'?'Context':'Primary',coverageWeight:1,evidenceShare:0,evidenceMode:'None',step:null,sequence:r.proposal.items.length+1,modelScore:null,sourceRefs:[`draft:${detail.value!.run.sourceDraftId}/${r.ownerType}:${r.ownerRevisionId}`]});}
function changeMode(i:Item){if(i.evidenceMode==='None'){i.evidenceShare=0;i.step=null;}else{if(!i.evidenceShare)i.evidenceShare=i.evidenceMode==='WholeItem'?1:.5;if(i.evidenceMode!=='StepObserved')i.step=null;}}
function changeRole(i:Item){if(['Context','Prerequisite'].includes(i.role)){i.evidenceMode='None';changeMode(i);}}
function changePolicy(r:Item){if(r.proposal.evidencePolicy==='NoEvidence')r.proposal.items.forEach((i:Item)=>{i.evidenceMode='None';changeMode(i);});}
function applyCommon(){selected.value.forEach(r=>{r.decision=commonDecision.value;r.reason=commonReason.value.trim();});}
async function decide(){await run(async()=>{const result=await api('/builder/mapping-runs/'+detail.value!.run.id+'/suggestions:decide',{decisions:selected.value.map(r=>({suggestionId:r.id,decision:r.decision,reason:r.reason.trim(),correctedProposal:r.decision==='Accept'?r.proposal:null}))},'POST',undefined,reviewVersion.value);await open(result.runId);await load();emit('refresh');notice.value=result.draftId?'本批决定已保存，接受项生成待审核草稿；尚未发布或改变学生学习。':'拒绝决定已保存，没有生成正式映射。';});}
function flags(r:Item){const labels:Record<string,string>={ManualSource:'手工维护，只带入原有关联，不使用模型',CoverageNeedsReview:'原快照未记录覆盖权重，默认值1须人工核对',MockOnly:'本地模拟，尚无质量评测',HumanReviewRequired:'须人工核对',IndependentStepReviewRequired:'逐个核对独立观察步骤',OriginalKCOutsideLibrary:'原能力不在所选能力库',NoLibraryMatch:'没有能力库匹配'};return JSON.parse(r.validationFlags).map((f:string)=>labels[f]||f).join('；');}
onMounted(()=>{void run(load);poll=setInterval(()=>{if(!blocked.value && preparations.value.some(p=>['Queued','Running','Retrying'].includes(p.status)))void run(refreshPreparation);},1500);});
onUnmounted(()=>{if(poll)clearInterval(poll);});
</script>
<template>
<section class="card mapping-workbench" aria-label="映射建议与批量审核">
  <h2>映射建议与批量审核</h2><p class="muted">选择题目、课时或讲解资源，参考本地模拟建议，再由你核对和校正。接受只生成待审核草稿，发布仍需单独审核；排序分数与接受次数不代表映射质量。</p>
  <p v-if="error" class="warning" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
  <details><summary>准备一批建议</summary>
    <label>准备方式<select v-model="provider" :disabled="blocked"><option value="Mock">本地模拟建议</option><option value="Manual">手工维护原映射</option></select></label>
    <p v-if="provider==='Manual'" class="muted">只带入原对象已记录的能力关联与观察步骤。所选库以外的原关联会明确提示，需自行补齐；不会自动选择替代能力。</p>
    <label>映射来源草稿<select v-model="draftId" @change="resetOwners" :disabled="blocked"><option value="">选择要核对的内容</option><option v-for="d in drafts" :key="d.id" :value="d.id">{{d.title}} · 第 {{d.version}} 版</option></select></label>
    <label>对照的正式能力库<select v-model="libraryId" :disabled="blocked"><option value="">明确选择正式版本</option><option v-for="r in available" :key="r.id" :value="r.id">内容版本 {{r.number}}</option></select></label>
    <p v-if="!available.length" class="muted">先人工建立并发布一个正式能力库，再准备建议。</p>
    <template v-if="source">
      <div class="mapping-fields"><label>对象类型<select v-model="ownerType"><option value="All">全部</option><option v-for="(label,type) in names" :key="type" :value="type">{{label}}</option></select></label><label>查找对象<input v-model="search" placeholder="输入题目或名称"></label></div>
      <p>已选 {{selectedOwners.length}} 项；每批最多100项。</p>
      <label v-for="o in visibleOwners" :key="key(o)" class="mapping-check"><input type="checkbox" :checked="selectedOwners.includes(key(o))" :disabled="blocked||!o.revision" @change="chooseOwner(o,$event)"><span>{{names[o.type]}} · {{o.title}}<small v-if="!o.revision">版本未记录，请先编辑保存草稿。</small></span></label>
      <div class="mapping-actions"><button @click="ownerPage--" :disabled="blocked||ownerPage===0">上一页对象</button><span>第 {{ownerPage+1}} 页 · 共 {{filtered.length}} 项</span><button @click="ownerPage++" :disabled="blocked||(ownerPage+1)*20>=filtered.length">下一页对象</button><button @click="selectedOwners=[]" :disabled="blocked">清除对象选择</button></div>
    </template>
    <button class="primary" @click="prepare" :disabled="blocked||!draftId||!libraryId||!selectedOwners.length">{{provider==='Manual'?'准备手工映射维护':'准备本地模拟映射建议'}}</button>
  </details>
  <section v-if="preparations.length" aria-label="后台映射准备"><h3>后台准备进度</h3><button @click="run(refreshPreparation)" :disabled="blocked">刷新准备进度</button><article v-for="p in preparations" :key="p.id"><p>{{p.sourceTitle}} · {{jobLabels[p.status]||p.status}}</p><p v-if="p.error" class="muted">{{mappingErrors[p.error]||'本次未能完成，请核对后台任务记录。'}}</p><button v-if="p.status==='Succeeded'" @click="run(()=>open(p.runId))" :disabled="blocked">查看已完成建议</button><button v-if="p.status==='Failed' && ['MAPPING_PROCESSING_FAILED','JOB_ATTEMPTS_EXHAUSTED','JOB_REQUESTER_FORBIDDEN'].includes(p.error)" @click="retry(p)" :disabled="blocked">按原输入重新处理</button></article></section>
  <label>查看建议运行<select :value="detail?.run.id||''" @change="run(()=>open(($event.target as HTMLSelectElement).value))" :disabled="blocked"><option value="" disabled>选择一批建议</option><option v-for="r in runs" :key="r.id" :value="r.id">{{r.provider==='Manual'?'手工':'模拟'}} · {{r.sourceTitle}} · {{new Date(r.createdAt).toLocaleString('zh-CN')}}</option></select></label>
  <button @click="run(async()=>{await load();if(detail)await open(detail.run.id)})" :disabled="blocked">刷新映射审核</button>
  <template v-if="detail">
    <p class="muted">{{detail.run.provider==='Manual'?'手工原映射':'本地模拟建议'}} · 源草稿第 {{detail.run.sourceDraftVersion}} 版 · 固定能力库内容版本 {{releases.find(r=>r.id===detail!.run.libraryReleaseId)?.number||'历史'}}。源草稿后续修改与新发布版本不会替换本批输入。</p>
    <p>共 {{detail.quality.suggested}} 项 · 待处理 {{detail.quality.pending}} 项 · 接受 {{detail.quality.accepted}} 项 · 拒绝 {{detail.quality.rejected}} 项 · 接受时校正 {{detail.quality.corrected}} 项</p><p class="muted">{{detail.quality.note}}</p>
    <div v-if="pending.length">
      <label>选中项的处理方式<select v-model="commonDecision" :disabled="blocked"><option value="Accept">接受校正后的映射</option><option value="Reject">拒绝建议</option></select></label><label>选中项的共同审核依据<textarea v-model="commonReason" maxlength="4000" :disabled="blocked" placeholder="先核对选中对象，再填写具体依据。也可以逐项填写不同理由。"></textarea></label>
      <button @click="applyCommon" :disabled="blocked||!selected.length||!commonReason.trim()">将方式与理由应用到选中项</button>
      <article v-for="r in visibleReviews" :key="r.id" class="content-editor-row" :aria-label="`${names[r.ownerType]}映射审核 · ${r.ownerTitle}`">
        <label class="mapping-check"><input type="checkbox" v-model="selectedReviews" :value="r.id" :disabled="blocked"><span>本批处理 · {{names[r.ownerType]}} · {{r.ownerTitle}}</span></label>
        <p class="muted">{{flags(r)}}</p>
        <details><summary>核对原对象与原映射</summary><p v-if="r.ownerType==='Question'">参考答案：{{owner(r)?.answer}}<br>解析：{{owner(r)?.explanation}}<br>原归因：{{policies[owner(r)?.policy]}}</p><p v-if="r.ownerType==='Resource'">原执行说明：{{owner(r)?.paperReference}}</p><p>原关联能力：{{(r.ownerType==='Question'?owner(r)?.mappings.map((m:Item)=>m.kcId):owner(r)?.kcIds||[]).map(kcName).join('、')}}</p></details>
        <template v-if="r.decision!=='Reject'">
          <label v-if="r.ownerType==='Question'">校正后的题目归因<select v-model="r.proposal.evidencePolicy" @change="changePolicy(r)" :disabled="blocked"><option v-for="(label,policy) in policies" :key="policy" :value="policy">{{label}}</option></select></label><p v-else class="muted">只记录讲解覆盖，完成课时或资源不会形成作答证据。</p>
          <div v-for="(i,index) in r.proposal.items" :key="index" class="mapping-item">
            <label>关联能力<select :value="i.kcId" @change="setKC(i,($event.target as HTMLSelectElement).value)" :disabled="blocked"><option v-for="k in detail.library" :key="k.id" :value="k.id">{{k.name}}</option></select></label>
            <p class="muted">{{detail.library.find((k:Item)=>k.id===i.kcId)?.behavior}}<br>{{detail.library.find((k:Item)=>k.id===i.kcId)?.boundary}}</p>
            <div class="mapping-fields"><label>教学覆盖权重<input v-model.number="i.coverageWeight" type="number" min="0" max="1" step="0.000001" :disabled="blocked"></label><template v-if="r.ownerType==='Question'"><label>映射角色<select v-model="i.role" @change="changeRole(i)" :disabled="blocked"><option v-for="(label,role) in roles" :key="role" :value="role">{{label}}</option></select></label><label>测量方式<select v-model="i.evidenceMode" @change="changeMode(i)" :disabled="blocked"><option v-for="(label,mode) in modes" :key="mode" :value="mode">{{label}}</option></select></label><label>证据预算份额<input v-model.number="i.evidenceShare" type="number" min="0" max="1" step="0.000001" :disabled="blocked||i.evidenceMode==='None'"></label><label v-if="i.evidenceMode==='StepObserved'">独立观察点<input v-model="i.step" maxlength="100" :disabled="blocked"></label></template></div>
            <button @click="r.proposal.items.splice(Number(index),1);normalizeItems(r)" :disabled="blocked">移除此映射</button>
          </div>
          <button @click="addItem(r)" :disabled="blocked||r.proposal.items.length>=20">新增关联能力</button>
          <details><summary>相近能力与原始建议</summary><p v-for="m in JSON.parse(r.matches)" :key="m.kcId">{{m.name}} · 模拟排序 {{m.score.toFixed(3)}}<br>{{m.behavior}}<br>{{m.boundary}}</p><p>原始建议：{{JSON.parse(r.suggestedItems).map((i:Item)=>kcName(i.kcId)+(i.step?' · '+i.step:'')).join('、')}} · {{policies[r.evidencePolicy]}}</p></details>
        </template>
        <label>本项决定<select v-model="r.decision" :disabled="blocked"><option value="Later">本次暂不处理</option><option value="Accept">接受校正后的映射</option><option value="Reject">拒绝建议</option></select></label><label>本项审核依据<textarea v-model="r.reason" maxlength="4000" :disabled="blocked"></textarea></label>
      </article>
      <div class="mapping-actions"><button @click="reviewPage--" :disabled="blocked||reviewPage===0">上一页建议</button><span>第 {{reviewPage+1}} 页 · 本批已选 {{selected.length}} 项</span><button @click="reviewPage++" :disabled="blocked||(reviewPage+1)*20>=pending.length">下一页建议</button></div>
      <label class="mapping-check"><input type="checkbox" v-model="acknowledged" :disabled="blocked||!selected.length"><span>已核对选中对象、能力范围和独立测量依据，确认本批决定</span></label>
      <button class="primary" @click="decide" :disabled="blocked||!canReview">提交本批人工决定</button><p class="muted">整批成功后才保存；任一项无效时整批不生效，当前校正内容继续保留。</p>
    </div>
    <details v-if="detail.decisions.length"><summary>查看已保存的决定与草稿</summary><article v-for="d in detail.decisions" :key="d.id" class="content-editor-row"><p>{{names[detail.suggestions.find((s:Item)=>s.id===d.suggestionId)?.ownerType]}} · {{detail.suggestions.find((s:Item)=>s.id===d.suggestionId)?.ownerTitle}} · {{d.decision==='Accept'?'接受为草稿':'已拒绝'}}</p><p>{{new Date(d.reviewedAt).toLocaleString('zh-CN')}} · 审核依据：{{d.reason}}</p><p v-if="d.decision==='Accept'">保存的关联：{{JSON.parse(d.correctedPayload).items.map((i:Item)=>kcName(i.kcId)+(i.step?' · '+i.step:'')).join('、')}}</p><button v-if="d.createdDraftId" @click="emit('editDraft',d.createdDraftId)" :disabled="blocked">继续编辑审核映射草稿</button></article></details>
  </template>
</section>
</template>
<style scoped>
.mapping-fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(220px,100%),1fr));gap:12px}
.mapping-check{display:flex;align-items:flex-start;gap:10px;line-height:1.6;margin:12px 0}.mapping-check input[type=checkbox]{width:20px;height:20px;flex:0 0 20px;margin:2px 0 0;accent-color:#22624d}.mapping-check span{overflow-wrap:anywhere;min-width:0}.mapping-check small{display:block;color:#88602e}.mapping-item{border-left:3px solid #dce7db;padding:12px;margin:14px 0;background:#f8faf6}.mapping-actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:14px 0}.mapping-workbench select,.mapping-workbench input,.mapping-workbench textarea{max-width:100%;min-width:0}.mapping-workbench p{overflow-wrap:anywhere}
</style>
