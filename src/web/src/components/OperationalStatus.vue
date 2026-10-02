<script setup lang="ts">
import {onMounted,onUnmounted,ref} from 'vue';
import {api} from '../api';
const props=defineProps<{detailed:boolean}>(),emit=defineEmits<{details:[]}>();
const state=ref<any>(null),busy=ref(false),error=ref(''),notice=ref('');let timer:ReturnType<typeof setTimeout>|undefined;let disposed=false;
async function load(){if(busy.value||disposed)return;busy.value=true;try{const result=await api('/operations');if(!disposed){state.value=result;error.value='';}}catch(e){if(!disposed)error.value=(e as Error).message}finally{busy.value=false}}
function poll(){timer=setTimeout(async()=>{if(document.visibilityState==='visible')await load();if(!disposed)poll();},10000)}
async function retry(id:string){if(busy.value)return;busy.value=true;notice.value='';try{await api(`/jobs/${id}:retry`,{});notice.value='已请求重新处理原始记录；请等待后台更新，无需重新提交答案。';}catch(e){notice.value=(e as Error).message}finally{busy.value=false;await load();}}
function gigabytes(bytes:number){return (bytes/1073741824).toFixed(1)+' GB'}
const backupNames:Record<string,string>={NotConfigured:'待配置',Running:'正在备份',Succeeded:'最近归档成功',Failed:'最近备份失败',Unavailable:'状态无法核对'};
onMounted(async()=>{await load();if(!disposed)poll()});onUnmounted(()=>{disposed=true;clearTimeout(timer)});
</script>
<template>
<div v-if="!detailed&&(error||state?.signals.length)" class="warning" role="status"><p v-if="error">无法获取后台运行状态：{{error}}</p><p v-for="signal in state?.signals||[]">{{signal.title}} · {{signal.count}} 项</p><button @click="emit('details')">查看后台处理</button></div>
<section v-if="detailed" class="card" aria-label="后台运行状态"><h2>后台运行状态</h2><p class="muted">学习结果只显示当前家庭；负责人另可查看本机备份与存储。自动每10秒检查，离开家长模式后停止；后台日志独立检查异常。</p><button @click="load" :disabled="busy">检查后台状态</button><p v-if="error" class="warning" role="alert">{{error}}</p><p v-if="notice" role="status">{{notice}}</p>
<template v-if="state"><p class="muted">最近检查：{{new Date(state.observedAt).toLocaleString()}}</p><div v-for="signal in state.signals" class="warning"><strong>{{signal.title}} · {{signal.count}} 项</strong><p>{{signal.guidance}}</p></div><p v-if="!state.signals.length">当前家庭未发现结果积压或建库异常。</p>
<h3>学习结果更新</h3><p>待处理 {{state.projection.pending}} 项 · 需人工处理 {{state.projection.failed}} 项<span v-if="state.projection.oldestSeconds!==null"> · 最长等待 {{Math.ceil(state.projection.oldestSeconds)}} 秒</span></p><div v-for="job in state.projection.jobs" :key="job.id" class="content-row"><div><strong>{{job.student}}</strong><p>{{job.retries>=3?'自动重试已停止':'等待后台处理'}} · 已自动重试 {{job.retries}} 次</p></div><button v-if="job.canRetry" @click="retry(job.id)" :disabled="busy">重新处理原始结果</button></div>
<h3>接口请求</h3><p>本次服务启动后的最近15分钟：{{state.requests.requests}} 次请求、{{state.requests.serverErrors}} 次服务异常、{{state.requests.rejected}} 次校验或权限拒绝。</p><p class="muted">{{state.requests.notice}}</p>
<template v-if="state.builder"><h3>辅助建库</h3><p>等待 {{state.builder.queued}} · 已停止失败 {{state.builder.failed}} · 需要文字识别 {{state.builder.needsOCR}} · 未审核候选 {{state.builder.unreviewed}} · 未发布草稿 {{state.builder.drafts}}</p><p class="muted">建库失败请在辅助建库中核对来源，不能直接重新提交学习答案。</p></template>
<template v-if="state.storage"><h3>本地存储</h3><p v-if="state.storage.available">可用 {{gigabytes(state.storage.freeBytes)}} / 总计 {{gigabytes(state.storage.totalBytes)}}</p><p class="muted">{{state.storage.notice}}</p></template>
<template v-if="state.backup"><h3>完整备份</h3><p>{{backupNames[state.backup.status]||'待核对'}}</p><template v-if="state.backup.configured"><p>调度进程：{{state.backup.schedulerActive?'已确认运行':'未确认运行'}}</p><p v-if="state.backup.lastSnapshotAt">最近成功快照：{{new Date(state.backup.lastSnapshotAt).toLocaleString()}} · 距今 {{state.backup.snapshotAgeHours.toFixed(1)}} 小时</p><p v-else>尚无可核对的成功快照。</p><p>归档摘要：{{state.backup.archiveVerified?'已核对':'未通过核对'}} · 实际恢复：{{state.backup.restoreVerified?'已验证':'待演练'}} · 独立物理磁盘：{{state.backup.independentDisk?'已验证':'待确认'}}</p><p v-if="state.backup.nextDueAt">下次到期：{{new Date(state.backup.nextDueAt).toLocaleString()}}</p><p v-if="state.backup.errorCode" class="muted">错误编号：{{state.backup.errorCode}}</p></template><p class="muted">{{state.backup.notice}}</p></template><h3>模型连接</h3><p>{{state.model.notice}}</p></template></section>
</template>
