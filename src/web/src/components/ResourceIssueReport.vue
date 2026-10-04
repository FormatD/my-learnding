<script setup lang="ts">
import {ref} from 'vue';import {api} from '../api';
const props=defineProps<{taskId:string,disabled:boolean}>();
let submission:{reason:string,key:string}|null=null;
const open=ref(false),reason=ref(''),busy=ref(false),error=ref(''),saved=ref(false);
async function report(){if(busy.value||props.disabled||!reason.value.trim())return;busy.value=true;error.value='';try{const text=reason.value.trim();if(submission?.reason!==text)submission={reason:text,key:crypto.randomUUID()};await api(`/tasks/${props.taskId}/resource-issues`,{reason:text},'POST',submission.key);saved.value=true;open.value=false}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
</script>
<template><div aria-label="报告资源问题"><p v-if="saved" role="status">已告诉家长。任务还没有完成；可以返回今日任务，等待家长处理。</p><button v-else-if="!open" :disabled="disabled||busy" @click="open=true">资源打不开或不合适</button><form v-if="open&&!saved" @submit.prevent="report"><label>资源遇到什么问题？<textarea v-model="reason" maxlength="500" required :disabled="disabled||busy"></textarea></label><button :disabled="disabled||busy||!reason.trim()">告诉家长</button><button type="button" :disabled="busy" @click="open=false">取消</button></form><p v-if="error" role="alert">{{error}}</p></div></template>
