<script setup lang="ts">
import {ref,onMounted} from 'vue';
import {api} from '../api';
const page=ref(1),data=ref<any>({calls:[],total:0}),busy=ref(false),error=ref('');
const status:Record<string,string>={Started:'已开始，结果尚未确认',Returned:'已收到返回',Failed:'调用失败',Cancelled:'已取消等待，费用未确认'};
async function load(next=page.value){busy.value=true;error.value='';try{const result=await api(`/builder/calls?page=${next}&pageSize=20`);data.value=result;page.value=next}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
onMounted(()=>load());
</script>
<template><section class="card" aria-label="建库调用账本"><h2>建库调用账本</h2><p class="muted">每次调用单独保留，包括修复和失败。已开始但未确认的结果不会当成未发生；旧任务没有记录时不推算用量。</p><button :disabled="busy" @click="load()">刷新调用账本</button><p v-if="error" role="alert">{{error}}</p><article v-for="call in data.calls" :key="call.id" class="content-editor-row"><h3>{{call.repair?'修复调用':'初次调用'}} · {{status[call.status]||call.status}}</h3><p>{{new Date(call.startedAt).toLocaleString()}} · {{call.model}}</p><p>费用：{{call.billingStatus==='LocalNoCharge'?'本地模拟，不计费':call.billingStatus==='Confirmed'?`${call.chargedCost} ${call.currency}`:'尚未确认'}}；输入/输出 Token：{{call.billingStatus==='LocalNoCharge'?'不适用':`${call.inputTokens??'未记录'} / ${call.outputTokens??'未记录'}`}}</p><p v-if="call.elapsedMilliseconds!=null">本次等待 {{call.elapsedMilliseconds}} 毫秒</p><p v-if="call.errorCode">失败编号：{{call.errorCode}}</p></article><p v-if="!data.total">还没有逐次调用记录；旧历史不会补造。</p><p>{{data.total}} 条 · 第 {{page}} 页</p><button :disabled="busy||page===1" @click="load(page-1)">上一页</button><button :disabled="busy||page*20>=data.total" @click="load(page+1)">下一页</button></section></template>
