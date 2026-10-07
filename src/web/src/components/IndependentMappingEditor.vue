<script setup lang="ts">
import SourceEvidenceButton from './SourceEvidenceButton.vue';
import {computed,onMounted,ref,watch} from 'vue';
import {api} from '../api';
type Item=Record<string,any>;
const props=defineProps<{drafts:Item[];releases:Item[];disabled:boolean}>();
const emit=defineEmits<{refresh:[];editDraft:[id:string]}>();
const busy=ref(false),error=ref(''),notice=ref(''),draftId=ref(''),libraryId=ref(''),ownerKey=ref(''),search=ref(''),page=ref(0);
const prepared=ref<Item|null>(null),proposal=ref<Item>({evidencePolicy:'NoEvidence',items:[]}),reason=ref('');
const saved=ref<Item|null>(null),history=ref<Item>({sets:[],total:0}),historyPage=ref(0);
const names:Item={Question:'题目',Lesson:'课时',Resource:'资源'},policies:Item={NoEvidence:'不计能力证据',SingleKC:'整题测量一个能力',ObservedSteps:'家长逐步观察'},roles:Item={Primary:'主要能力',Secondary:'次要能力',Prerequisite:'仅前置',Context:'仅上下文'},modes:Item={None:'不计证据',WholeItem:'整题测量',StepObserved:'独立观察步骤'};
const collections:Record<string,string>={Question:'questions',Lesson:'lessons',Resource:'resources'};
const blocked=computed(()=>busy.value||props.disabled);
const source=computed(()=>{const d=props.drafts.find(d=>d.id===draftId.value);return d?JSON.parse(d.payload):null});
function selections(c:Item){return ['Question','Lesson','Resource'].flatMap(t=>(c[collections[t]!]||[]).filter((o:Item)=>o.revisionId).map((o:Item)=>({ownerType:t,ownerId:o.id,ownerRevisionId:o.revisionId,title:o.stem||o.title,key:t+':'+o.id})));}
const owners=computed(()=>source.value?selections(source.value).filter(o=>o.title.includes(search.value.trim())):[]);
const visible=computed(()=>owners.value.slice(page.value*20,(page.value+1)*20));
watch([draftId,libraryId,ownerKey],()=>{prepared.value=null;error.value='';notice.value=''});
watch(draftId,()=>{ownerKey.value='';page.value=0});watch(search,()=>page.value=0);
async function run(action:()=>Promise<void>){if(blocked.value)return;busy.value=true;error.value='';notice.value='';try{await action()}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
async function loadHistory(){history.value=await api('/content/mapping-sets?offset='+historyPage.value*20+'&limit=20')}
async function prepare(){await run(async()=>{
 const preview=await api('/content/drafts/'+draftId.value+'/mapping-preview'),selection=selections(preview.catalog).find(o=>o.key===ownerKey.value),release=props.releases.find(r=>r.id===libraryId.value&&!r.withdrawn);
 if(!selection||!release)throw new Error('所选对象或能力库已变化，请刷新后重新选择。');
 const c=preview.catalog,lib=JSON.parse(release.payload).kcs,list=c[collections[selection.ownerType]!],o=list.find((o:Item)=>o.id===selection.ownerId);
 const mappings=selection.ownerType==='Question'?o.mappings:o.kcIds.map((id:string)=>({kcId:id,role:'Primary',share:0,mode:'None',step:null}));
 proposal.value={evidencePolicy:selection.ownerType==='Question'?o.policy:'NoEvidence',items:mappings.map((m:Item,i:number)=>({kcId:m.kcId,kcRevisionId:lib.find((k:Item)=>k.id===m.kcId)?.revisionId||c.kcs.find((k:Item)=>k.id===m.kcId)?.revisionId,role:m.role,coverageWeight:c.mappingCoverage.find((r:Item)=>r.ownerType===selection.ownerType&&r.ownerId===o.id&&r.kcId===m.kcId&&r.role===m.role&&r.evidenceMode===m.mode&&r.step===m.step&&r.evidenceShare===m.share)?.coverageWeight??1,evidenceShare:m.share,evidenceMode:m.mode,step:m.step,sequence:i+1,modelScore:null,sourceRefs:[`draft:${draftId.value}/${selection.ownerType}:${selection.ownerRevisionId}`]}))};
 prepared.value={sourceDraftId:draftId.value,expectedDraftVersion:preview.draftVersion,libraryReleaseId:release.id,...selection,library:lib,object:o};reason.value='';
 notice.value='已载入实际原映射和覆盖权重；保存仅建立待审核草稿。';
 });}
function setKC(i:Item,id:string){i.kcId=id;i.kcRevisionId=prepared.value!.library.find((k:Item)=>k.id===id).revisionId;}
function mode(i:Item){if(i.evidenceMode==='None'){i.evidenceShare=0;i.step=null}else{if(!i.evidenceShare)i.evidenceShare=i.evidenceMode==='WholeItem'?1:.5;if(i.evidenceMode!=='StepObserved')i.step=null}}
function role(i:Item){if(['Context','Prerequisite'].includes(i.role)){i.evidenceMode='None';mode(i)}}
function policy(){if(proposal.value.evidencePolicy==='NoEvidence')proposal.value.items.forEach((i:Item)=>{i.evidenceMode='None';mode(i)})}
function add(){const p=prepared.value!,k=p.library[0];if(!k)return;proposal.value.items.push({kcId:k.id,kcRevisionId:k.revisionId,role:p.ownerType==='Question'?'Context':'Primary',coverageWeight:1,evidenceShare:0,evidenceMode:'None',step:null,sequence:proposal.value.items.length+1,modelScore:null,sourceRefs:[`draft:${p.sourceDraftId}/${p.ownerType}:${p.ownerRevisionId}`]})}
async function save(){await run(async()=>{const p=prepared.value!;saved.value=await api('/content/mapping-sets',{sourceDraftId:p.sourceDraftId,expectedDraftVersion:p.expectedDraftVersion,libraryReleaseId:p.libraryReleaseId,ownerType:p.ownerType,ownerId:p.ownerId,ownerRevisionId:p.ownerRevisionId,...proposal.value,reason:reason.value.trim(),provider:'Manual'});await loadHistory();emit('refresh');notice.value='独立映射草稿已保存，尚未审核、发布或改变学生绑定。';});}
async function open(id:string){await run(async()=>{saved.value=await api('/content/mapping-sets/'+id)})}
onMounted(()=>run(loadHistory));
</script>
<template><SourceEvidenceButton v-if="draftId" stage="drafts" :task-id="draftId"/>
<section class="card independent-mapping" aria-label="独立映射草稿">
<h2>独立映射草稿</h2><p class="muted">直接维护一个题目、课时或资源的能力关联。保存会建立新映射版次和待审核内容草稿，原发布与学习记录保持原依据。</p>
<p v-if="error" class="warning" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
<details><summary>建立一版映射草稿</summary>
<label>独立映射来源<select v-model="draftId" :disabled="blocked"><option value="">选择来源草稿</option><option v-for="d in drafts" :key="d.id" :value="d.id">{{d.title}} · 第 {{d.version}} 版</option></select></label>
<label>映射使用的正式能力库<select v-model="libraryId" :disabled="blocked"><option value="">选择正式版本</option><option v-for="r in releases.filter(r=>!r.withdrawn)" :key="r.id" :value="r.id">内容版本 {{r.number}}</option></select></label>
<template v-if="source"><label>查找映射对象<input v-model="search" :disabled="blocked" placeholder="输入题目或名称"></label><label v-for="o in visible" :key="o.key" class="choice"><input type="radio" v-model="ownerKey" :value="o.key" :disabled="blocked"><span>{{names[o.ownerType]}} · {{o.title}}</span></label><div class="actions"><button @click="page--" :disabled="blocked||page===0">上一页映射对象</button><span>第 {{page+1}} 页 · {{owners.length}} 项</span><button @click="page++" :disabled="blocked||(page+1)*20>=owners.length">下一页映射对象</button></div></template>
<button @click="prepare" :disabled="blocked||!draftId||!libraryId||!ownerKey">载入原映射</button>
<template v-if="prepared">
<p>已固定来源第 {{prepared.expectedDraftVersion}} 版 · {{names[prepared.ownerType]}} · {{prepared.title}}</p>
<details><summary>核对来源内容</summary><p v-if="prepared.ownerType==='Question'">参考答案：{{prepared.object.answer}}<br>{{prepared.object.explanation}}</p><p v-else>{{prepared.object.paperReference||prepared.object.title}}</p></details>
<label v-if="prepared.ownerType==='Question'">草稿题目归因<select v-model="proposal.evidencePolicy" @change="policy" :disabled="blocked"><option v-for="(label,id) in policies" :key="id" :value="id">{{label}}</option></select></label><p v-else>课时与资源只表示教学覆盖，不产生作答证据。</p>
<div v-for="(i,index) in proposal.items" :key="index" class="mapping-item">
<label>草稿关联能力<select :value="i.kcId" @change="setKC(i,($event.target as HTMLSelectElement).value)" :disabled="blocked"><option v-if="!prepared.library.some((k:Item)=>k.id===i.kcId)" :value="i.kcId">原能力不在所选正式库，请明确校正</option><option v-for="k in prepared.library" :key="k.id" :value="k.id">{{k.name}}</option></select></label>
<p class="muted">{{prepared.library.find((k:Item)=>k.id===i.kcId)?.behavior}}<br>{{prepared.library.find((k:Item)=>k.id===i.kcId)?.boundary}}</p>
<div class="fields"><label>草稿教学覆盖权重<input v-model.number="i.coverageWeight" type="number" min="0" max="1" step="0.000001" :disabled="blocked"></label><template v-if="prepared.ownerType==='Question'"><label>草稿映射角色<select v-model="i.role" @change="role(i)" :disabled="blocked"><option v-for="(label,id) in roles" :key="id" :value="id">{{label}}</option></select></label><label>草稿测量方式<select v-model="i.evidenceMode" @change="mode(i)" :disabled="blocked"><option v-for="(label,id) in modes" :key="id" :value="id">{{label}}</option></select></label><label>草稿证据预算份额<input v-model.number="i.evidenceShare" type="number" min="0" max="1" step="0.000001" :disabled="blocked||i.evidenceMode==='None'"></label><label v-if="i.evidenceMode==='StepObserved'">草稿独立观察点<input v-model="i.step" maxlength="100" :disabled="blocked"></label></template></div>
<button @click="proposal.items.splice(Number(index),1);proposal.items.forEach((i:Item,n:number)=>i.sequence=n+1)" :disabled="blocked">移除草稿关联</button>
</div>
<button @click="add" :disabled="blocked||proposal.items.length>=20">添加草稿关联</button><label>映射维护依据<textarea v-model="reason" maxlength="4000" :disabled="blocked"></textarea></label><button class="primary" @click="save" :disabled="blocked||!reason.trim()">保存独立映射草稿</button>
</template>
</details>
<details><summary>已保存的独立映射</summary><p v-if="!history.total">尚未保存独立映射草稿。</p><article v-for="s in history.sets" :key="s.setRevisionId"><p>{{names[s.ownerType]}} · 映射第 {{s.revisionNo}} 版 · {{s.reviewStatus==='Draft'?'待审核':'整份内容已审核'}}<br>{{s.reason}}</p><button @click="open(s.setRevisionId)" :disabled="blocked">查看这版映射</button></article><div class="actions"><button @click="run(async()=>{historyPage--;await loadHistory()})" :disabled="blocked||historyPage===0">上一页已存映射</button><span>共 {{history.total}} 版</span><button @click="run(async()=>{historyPage++;await loadHistory()})" :disabled="blocked||(historyPage+1)*20>=history.total">下一页已存映射</button></div></details>
<div v-if="saved" class="mapping-item"><h3>保存的映射第 {{saved.set.revisionNo}} 版</h3><p>{{saved.set.reviewStatus==='Draft'?'待审核 · 不用于新会话':'已完成整份内容审核'}} · 来源草稿第 {{saved.source.sourceDraftVersion}} 版</p><p>维护依据：{{saved.source.reason}}</p><p v-for="i in saved.items" :key="i.id">{{saved.library.find((k:Item)=>k.id===i.kcId)?.name}} · 覆盖 {{i.coverageWeight}} · 证据份额 {{i.evidenceShare}} · {{modes[i.evidenceMode]}}{{i.step?' · '+i.step:''}}</p><p v-for="w in saved.draftValidationWarnings" :key="w" class="warning">{{w}}</p><button @click="emit('editDraft',saved.draft.id)" :disabled="blocked">继续核对独立映射内容草稿</button></div>
</section>
</template>
<style scoped>
.fields{display:grid;grid-template-columns:repeat(auto-fit,minmax(min(220px,100%),1fr));gap:12px}.choice{display:flex;align-items:flex-start;gap:10px;margin:12px 0}.choice input{width:20px;height:20px;flex:0 0 20px;margin:2px 0 0}.choice span,.independent-mapping p{overflow-wrap:anywhere;min-width:0}.mapping-item{padding:12px;margin:14px 0;background:#f8faf6;border-left:3px solid #dce7db}.actions{display:flex;gap:10px;align-items:center;flex-wrap:wrap;margin:12px 0}.independent-mapping select,.independent-mapping input,.independent-mapping textarea{max-width:100%;min-width:0}
</style>
