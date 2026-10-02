<script setup lang="ts">
import {ref,onMounted} from 'vue';
import {api,localDate} from '../api';
const props=defineProps<{studentId:string,timeZone:string,burden:any}>();const emit=defineEmits<{saved:[]}>();
const allRecords=ref<any>(null);async function load(){try{allRecords.value=await api(`/students/${props.studentId}/parent-burden`);}catch(e:any){error.value=e.message;}}onMounted(load);
const date=ref(localDate(props.timeZone)),category=ref('Daily'),minutes=ref<number|null>(null),note=ref(''),reason=ref(''),editing=ref(''),busy=ref(false),error=ref('');
function edit(r:any){editing.value=r.id;date.value=r.date;category.value=r.category;minutes.value=r.minutes;note.value=r.note;reason.value='';error.value='';}
function reset(){editing.value='';date.value=localDate(props.timeZone);category.value='Daily';minutes.value=null;note.value='';reason.value='';}
async function save(){busy.value=true;error.value='';try{await api(editing.value?`/parent-burden/${editing.value}:correct`:`/students/${props.studentId}/parent-burden`,{date:date.value,category:category.value,minutes:minutes.value,note:note.value,reason:reason.value});reset();await load();emit('saved');}catch(e:any){error.value=e.message;}finally{busy.value=false;}}
</script>
<template>
  <section class="card">
    <h2>家长投入记录</h2><p class="muted">由家长填写实际耗时。日常确认与调整、集中建库与审核分开统计；没有记录的日期不按零投入计算，补记也不代表已完成连续试用。</p>
    <p v-if="burden?.recordCount">本周已记录 {{burden.recordedDays}} 天 · 日常维护 {{burden.dailyMinutes}} 分钟 · 内容审核 {{burden.contentReviewMinutes}} 分钟</p><p v-else class="empty">本周尚未记录投入，暂不能评价家长维护成本。</p>
    <form @submit.prevent="save" class="burden-form">
      <label>投入日期<input type="date" v-model="date" :max="localDate(timeZone)" required></label>
      <label>投入类别<select v-model="category"><option value="Daily">日常确认与调整</option><option value="ContentReview">集中建库与内容审核</option></select></label>
      <label>实际投入（分钟，家长填写）<input type="number" v-model.number="minutes" min="0.1" max="240" step="0.1" required></label>
      <label>投入说明（可选）<input v-model="note" maxlength="500" placeholder="例如：核对作业并调整明日安排"></label>
      <label v-if="editing">投入更正原因<input v-model="reason" maxlength="500" required></label>
      <div v-if="error"><p class="error" role="alert">{{error}}</p><button type="button" @click="load" :disabled="busy">刷新投入记录</button></div>
      <div><button class="primary" :disabled="busy">{{editing?'保存投入更正':'记录实际投入'}}</button><button v-if="editing" type="button" @click="reset" :disabled="busy">取消更正</button></div>
    </form>
    <p class="muted">下方保留最近28天填写的记录；上方周汇总只计算近7天。</p>
    <div v-for="r in allRecords?.records||[]" :key="r.id" class="content-row"><div><h3>{{r.date}} · {{r.category==='Daily'?'日常维护':'内容审核'}} · {{r.minutes}} 分钟</h3><p>{{r.note||'家长填写的实际投入'}}<span v-if="r.supersedesId"> · 已更正：{{r.correctionReason}}</span></p></div><button @click="edit(r)" :disabled="busy">更正投入记录</button></div>
    <details v-if="allRecords?.history?.some((r:any)=>allRecords.records.every((a:any)=>a.id!==r.id))"><summary>保留的投入历史</summary><p v-for="r in allRecords.history" :key="r.id">{{r.date}} · {{r.category==='Daily'?'日常维护':'内容审核'}} · {{r.minutes}} 分钟 · {{r.note}} <span v-if="r.correctionReason"> · 更正原因：{{r.correctionReason}}</span></p></details>
  </section>
</template>
<style scoped>.burden-form{display:grid;grid-template-columns:1fr 1fr;gap:14px;margin:22px 0}.burden-form>div,.burden-form>.error{grid-column:1/-1}.burden-form button{margin-right:10px}@media(max-width:760px){.burden-form{grid-template-columns:1fr}}</style>
