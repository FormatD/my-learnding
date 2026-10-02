<script setup lang="ts">
import {computed,onMounted,ref} from 'vue';
import {api} from '../api';
type Item=Record<string,any>;
const props=defineProps<{draftId:string}>();
const rows=ref<Item[]>([]),error=ref(''),selected=ref(''),page=ref(0);
const review=computed(()=>rows.value.find(r=>r.id===selected.value));
const catalog=computed(()=>review.value?JSON.parse(review.value.sourcePayload):null);
const questions=computed(()=>catalog.value?.questions.slice(page.value*20,(page.value+1)*20)||[]);
function name(id:string){return catalog.value?.kcs.find((k:Item)=>k.id===id)?.name||'未记录能力';}
onMounted(async()=>{try{rows.value=await api('/content/drafts/'+props.draftId+'/reviews');selected.value=rows.value[0]?.id||'';}catch(e){error.value=(e as Error).message;}});
</script>
<template>
<section class="card" aria-label="完整内容审核记录">
  <h2>完整内容审核记录</h2><p class="muted">保存审核当时的完整内容。这里记录整份内容确认；逐项映射决定可在映射工作台查看。</p>
  <p v-if="error" class="warning" role="alert">{{error}}</p>
  <p v-if="!rows.length&&!error">尚无独立审核快照。旧版本的历史审核内容未补造。</p>
  <label v-if="rows.length">选择审核记录<select v-model="selected" @change="page=0"><option v-for="r in rows" :key="r.id" :value="r.id">{{r.sourceTitle}} · 草稿第 {{r.draftVersion}} 版 · {{new Date(r.reviewedAt).toLocaleString('zh-CN')}}</option></select></label>
  <template v-if="review&&catalog">
    <p>审核范围：完整内容 · {{new Date(review.reviewedAt).toLocaleString('zh-CN')}} · {{review.publishedReleaseId?'已关联实际发布':'未用于发布'}}</p>
    <p>{{review.reasonSource==='UserProvided'?'审核备注':'确认记录'}}：{{review.reason}}</p>
    <p>能力 {{catalog.kcs.length}} 项 · 题目 {{catalog.questions.length}} 题 · 课时 {{catalog.lessons.length}} 项 · 资源 {{catalog.resources.length}} 项</p>
    <details><summary>核对审核时的题目与映射</summary>
      <article v-for="q in questions" :key="q.id" class="content-editor-row"><p>{{q.stem}}</p><p>答案：{{q.answer}}<br>解析：{{q.explanation}}</p><p>能力关联：{{q.mappings.map((m:Item)=>name(m.kcId)+(m.step?' · '+m.step:'')).join('、')||'无'}}</p></article>
      <button @click="page--" :disabled="page===0">上一页题目</button><span> 第 {{page+1}} 页 </span><button @click="page++" :disabled="(page+1)*20>=catalog.questions.length">下一页题目</button>
    </details>
    <details><summary>核对审核时的课时与资源</summary><article v-for="l in catalog.lessons" :key="l.id" class="content-editor-row"><p>课时：{{l.title}}</p><p>{{l.kcIds.map(name).join('、')}}</p></article><article v-for="r in catalog.resources" :key="r.id" class="content-editor-row"><p>资源：{{r.title}} · {{r.minutes}} 分钟</p><p>{{r.paperReference}}</p><p>{{r.kcIds.map(name).join('、')}}</p></article></details>
  </template>
</section>
</template>
<style scoped>p{overflow-wrap:anywhere}select{max-width:100%;min-width:0}</style>
