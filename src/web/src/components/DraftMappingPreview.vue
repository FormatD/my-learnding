<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {api} from '../api';
import {coverageLabel} from '../coverage';
type Item=Record<string,any>;
const props=defineProps<{draftId:string}>();const data=ref<Item|null>(null),error=ref(''),page=ref(0);
const labels:Record<string,string>={Question:'题目',Resource:'资源',Lesson:'课时'};
const rows=computed(()=>data.value?.catalog.mappingCoverage||[]);
function title(row:Item){const collection={Question:'questions',Resource:'resources',Lesson:'lessons'}[row.ownerType as 'Question'|'Resource'|'Lesson'];const o=data.value?.catalog[collection].find((o:Item)=>o.id===row.ownerId);return o?.stem||o?.title||'未命名内容';}
function kc(row:Item){return data.value?.catalog.kcs.find((k:Item)=>k.id===row.kcId)?.name||'未命名能力';}
onMounted(async()=>{try{data.value=await api(`/content/drafts/${props.draftId}/mapping-preview`)}catch(e){error.value=(e as Error).message;}});
</script>
<template><section class="card" aria-label="待审映射预览"><h2>待审教学覆盖与证据份额</h2><p v-if="error" class="warning" role="alert">{{error}}</p><template v-if="data"><p>{{data.title}} · 草稿版本 {{data.draftVersion}}</p><p class="muted">审核发布包括以下权重。教学覆盖用于表示内容关联；作答证据仍按题目的归因规则计算。继承表示有真实旧映射来源，新增默认值需核对；旧内容缺少修订时不会补造权重。</p><article v-for="(r,index) in rows.slice(page*20,(page+1)*20)" :key="index" class="content-editor-row"><h3>{{labels[r.ownerType]}} · {{title(r)}}</h3><p>{{kc(r)}}<span v-if="r.step"> · {{r.step}}</span></p><p>教学覆盖：{{r.coverageWeight}} · 作答证据份额：{{r.evidenceShare}} · {{coverageLabel(r)}}</p></article><p v-if="!rows.length">尚无具有明确修订的内容关联。</p><template v-if="rows.length"><button @click="page--" :disabled="page===0">上一页待审映射</button><span> 第 {{page+1}} 页 </span><button @click="page++" :disabled="(page+1)*20>=rows.length">下一页待审映射</button></template></template></section></template>
<style scoped>h3,p{overflow-wrap:anywhere}</style>
