<script setup lang="ts">
import {ref,onMounted,onUnmounted} from 'vue';
import {api} from '../api';
const props=defineProps<{owner:boolean}>();const drafts=ref<Record<string,any>>({});
const page=ref(1),data=ref<any>({calls:[],total:0}),busy=ref(false),error=ref('');
const status:Record<string,string>={Started:'已开始，结果尚未确认',Returned:'已收到返回',Failed:'调用失败',Cancelled:'已取消等待，费用未确认',Denied:'预算未允许，未开始调用'};
const cursors=ref<(string|null)[]>([null]);let alive=true;
onUnmounted(()=>{alive=false});
async function load(next=page.value,refresh=false){
 busy.value=true;error.value='';
 try{
  const cursor=refresh?null:cursors.value[next-1];
  const result=await api(`/builder/calls/window?pageSize=20${cursor?'&cursor='+encodeURIComponent(cursor):''}`);
  if(!alive)return;
  if(refresh)cursors.value=[null];
  data.value=result;page.value=refresh?1:next;
  cursors.value=cursors.value.slice(0,page.value);if(result.nextCursor)cursors.value.push(result.nextCursor);
 }catch(e){if(alive)error.value=(e as Error).message}finally{if(alive)busy.value=false}
}
function local(call:any){return call.quotePayload&&JSON.parse(call.quotePayload).localNoCharge&&call.billingStatus!=='Confirmed'}
function draft(call:any){return drafts.value[call.id]??=({providerFinished:false,chargedCost:call.chargedCost??'',inputTokens:call.inputTokens??'',outputTokens:call.outputTokens??'',reason:'',receiptReference:''})}
function resolved(call:any){return data.value.reconciliations?.find((r:any)=>r.callId===call.id)}
async function reconcile(call:any){busy.value=true;error.value='';try{const d=draft(call),free=local(call);await api(`/builder/calls/${call.id}:reconcile`,{providerFinished:d.providerFinished,chargedCost:free?0:d.chargedCost,currency:free?null:'USD',inputTokens:free?null:d.inputTokens,outputTokens:free?null:d.outputTokens,reason:d.reason,receiptReference:d.receiptReference});await load()}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
onMounted(()=>load());
</script>
<template><section class="card" aria-label="建库调用账本"><h2>建库调用账本</h2><p class="muted">每次调用单独保留，包括修复和失败。已开始但未确认的结果不会当成未发生；旧任务没有记录时不推算用量。</p><button :disabled="busy" @click="load(1,true)">刷新调用账本</button><p v-if="error" role="alert">{{error}}</p><article v-for="call in data.calls" :key="call.id" class="content-editor-row"><h3>{{call.repair?'修复调用':'候选调用'}} · {{status[call.status]||call.status}}</h3><p>{{new Date(call.startedAt).toLocaleString()}} · {{call.model}}</p><p>费用：{{call.status==='Denied'?'未发生':call.billingStatus==='LocalNoCharge'?'本地模拟，不计费':call.billingStatus==='LocalMeasured'?'本机模型，无外部账单':call.billingStatus==='Confirmed'?`${call.chargedCost} ${call.currency}`:'尚未确认'}}；输入/输出 Token：{{call.status==='Denied'||call.billingStatus==='LocalNoCharge'?'不适用':`${call.inputTokens??'未记录'} / ${call.outputTokens??'未记录'}`}}</p><p v-if="call.elapsedMilliseconds!=null">本次等待 {{call.elapsedMilliseconds}} 毫秒</p><p v-if="call.status==='Denied'">未开始提供者调用，没有费用或Token用量。</p><p v-if="resolved(call)">已追加家庭负责人核对：{{resolved(call).reason}}。原调用事实保留。</p><form v-if="props.owner&&call.status!=='Denied'&&call.budgetState!=='Settled'&&!resolved(call)" class="form-grid" @submit.prevent="reconcile(call)"><label><input type="checkbox" v-model="draft(call).providerFinished" required>已确认提供者调用或本地进程已结束</label><template v-if="!local(call)"><label>实际 USD 费用<input v-model="draft(call).chargedCost" type="number" min="0" step="0.000001" required></label><label>实际输入 Token<input v-model.number="draft(call).inputTokens" type="number" min="0" step="1" required></label><label>实际输出 Token<input v-model.number="draft(call).outputTokens" type="number" min="0" step="1" required></label></template><label>核对原因<textarea v-model="draft(call).reason" maxlength="4000" required></textarea></label><label>账单或本地进程核对依据<input v-model="draft(call).receiptReference" maxlength="500" required></label><button :disabled="busy" type="submit">追加调用核对</button></form><p v-if="call.errorCode">失败编号：{{call.errorCode}}</p></article><p v-if="!data.total">还没有逐次调用记录；旧历史不会补造。</p><p>{{data.total}} 条 · 第 {{page}} 页</p><button :disabled="busy||page===1" @click="load(page-1)">上一页</button><button :disabled="busy||!data.nextCursor" @click="load(page+1)">下一页</button></section></template>
