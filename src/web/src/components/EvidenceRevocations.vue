<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {api} from '../api';
type Item=Record<string,any>;
const props=defineProps<{studentId:string}>();const data=ref<Item|null>(null),offset=ref(0),error=ref(''),busy=ref(false);
async function load(){busy.value=true;error.value='';try{data.value=await api(`/students/${props.studentId}/evidence-revocations?offset=${offset.value}&limit=20`)}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
function evidence(row:Item){return data.value!.evidence.find((e:Item)=>e.id===row.evidenceId)}
function batch(row:Item){return data.value!.batches.find((b:Item)=>b.id===row.correctionBatchId)}
onMounted(load);
</script>
<template><section class="card" aria-label="证据撤销记录"><h2>证据撤销记录</h2><p class="muted">只记录已确认更正导致的错误证据。正常历史版本退休、相同结果重新确认不算撤销；原证据和原始答案保留。</p><p v-if="error" class="warning" role="alert">{{error}}</p><template v-if="data"><p>已撤销 {{data.total}} 条历史证据</p><article v-for="r in data.revocations" :key="r.id" class="content-editor-row"><p>{{batch(r)?.cause==='Grading'?'判分更正':'映射更正'}} · {{r.effect==='DirectCorrection'?'直接更正':'后续重放影响'}}</p><p>{{r.reason}}</p><p>原{{evidence(r)?.positive?'正':'负'}}证据 {{evidence(r)?.weight}} · {{evidence(r)?.part}}</p><details><summary>核对撤销依据</summary><p>原证据：{{r.evidenceId}}</p><p>已确认更正：{{r.correctionBatchId}}</p><p>替代评估：{{r.replacementGenerationId}}</p></details></article><button @click="offset-=20;load()" :disabled="busy||offset===0">上一页撤销记录</button><span> 第 {{Math.floor(offset/20)+1}} 页 </span><button @click="offset+=20;load()" :disabled="busy||offset+20>=data.total">下一页撤销记录</button></template></section></template>
<style scoped>p{overflow-wrap:anywhere}</style>
