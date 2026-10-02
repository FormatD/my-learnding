<script setup lang="ts">
import {onMounted,ref} from 'vue';
import {api} from '../api';
const props=defineProps<{owner:boolean;ownerId:string|null}>();
const members=ref<any[]>([]),busy=ref(false),error=ref(''),notice=ref('');
const input=ref({userName:'',password:'',roles:['Parent']}),reason=ref('');
const preview=ref<any>(null),password=ref(''),confirm=ref('');
const roleNames:Record<string,string>={Parent:'家长',ContentEditor:'内容编辑',Publisher:'发布审核'};
async function run(action:()=>Promise<void>){if(busy.value)return;busy.value=true;error.value='';notice.value='';try{await action()}catch(e){error.value=(e as Error).message}finally{busy.value=false}}
async function load(){members.value=(await api('/family/members')).map((m:any)=>({...m,selected:m.roles?m.roles.split(','):[]}));}
async function create(){await run(async()=>{await api('/family/members',input.value);input.value={userName:'',password:'',roles:['Parent']};await load();notice.value='成员已创建，可使用自己的账号登录。';});}
async function roles(m:any){await run(async()=>{await api(`/family/members/${m.id}/roles`,{roles:m.selected,reason:reason.value},'PUT');reason.value='';await load();notice.value='权限已更新，相关成员与孩子需要重新登录。';});}
async function remove(){await run(async()=>{const receipt=await api('/family:delete',{previewHash:preview.value.previewHash,password:password.value,confirm:confirm.value});password.value='';sessionStorage.setItem('family-deletion-receipt',JSON.stringify(receipt));window.location.reload();});}
onMounted(()=>{if(props.owner)run(load)});
</script>
<template>
<section class="card" aria-label="家庭数据管理"><h2>家庭数据与成员</h2><p v-if="!owner" class="muted">全家导出、成员权限与全家删除由家庭负责人管理。</p><template v-else>
<p v-if="error" role="alert" class="alert error">{{error}}</p><p v-if="notice" role="status" class="alert success">{{notice}}</p>
<h3>导出全家数据</h3><p>导出所有学生、内容、业务历史及原始附件。文件不包含密码和登录凭据，请妥善保管。</p><a href="/api/v1/family/export" class="export-link">下载全家数据包</a>
<h3>添加成员</h3><form @submit.prevent="create" class="form-grid"><label>成员用户名<input v-model="input.userName" minlength="3" required autocomplete="off"></label><label>初始密码<input v-model="input.password" type="password" minlength="12" maxlength="128" required autocomplete="new-password"></label><fieldset><legend>成员权限</legend><label v-for="(name,role) in roleNames"><input type="checkbox" :value="role" v-model="input.roles">{{name}}</label></fieldset><button :disabled="busy||!input.roles.length">添加成员</button></form>
<p class="muted">家长可管理学习记录；内容编辑可准备草稿；发布审核可编辑和发布内容。内容权限不会授予学生作答访问权。</p>
<h3>已有成员</h3><label>权限调整依据<input v-model="reason" placeholder="说明为什么调整成员权限"></label><div v-for="m in members" :key="m.id" class="content-row"><div><strong>{{m.userName}}{{m.accountId===ownerId?' · 负责人':''}}</strong><label v-for="(name,role) in roleNames"><input type="checkbox" :value="role" v-model="m.selected" :disabled="m.accountId===ownerId">{{name}}</label><p v-if="!m.selected.length">已停用：不能登录</p></div><button v-if="m.accountId!==ownerId" @click="roles(m)" :disabled="busy||!reason.trim()">保存成员权限</button></div>
<h3>永久删除全家数据</h3><p>此操作包括所有成员账户、学生记录、内容和私有附件。请先保存需要保留的导出包。</p><button @click="run(async()=>{preview=await api('/family/delete-preview')})" :disabled="busy">预览全家删除范围</button><div v-if="preview" class="warning"><strong>{{preview.family}}</strong><p>{{preview.notice}}</p><details><summary>查看记录数量</summary><dl><template v-for="(count,name) in preview.counts"><dt>{{name}}</dt><dd>{{count}}</dd></template></dl></details><label>负责人密码<input v-model="password" type="password" autocomplete="current-password"></label><label>输入“永久删除家庭”<input v-model="confirm"></label><button @click="remove" :disabled="busy||confirm!=='永久删除家庭'||!password">确认永久删除全家数据</button><button @click="preview=null;password='';confirm=''">取消全家删除</button></div>
</template></section>
</template>
