<script setup lang="ts">
import {onUnmounted,ref} from 'vue';
import {api,captureFamilyVersion} from '../api';
import {parseContentImport,type ContentImport} from '../contentImport';
const props=defineProps<{disabled:boolean}>();
const emit=defineEmits<{created:[draft:Record<string,any>]}>();
const preview=ref<ContentImport|null>(null),reading=ref(false),saving=ref(false),error=ref(''),notice=ref('');
let alive=true,selection=0,attempt:{body:ContentImport,key:string,version:string}|null=null;
onUnmounted(()=>{alive=false;selection++;});
async function choose(event:Event){
 const input=event.target as HTMLInputElement,file=input.files?.[0];input.value='';if(!file||props.disabled||saving.value)return;
 const token=++selection;reading.value=true;preview.value=null;attempt=null;error.value='';notice.value='';
 try{if(file.size>2_000_000)throw new Error('题包最多 2 MB，请按单元拆分。');const parsed=parseContentImport(await file.text());if(alive&&token===selection&&!props.disabled)preview.value=parsed;}
 catch(e){if(alive&&token===selection)error.value=(e as Error).message;}
 finally{if(alive&&token===selection)reading.value=false;}
}
async function save(){
 if(!preview.value||props.disabled||reading.value||saving.value)return;
 attempt??={body:JSON.parse(JSON.stringify(preview.value)),key:crypto.randomUUID(),version:captureFamilyVersion()};const request=attempt;
 saving.value=true;error.value='';
 try{const draft=await api('/content/drafts',request.body,'POST',request.key,request.version);if(alive){preview.value=null;attempt=null;notice.value='题包已保存为待审核草稿。请逐题核对答案、测量范围与资源。';emit('created',draft);}}
 catch(e){if(alive)error.value=(e as Error).message;}
 finally{if(alive)saving.value=false;}
}
</script>
<template>
<section class="card" aria-label="导入本地题包">
 <h2>导入本地题包</h2>
 <p class="muted">选择本地 JSON 题包，先查看数量，再保存为独立的待审核草稿。答案、能力映射、教材适配和资源仍需逐项核对。</p>
 <label>选择本地题包文件<input type="file" accept=".json,application/json" :disabled="disabled||reading||saving" @change="choose"></label>
 <p v-if="disabled" class="muted">请先保存或取消正在编辑的草稿。</p>
 <p v-if="reading" role="status">正在读取本地题包…</p>
 <p v-if="error" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
 <div v-if="preview"><h3>{{preview.title}}</h3><p>{{preview.catalog.kcs.length}} 个能力 · {{preview.catalog.questions.length}} 道题 · {{preview.catalog.resources.length}} 份资源 · {{preview.catalog.lessons.length}} 个课时</p>
 <p class="muted">此预览只确认文件可读取，数量不表示内容已经审核或覆盖整个教材。</p>
 <button :disabled="disabled||reading||saving" @click="save">{{saving?'正在保存…':'保存为待审核草稿'}}</button>
 <button :disabled="disabled||reading||saving" @click="preview=null;attempt=null;error=''">取消导入</button></div>
</section>
</template>
