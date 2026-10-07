<script setup lang="ts">
import {ref} from 'vue';import ReviewDialog from './ReviewDialog.vue';import SourceEvidence from './SourceEvidence.vue';
defineProps<{stage:string;taskId:string}>();const open=ref(false);
</script>
<template><button @click="open=true">对照原图与来源文字</button><ReviewDialog v-if="open" title="原图与来源复核" @close="open=false"><SourceEvidence :stage="stage" :task-id="taskId"/></ReviewDialog></template>
