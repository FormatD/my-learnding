<script setup lang="ts">
import {onUnmounted,ref,watch} from 'vue';
import {readJsonText} from '../api';
const props=defineProps<{kind:'builder'|'mapping';runId?:string;disabled:boolean}>();
const busy=ref(false),error=ref(''),notice=ref('');let generation=0,alive=true;
watch(()=>[props.kind,props.runId],()=>{generation++;busy.value=false;error.value='';notice.value='';});
onUnmounted(()=>{alive=false;generation++;});
async function download(){
  if(busy.value||props.disabled||props.kind==='mapping'&&!props.runId)return;
  const kind=props.kind,id=props.runId,token=++generation;busy.value=true;error.value='';notice.value='';
  try{
    const captured=await readJsonText(kind==='builder'?'/builder':'/builder/mapping-runs/'+id);
    if(!alive||token!==generation)return;
    const stamp=new Date().toISOString().replace(/[:.]/g,'-');
    const blob=new Blob([captured],{type:'application/json'}),url=URL.createObjectURL(blob),link=document.createElement('a');
    try{link.href=url;link.download=kind==='builder'?`candidate-evaluation-${stamp}.json`:`mapping-evaluation-${id}-${stamp}.json`;link.click();}finally{setTimeout(()=>URL.revokeObjectURL(url),1000);}
    notice.value='已下载当前已保存的评测材料。尚未提交的页面校正不在文件中；下载不代表质量达标。';
  }catch(e){if(alive&&token===generation)error.value=(e as Error).message;}
  finally{if(alive&&token===generation)busy.value=false;}
}
</script>
<template>
<details class="evaluation-download" :aria-label="kind==='builder'?'候选评测材料':'映射评测材料'"><summary>保存评测材料</summary>
<p class="muted">{{kind==='builder'?'下载本家庭已保存的来源、候选、任务与尝试记录，供核对结构和来源。':'下载本次运行已保存的原始建议与人工决定，供分别核对原建议和审核后映射。'}} 文件含来源内容和审核资料，请按私有资料保管。</p>
<button @click="download" :disabled="busy||disabled||(kind==='mapping'&&!runId)">{{busy?'正在准备下载…':'下载评测材料'}}</button>
<p v-if="error" class="warning" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
</details>
</template>
