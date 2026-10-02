<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import { api } from '../api';
type Item=Record<string,any>;
const props=defineProps<{releases:Item[];publisher:boolean;disabled:boolean}>();
const data=ref<Item>({proposals:[],items:[],migrations:[],identities:[]}),busy=ref(false),error=ref(''),notice=ref('');
const editing=ref(''),showForm=ref(false),form=ref({proposalType:'Split',rationale:'',fromReleaseId:'',effectiveReleaseId:'',fromKCIds:[] as string[],targetIds:[] as string[],weights:{} as Record<string,string>});
const decisionReasons=ref<Record<string,string>>({}),detail=ref<Item|null>(null),preview=ref<Item|null>(null),confirm=ref('');
const types:Record<string,string>={Split:'拆分',Merge:'合并',Redirect:'替换'},statuses:Record<string,string>={Draft:'草稿',InReview:'待审核',Approved:'已通过',Rejected:'已拒绝',Applied:'已记录'};
const blocked=computed(()=>busy.value||props.disabled),available=computed(()=>props.releases.filter(r=>!r.withdrawn));
function catalog(id:string){const r=props.releases.find(r=>r.id===id);return r?JSON.parse(r.payload):{kcs:[]};}
const sources=computed(()=>catalog(form.value.fromReleaseId).kcs as Item[]);
const targets=computed(()=>catalog(form.value.effectiveReleaseId).kcs.filter((k:Item)=>!form.value.fromKCIds.includes(k.id)&&(form.value.proposalType==='Redirect'||!props.releases.some(r=>r.number<(props.releases.find(r=>r.id===form.value.effectiveReleaseId)?.number||0)&&catalog(r.id).kcs.some((old:Item)=>old.id===k.id)))) as Item[]);
const shapeValid=computed(()=>form.value.proposalType==='Split'?form.value.fromKCIds.length===1&&form.value.targetIds.length>=2:form.value.proposalType==='Merge'?form.value.fromKCIds.length>=2&&form.value.targetIds.length===1:form.value.fromKCIds.length===1&&form.value.targetIds.length===1);
function clearSelections(){form.value.fromKCIds=[];form.value.targetIds=[];form.value.weights={};}
async function load(){data.value=await api('/content/kc-changes');}
async function run(action:()=>Promise<void>){if(blocked.value)return;busy.value=true;error.value='';notice.value='';try{await action()}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
function start(p?:Item){preview.value=null;detail.value=null;confirm.value='';editing.value=p?.id||'';showForm.value=true;const items=p?data.value.items.filter((i:Item)=>i.proposalId===p.id):[];form.value={proposalType:p?.proposalType||'Split',rationale:p?.rationale||'',fromReleaseId:p?.fromReleaseId||'',effectiveReleaseId:p?.effectiveReleaseId||'',fromKCIds:items.filter((i:Item)=>i.side==='From').map((i:Item)=>i.kcId),targetIds:items.filter((i:Item)=>i.side==='To').map((i:Item)=>i.kcId),weights:Object.fromEntries(items.filter((i:Item)=>i.side==='To').map((i:Item)=>[i.kcId,i.weight==null?'':String(i.weight)]))};}
async function save(){await run(async()=>{const f=form.value;const body={proposalType:f.proposalType,rationale:f.rationale,fromReleaseId:f.fromReleaseId,effectiveReleaseId:f.effectiveReleaseId,fromKCIds:f.fromKCIds,targets:f.targetIds.map(kcId=>({kcId,weight:f.weights[kcId]?.trim()?Number(f.weights[kcId]):null}))};await api('/content/kc-changes'+(editing.value?'/'+editing.value:''),body,editing.value?'PUT':'POST');showForm.value=false;editing.value='';await load();notice.value='提案草稿已保存，尚未停用旧能力或影响学习记录。';});}
async function submit(p:Item){await run(async()=>{await api(`/content/kc-changes/${p.id}:submit`,{});await load();});}
async function decide(p:Item,decision:string){await run(async()=>{await api(`/content/kc-changes/${p.id}:decide`,{decision,reason:decisionReasons.value[p.id]});await load();});}
async function openPreview(p:Item){await run(async()=>{preview.value=await api(`/content/kc-changes/${p.id}/preview`);detail.value=null;confirm.value='';});}
async function apply(){await run(async()=>{const result=await api(`/content/kc-changes/${preview.value!.proposal.id}:apply`,{previewHash:preview.value!.previewHash,confirm:confirm.value});preview.value=null;confirm.value='';await load();notice.value=result.notice;});}
function label(i:Item,p:Item){return catalog(i.side==='From'?p.fromReleaseId:p.effectiveReleaseId).kcs.find((k:Item)=>k.id===i.kcId)?.name||'原版本能力';}
function itemDescription(p:Item,side:string){return data.value.items.filter((i:Item)=>i.proposalId===p.id&&i.side===side).map((i:Item)=>label(i,p)).join('、');}
function releaseNumber(id:string){return props.releases.find(r=>r.id===id)?.number||'历史';}
onMounted(()=>run(load));
</script>
<template>
<section class="card" aria-label="能力变更提案">
<h2>能力变更提案</h2><p class="muted">先人工整理新能力、题目和映射并发布，再记录拆分、合并或替换关系。新能力从证据不足开始；替换到已有能力时保留它自己的证据。旧学习结果不会转移。</p>
<p v-if="error" class="warning" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
<button @click="start()" :disabled="blocked||available.length<2">新建能力变更提案</button><button @click="run(load)" :disabled="blocked">刷新提案</button><p v-if="available.length<2" class="muted">需要来源正式版本和较新的生效正式版本。</p>
<form v-if="showForm" @submit.prevent="save"><h3>{{editing?'编辑提案草稿':'记录变更理由与范围'}}</h3>
<label>变更类型<select v-model="form.proposalType" @change="clearSelections"><option v-for="(name,key) in types" :value="key">{{name}}</option></select></label>
<label>来源内容版本<select v-model="form.fromReleaseId" @change="clearSelections"><option value="">请选择</option><option v-for="r in available" :value="r.id">内容版本 {{r.number}}</option></select></label>
<label>生效内容版本<select v-model="form.effectiveReleaseId" @change="clearSelections"><option value="">请选择较新的版本</option><option v-for="r in available.filter(r=>r.number>(available.find(old=>old.id===form.fromReleaseId)?.number||Infinity))" :value="r.id">内容版本 {{r.number}}</option></select></label>
<fieldset><legend>来源能力</legend><label v-for="k in sources" :key="k.id"><input type="checkbox" :value="k.id" v-model="form.fromKCIds">{{k.name}}</label></fieldset>
<fieldset><legend>目标能力</legend><p v-if="!targets.length" class="muted">拆分与合并请选择在生效版本首次发布的新能力，并从生效版本移除原能力及其引用。</p><label v-for="k in targets" :key="k.id"><input type="checkbox" :value="k.id" v-model="form.targetIds">{{k.name}}</label></fieldset>
<details v-if="form.targetIds.length"><summary>可选关系权重</summary><p class="muted">仅记录关系，不会按权重分配证据或概率；可以留空。</p><label v-for="id in form.targetIds">{{targets.find(k=>k.id===id)?.name}}<input v-model="form.weights[id]" type="number" min="0.000001" max="1" step="any"></label></details>
<label>变更理由<textarea v-model="form.rationale" required maxlength="4000" placeholder="解释测量范围为何变化，以及新题目如何分别测量目标能力"></textarea></label>
<p v-if="!shapeValid" class="muted">拆分选择一个来源、至少两个目标；合并选择至少两个来源、一个目标；替换各选一个。</p><button type="submit" :disabled="blocked||!shapeValid||!form.rationale.trim()">保存提案草稿</button><button type="button" @click="showForm=false;editing=''">取消编辑</button>
</form>
<div v-for="p in data.proposals" :key="p.id" class="content-editor-row"><h3>{{types[p.proposalType]}} · {{statuses[p.status]}}</h3><p>内容版本 {{releaseNumber(p.fromReleaseId)}} → {{releaseNumber(p.effectiveReleaseId)}}</p><p>{{itemDescription(p,'From')}} → {{itemDescription(p,'To')}}</p><p>{{p.rationale}}</p>
<button @click="run(async()=>{detail=await api(`/content/kc-changes/${p.id}`);preview=null})" :disabled="blocked">查看审核历史</button>
<template v-if="p.status==='Draft'"><button @click="start(p)" :disabled="blocked">编辑提案</button><button @click="submit(p)" :disabled="blocked||showForm">提交变更审核</button></template>
<template v-if="p.status==='InReview'&&publisher"><label>审核依据<textarea v-model="decisionReasons[p.id]" maxlength="4000" placeholder="确认范围、测量题与来源，说明通过或拒绝的依据"></textarea></label><button @click="decide(p,'Approved')" :disabled="blocked||!decisionReasons[p.id]?.trim()">通过变更审核</button><button @click="decide(p,'Rejected')" :disabled="blocked||!decisionReasons[p.id]?.trim()">拒绝变更提案</button></template>
<button v-if="p.status==='Approved'&&publisher" @click="openPreview(p)" :disabled="blocked">预览记录能力变更</button><p v-if="p.status==='Applied'" class="muted">旧能力已停用，历史版本和任务仍保留；学生内容版本需由家长单独选择。</p></div>
<p v-if="!data.proposals.length" class="empty">还没有能力变更提案。</p>
<div v-if="preview" class="warning"><h3>确认记录能力变更</h3><p>{{preview.notice}}</p><p>来源：{{preview.items.filter((i:Item)=>i.side==='From').map((i:Item)=>label(i,preview!.proposal)).join('、')}}</p><p>目标：{{preview.items.filter((i:Item)=>i.side==='To').map((i:Item)=>label(i,preview!.proposal)).join('、')}}</p><label>输入“记录能力变更”<input v-model="confirm"></label><button @click="apply" :disabled="blocked||confirm!=='记录能力变更'">确认记录能力变更</button><button @click="preview=null;confirm=''">取消确认</button></div>
<div v-if="detail"><h3>审核与记录历史</h3><div v-for="e in detail.events" :key="e.id"><p>{{new Date(e.createdAt).toLocaleString()}} · 修订 {{e.version}} · {{({Created:'创建',Edited:'修改',Submitted:'提交审核',Approved:'通过',Rejected:'拒绝',Applied:'记录变更'} as Record<string,string>)[e.action]}}</p><p>{{e.reason}}</p><details><summary>当时的能力与测量范围</summary><div v-for="i in JSON.parse(e.snapshot).items"><p>{{i.side==='From'?'来源':'目标'}}：{{label(i,JSON.parse(e.snapshot).proposal)}}</p><p>{{catalog(i.side==='From'?JSON.parse(e.snapshot).proposal.fromReleaseId:JSON.parse(e.snapshot).proposal.effectiveReleaseId).kcs.find((k:Item)=>k.id===i.kcId)?.behavior}}</p></div></details></div><p v-if="detail.migrations.length">已记录 {{detail.migrations.length}} 条来源到目标关系；历史证据保持不变。</p><button @click="detail=null">收起审核历史</button></div>
</section>
</template>
<style scoped>
fieldset{border:1px solid #e3e8de;border-radius:10px;padding:10px 14px;margin:14px 0}
fieldset label{display:flex;align-items:center;gap:10px;margin:8px 0;line-height:1.5}
fieldset input[type=checkbox]{width:18px;height:18px;flex:0 0 18px;margin:0;accent-color:#22624d}
</style>
