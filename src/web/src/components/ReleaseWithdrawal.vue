<script setup lang="ts">
import {ref,computed,watch,onUnmounted} from 'vue';
import {api} from '../api';
const props=defineProps<{release:{id:string,number:number,withdrawn:boolean},publisher:boolean,bound:boolean,disabled:boolean}>();
const emit=defineEmits<{withdrawn:[id:string]}>();
const reason=ref(''),confirmed=ref(false),open=ref(false),busy=ref(false),error=ref(''),notice=ref(''),committed=ref(false);
const blocked=computed(()=>props.disabled||busy.value||props.release.withdrawn||committed.value);let alive=true;
onUnmounted(()=>{alive=false});
watch(()=>props.release.withdrawn,()=>{confirmed.value=false;open.value=false});
async function withdraw(){
 if(blocked.value||!props.publisher||!confirmed.value||!reason.value.trim())return;
 busy.value=true;error.value='';
 try{
  const result=await api(`/content/releases/${props.release.id}:withdraw`,{reason:reason.value.trim()});
  if(!alive)return;
  committed.value=true;open.value=false;confirmed.value=false;notice.value=result.notice;emit('withdrawn',props.release.id);
 }catch(e){if(alive)error.value=(e as Error).message}finally{if(alive)busy.value=false}
}
</script>
<template><div class="release-withdrawal">
 <p v-if="release.withdrawn||committed" role="status">已撤回 · 不再接受新绑定或新会话</p>
 <p v-if="(release.withdrawn||committed)&&bound" class="warning">当前学生仍绑定此版本。请核对未来任务，并绑定已审核的替代版本。</p>
 <p v-if="notice" role="status">{{notice}}</p><p v-if="error" role="alert">{{error}}</p>
 <button v-if="publisher&&!release.withdrawn&&!committed&&!open" :disabled="blocked" @click="open=true">撤回此版本</button>
 <form v-if="publisher&&open" class="form-grid" @submit.prevent="withdraw">
  <p>将撤回内容版本 {{release.number}}，阻止新绑定和新会话。已领取会话、历史作答和证据保留；撤回不会自动更正历史结果。需要继续学习时，请另行绑定已审核的替代版本。</p>
  <label>撤回原因<textarea v-model="reason" required maxlength="4000" :disabled="blocked"></textarea></label>
  <label class="withdraw-confirmation"><input type="checkbox" v-model="confirmed" :disabled="blocked">已核对此版本及影响，确认撤回</label>
  <button type="submit" :disabled="blocked||!confirmed||!reason.trim()">{{busy?'正在撤回…':'确认撤回此版本'}}</button>
  <button type="button" :disabled="busy" @click="open=false;confirmed=false;error=''">取消撤回</button>
 </form>
</div></template>

<style scoped>
.withdraw-confirmation{display:flex;align-items:center;gap:8px}.withdraw-confirmation input{width:auto;margin:0;flex:0 0 auto}
</style>
