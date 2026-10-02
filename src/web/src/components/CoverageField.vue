<script setup lang="ts">
import {computed} from 'vue';
import {coverageRow,coverageLabel} from '../coverage';
type Item=Record<string,any>;
const props=defineProps<{catalog:Item,type:string,ownerId:string,mapping:Item,busy:boolean}>();
const row=computed(()=>coverageRow(props.catalog,props.type,props.ownerId,props.mapping));
function change(event:Event){row.value.coverageWeight=Number((event.target as HTMLInputElement).value);row.value.origin='Explicit';row.value.sourceSetRevisionId=null;}
</script>
<template><label>教学覆盖权重<input :value="row.coverageWeight" @input="change" type="number" min="0" max="1" step="0.01" :disabled="busy"></label><p class="muted">{{coverageLabel(row)}} · 作答证据份额 {{mapping.share}}</p></template>
