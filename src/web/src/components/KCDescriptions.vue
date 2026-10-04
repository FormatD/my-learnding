<script setup lang="ts">
defineProps<{definition:Record<string,any>}>();
const complexity:Record<string,string>={Easy:'简单',Medium:'中等',Hard:'较复杂'};
</script>
<template>
  <div class="muted" aria-label="能力描述">
    <p>能力领域：{{definition.domain||'未记录'}}</p>
    <p>能力复杂度：{{complexity[definition.difficultyLevel]||'未记录'}}；与题目难度分开记录。</p>
    <p>能力认知层级：{{definition.cognitiveLevel||'未记录'}}</p>
  </div>
</template>
