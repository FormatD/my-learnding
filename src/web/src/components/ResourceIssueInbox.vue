<script setup lang="ts">
import {ref,onMounted,onUnmounted} from 'vue';import {api} from '../api';
const props=defineProps<{studentId:string}>();const rows=ref<any[]>([]),busy=ref(false),error=ref('');let alive=true;onUnmounted(()=>{alive=false});
async function load(){busy.value=true;error.value='';try{const value=await api(`/students/${props.studentId}/resource-issues`);if(alive)rows.value=value}catch(e){if(alive)error.value=(e as Error).message}finally{if(alive)busy.value=false}}onMounted(load);
</script>
<template><section class="card" aria-label="孩子报告的资源问题"><h2>资源问题 · 最近50条</h2><p class="muted">孩子报告后，任务状态和学习证据保持。请核对资源，必要时调整任务或重新审核内容；报告完整保留在学生导出中。</p><button :disabled="busy" @click="load">刷新资源问题</button><p v-if="error" role="alert">{{error}}</p><p v-if="!busy&&!rows.length">暂时没有资源问题报告。</p><article v-for="row in rows" :key="row.id" class="content-editor-row"><h3>{{row.issue.title}}</h3><p>{{row.issue.reason}}</p><p>{{row.issue.resourceRef}}</p><p class="muted">{{new Date(row.createdAt).toLocaleString()}}</p></article></section></template>
