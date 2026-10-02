<script setup lang="ts">
import {computed,onMounted,ref,watch} from 'vue';
import {api} from '../api';
type Item=Record<string,any>;
const props=defineProps<{release:Item}>();
const data=ref<Item|null>(null),error=ref(''),type=ref('All'),page=ref(0);
const catalog=computed(()=>JSON.parse(props.release.payload));
const labels:Record<string,string>={Question:'题目',Lesson:'课时',Resource:'资源'};
const modes:Record<string,string>={None:'不计能力证据',WholeItem:'整题测量',StepObserved:'独立观察步骤'};
const collections:Record<string,string>={Question:'questions',Lesson:'lessons',Resource:'resources'};
const filtered=computed(()=>data.value?.bindings.filter((b:Item)=>type.value==='All'||b.ownerType===type.value)||[]);
const bindings=computed(()=>filtered.value.slice(page.value*20,(page.value+1)*20));
function owner(b:Item){return catalog.value[collections[b.ownerType]].find((o:Item)=>o.id===b.ownerId);}
function set(b:Item){return data.value!.sets.find((s:Item)=>s.id===b.setRevisionId);}
function items(b:Item){return data.value!.items.filter((i:Item)=>i.setRevisionId===b.setRevisionId);}
function kc(id:string){return catalog.value.kcs.find((k:Item)=>k.id===id);}
function missing(value:string){const [kind,id]=value.split(':');const o=catalog.value[collections[kind]].find((o:Item)=>o.id===id);return labels[kind]+' · '+(o?.title||o?.stem||'未记录内容');}
watch(type,()=>page.value=0);
onMounted(async()=>{try{data.value=await api('/content/releases/'+props.release.id+'/mapping-sets');}catch(e){error.value=(e as Error).message;}});
</script>
<template>
<section class="card" aria-label="发布映射版本">
  <h2>内容版本 {{release.number}} 的映射版本</h2><p class="muted">发布固定到具体映射与能力版本。教学覆盖与作答证据份额分别显示，后续版本不改写这里的记录。</p>
  <p v-if="error" class="warning" role="alert">{{error}}</p>
  <template v-if="data">
    <p v-if="data.status==='Bound'">版本关联完整 · {{data.bindings.length}} 个对象</p>
    <p v-else-if="data.status==='LegacySnapshot'" class="warning">此历史发布保留原快照，尚无独立映射版本关联；没有补造历史审核。</p>
    <p v-else-if="data.status==='UnversionedOwners'" class="warning">已记录明确修订的对象；以下旧内容未记录独立修订，仍需在新草稿补齐：{{data.unversionedOwners.map(missing).join('、')}}</p>
    <p v-else class="warning">映射关联不完整，请核对后台记录后继续使用。</p>
    <label v-if="data.bindings.length">查看对象类型<select v-model="type"><option value="All">全部</option><option v-for="(label,key) in labels" :key="key" :value="key">{{label}}</option></select></label>
    <article v-for="b in bindings" :key="b.id" class="content-editor-row" :aria-label="labels[b.ownerType]+'发布映射'">
      <h3>{{labels[b.ownerType]}} · {{owner(b)?.stem||owner(b)?.title}}</h3>
      <p>映射第 {{set(b).revisionNo}} 版 · {{set(b).reviewDecisionId?'逐项映射审核':'整份内容审核'}} · {{set(b).coverageOrigin==='CatalogDefault'?'教学覆盖采用默认权重1':'保留人工覆盖权重'}}</p>
      <div v-for="i in items(b)" :key="i.id"><p>{{kc(i.kcId)?.name}}<br>教学覆盖：{{i.coverageWeight}} · 作答证据份额：{{i.evidenceShare}} · {{modes[i.evidenceMode]}}<span v-if="i.step"> · {{i.step}}</span></p></div>
    </article>
    <template v-if="data.bindings.length"><button @click="page--" :disabled="page===0">上一页映射</button><span> 第 {{page+1}} 页 </span><button @click="page++" :disabled="(page+1)*20>=filtered.length">下一页映射</button></template>
  </template>
</section>
</template>
<style scoped>p,h3{overflow-wrap:anywhere}select{min-width:0;max-width:100%}</style>
