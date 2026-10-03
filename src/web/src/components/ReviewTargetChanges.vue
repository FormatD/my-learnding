<script setup lang="ts">
import {ref,onMounted,watch} from 'vue';
import {api} from '../api';
const props=defineProps<{studentId:string,refreshKey:number}>();const emit=defineEmits<{saved:[],arrange:[]}>();
const rows=ref<any[]>([]),selected=ref<any|null>(null),preview=ref<any|null>(null),action=ref('KeepOriginal'),target=ref(''),reason=ref(''),busy=ref(false),error=ref(''),notice=ref('');
async function load(){try{rows.value=await api(`/students/${props.studentId}/review-target-changes`);}catch(e:any){error.value=e.message;}}
onMounted(load);watch(()=>props.refreshKey,load);
function choose(row:any){selected.value=row;preview.value=null;action.value='KeepOriginal';target.value='';reason.value='';error.value='';notice.value='';}
async function prepare(){busy.value=true;error.value='';try{const input={action:action.value,targetId:action.value==='KeepOriginal'?null:target.value,reason:reason.value};preview.value={...await api(`/students/${props.studentId}/attempts/${selected.value.attemptId}/review-target:preview`,input),input};}catch(e:any){error.value=e.message;}finally{busy.value=false;}}
async function confirm(){busy.value=true;error.value='';try{const p=preview.value;await api(`/students/${props.studentId}/attempts/${selected.value.attemptId}/review-target:confirm`,{...p.input,previewHash:p.previewHash});preview.value=null;selected.value=null;notice.value='确认依据已保存。原目标仍需复习；待结果同步完成后，核对当前发布题目并安排明日复习。';await load();emit('saved');}catch(e:any){error.value=e.message;preview.value=null;}finally{busy.value=false;}}
function targetName(row:any){return row.measuredTargets.find((t:any)=>t.id===row.confirmation?.confirmedTargetId)?.name||row.originalTargetName;}
</script>
<template>
  <section v-if="rows.length||error" class="card" aria-label="复习目标变化确认">
    <h2>核对复习目标变化</h2><p class="muted">题目已不测量已发布任务的原知识点。原任务和历史结果保留；本次不会自动替代原目标复习，也不会转移掌握度。</p>
    <p v-if="notice" role="status">{{notice}}</p><p v-if="error" class="error" role="alert">{{error}}</p>
    <div v-for="row in rows" :key="row.attemptId" class="content-row"><div><h3>{{row.questionStem}}</h3><p>原目标：{{row.originalTargetName}} · 当前测量：{{row.measuredTargets.map((t:any)=>t.name).join('、')||'无可测知识点'}}</p><p v-if="row.confirmation">家长已确认：{{row.confirmation.action==='KeepOriginal'?'保留原目标，另排复习':`本次按${targetName(row)}核对复测`}}。原目标仍需复习。</p><p v-if="row.confirmation" class="muted">依据：{{row.confirmation.reason}}</p><p v-else class="warning">尚待家长确认。</p></div><button @click="choose(row)" :disabled="busy">{{row.confirmation?'重新核对选择':'核对目标并确认'}}</button></div>
    <form v-if="selected&&!preview" @submit.prevent="prepare"><h3>核对：{{selected.questionStem}}</h3><label>处理选择<select v-model="action" aria-label="处理选择"><option value="KeepOriginal">保留原目标，重新安排复习</option><option v-if="selected.measuredTargets.length" value="AdoptMeasuredTarget">本次按实际测量目标核对复测</option></select></label><label v-if="action==='AdoptMeasuredTarget'">实际测量目标<select v-model="target" aria-label="实际测量目标" required><option value="">选择知识点</option><option v-for="t in selected.measuredTargets" :key="t.id" :value="t.id">{{t.name}}</option></select></label><label>家长确认依据<textarea v-model="reason" required maxlength="1000"></textarea></label><button :disabled="busy">预览目标确认影响</button><button type="button" @click="selected=null" :disabled="busy">取消核对</button></form>
    <div v-if="preview" class="warning"><h3>目标确认影响预览</h3><p>{{preview.notice}}</p><p>确认依据：{{preview.reason}}</p><button class="primary" @click="confirm" :disabled="busy">确认选择并后台重算</button><button @click="preview=null" :disabled="busy">返回修改</button></div>
    <button v-if="rows.some(r=>r.confirmation)" @click="emit('arrange')" :disabled="busy">去安排明日复习</button><button @click="load" :disabled="busy">刷新目标核对记录</button>
  </section>
</template>
