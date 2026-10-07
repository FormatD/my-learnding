<script setup lang="ts">
import {ref,onMounted,onUnmounted} from 'vue';
defineProps<{title:string}>();const emit=defineEmits<{close:[]}>(),dialog=ref<HTMLDialogElement|null>(null);
let previousOverflow='';onMounted(()=>{previousOverflow=document.body.style.overflow;document.body.style.overflow='hidden';dialog.value?.showModal()});onUnmounted(()=>document.body.style.overflow=previousOverflow);
</script>
<template><Teleport to="body"><dialog ref="dialog" class="review-dialog" :aria-label="title" @cancel.prevent="emit('close')" @click="e=>{if(e.target===dialog)emit('close')}"><header><h2>{{title}}</h2><button autofocus @click="emit('close')" aria-label="关闭复核面板">关闭</button></header><div class="review-body"><slot/></div></dialog></Teleport></template>
<style scoped>.review-dialog{position:fixed;inset:0;width:min(1440px,96vw);height:94dvh;max-width:96vw;max-height:94dvh;margin:auto;padding:0;border:1px solid #b5cbbb;border-radius:16px;box-shadow:0 24px 80px #183d3b55;color:#23372f;background:#fafcf8}.review-dialog::backdrop{background:#142e2dcc;backdrop-filter:blur(2px)}header{display:flex;align-items:center;justify-content:space-between;gap:16px;padding:14px 22px;border-bottom:1px solid #d7e3d4;background:#f1f6ee}h2{font-size:20px;margin:0}header button{flex-shrink:0}.review-body{height:calc(100% - 70px);overflow:auto;padding:18px;box-sizing:border-box}@media(max-width:760px){.review-dialog{width:100vw;max-width:100vw;height:100dvh;max-height:100dvh;border-radius:0}.review-body{padding:12px}header{padding:12px}}
</style>
