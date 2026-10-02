<script setup lang="ts">
import { computed, onMounted, ref } from 'vue';
import {api} from '../api';
import CoverageField from './CoverageField.vue';
import {pruneCoverage,teachingMapping} from '../coverage';
const sources=ref<Record<string,any>[]>([]);onMounted(async()=>{try{sources.value=await api('/content/directory-sources')}catch(e){error.value=(e as Error).message}});
type Item=Record<string,any>;
const props=defineProps<{modelValue:Item|null,title:string,busy:boolean,publishedIds:string[]}>();
const emit=defineEmits<{save:[],cancel:[],'update:title':[string]}>();
const catalog=computed(()=>props.modelValue),error=ref('');
const identity=()=>crypto.randomUUID();
function directories(){const c=catalog.value!;c.textbooks??=[];c.units??=[];c.courses??=[];return c;}
function addTextbook(){directories().textbooks.push({id:identity(),revisionId:identity(),publisher:'',edition:'具体版本待核对',subject:'Math',grade:3,semester:'上册',sourceId:null});}
function addUnit(){const c=directories();c.units.push({id:identity(),revisionId:identity(),textbookId:c.textbooks[0]?.id,title:'',sequence:c.units.length+1});}
function addCourse(){directories().courses.push({id:identity(),revisionId:identity(),provider:'',subject:'English',title:'',sourceRefs:[]});}
function owner(l:Item,value:string){l.unitId=null;l.courseId=null;if(value.startsWith('unit:'))l.unitId=value.slice(5);if(value.startsWith('course:'))l.courseId=value.slice(7);}

function addKC(){catalog.value!.kcs.push({id:identity(),revisionId:identity(),code:'MATH.CUSTOM.'+identity().replaceAll('-',''),name:'',behavior:'',boundary:'',type:'Procedure',requiredCoverage:['Basic']});}
function linked(id:string){const c=catalog.value!;return c.questions.some((q:Item)=>q.mappings.some((m:Item)=>m.kcId===id))||c.resources.some((r:Item)=>r.kcIds.includes(id))||c.lessons.some((l:Item)=>l.kcIds.includes(id))||c.relations.some((r:Item)=>r.from===id||r.to===id);}
function addQuestion(){catalog.value!.questions.push({id:identity(),revisionId:identity(),stem:'',answer:'',explanation:'',type:'Numeric',difficulty:'Medium',policy:'SingleKC',mappings:[{kcId:catalog.value!.kcs[0].id,role:'Primary',share:1,mode:'WholeItem',step:null}],coverage:'Basic',variantGroupId:null,hint:''});}
function policy(q:Item){const kc=q.mappings.find((m:Item)=>m.role==='Primary')?.kcId||catalog.value!.kcs[0]?.id;if(q.policy==='SingleKC')q.mappings=[{kcId:kc,role:'Primary',share:1,mode:'WholeItem',step:null}];if(q.policy==='NoEvidence')q.mappings=[{kcId:kc,role:'Context',share:0,mode:'None',step:null}];if(q.policy==='ObservedSteps'){q.type='MultiStep';q.mappings=[{kcId:kc,role:'Primary',share:1,mode:'StepObserved',step:'步骤1'}];}}
function addResource(){catalog.value!.resources.push({id:identity(),revisionId:identity(),title:'',paperReference:'',minutes:5,kcIds:[catalog.value!.kcs[0].id],url:null});}
function addLesson(){catalog.value!.lessons.push({id:identity(),title:'',sequence:catalog.value!.lessons.length+1,revisionId:identity(),unitId:null,courseId:null,estimatedMinutes:5,sourceRefs:[],kcIds:[catalog.value!.kcs[0].id]});}
function addRelation(){catalog.value!.relations.push({from:catalog.value!.kcs[0].id,to:catalog.value!.kcs[1].id,type:'Prerequisite'});}
function save(){error.value='';if(!props.title.trim()){error.value='请填写草稿标题。';return;}pruneCoverage(catalog.value!);emit('save');}

</script>
<template>
<section class="card content-authoring" aria-label="内容草稿编辑器">
  <h2>编辑内容草稿</h2><p class="muted">可以先保存未完成的草稿，发布前必须补齐并审核测量题。保存会退回待审核。已有能力的测量含义保持固定；改变测量范围时请新增独立能力，历史证据不会复制过去。</p>
  <p class="muted">教学覆盖权重与作答证据份额分别核对。既有映射可继承真实权重；关联能力、观察点或份额变化后采用新的默认值，保存前请核对。</p>
  <label>标题<input :value="title" @input="emit('update:title',($event.target as HTMLInputElement).value)" :disabled="busy"></label>
  <details open><summary>教材、单元与课程目录</summary><p class="muted">版本说明应填写实际教材信息。目录只组织内容，不代表孩子已掌握能力；来源引用由辅助建库导入后记录。</p>
    <div v-for="b in catalog?.textbooks||[]" :key="b.id" class="content-editor-row"><label>出版社或教材版本<input v-model="b.publisher" :disabled="busy"></label><label>教材印次与版本说明<input v-model="b.edition" :disabled="busy"></label><label>教材学科<select v-model="b.subject"><option value="Math">数学</option><option value="English">英语</option><option value="Reading">阅读</option><option value="Other">其他</option></select></label><label>教材年级<input v-model.number="b.grade" type="number" min="1" max="12"></label><label>教材学期<input v-model="b.semester"></label><label>教材原始来源<select v-model="b.sourceId"><option :value="null">尚未记录原始来源</option><option v-for="s in sources" :value="s.id">{{s.title}}</option></select></label><button @click="catalog!.textbooks.splice(catalog!.textbooks.indexOf(b),1)" :disabled="busy||(catalog?.units||[]).some((u:Item)=>u.textbookId===b.id)">移除教材</button></div><button @click="addTextbook" :disabled="busy">新增教材</button>
    <div v-for="u in catalog?.units||[]" :key="u.id" class="content-editor-row"><label>单元所属教材<select v-model="u.textbookId"><option v-for="b in catalog?.textbooks||[]" :value="b.id">{{b.publisher}} · {{b.grade}} 年级{{b.semester}} · {{b.edition}}</option></select></label><label>单元名称<input v-model="u.title"></label><label>单元顺序<input v-model.number="u.sequence" type="number" min="1"></label><button @click="catalog!.units.splice(catalog!.units.indexOf(u),1)" :disabled="busy||catalog?.lessons.some((l:Item)=>l.unitId===u.id)">移除单元</button></div><button @click="addUnit" :disabled="busy||!catalog?.textbooks?.length">新增教材单元</button>
    <div v-for="c in catalog?.courses||[]" :key="c.id" class="content-editor-row"><label>课程名称<input v-model="c.title"></label><label>课程提供方<input v-model="c.provider"></label><label>课程学科<select v-model="c.subject"><option value="Math">数学</option><option value="English">英语</option><option value="Reading">阅读</option><option value="Other">其他</option></select></label><label>课程原始来源（可多选）<select v-model="c.sourceRefs" multiple><option v-for="s in sources" :value="s.id">{{s.title}}</option></select></label><button @click="catalog!.courses.splice(catalog!.courses.indexOf(c),1)" :disabled="busy||catalog?.lessons.some((l:Item)=>l.courseId===c.id)">移除课程</button></div><button @click="addCourse" :disabled="busy">新增课程</button>
  </details>
  <details open><summary>能力定义 · {{catalog?.kcs.length}}</summary>
    <div v-for="(k,index) in catalog?.kcs" :key="k.id" class="content-editor-row" :aria-label="`能力 ${Number(index)+1}`">
      <label>能力名称<input v-model="k.name" :disabled="busy"></label>
      <label>独立可测行为<input v-model="k.behavior" :readonly="publishedIds.includes(k.id)" :disabled="busy"></label>
      <label>测量边界与排除范围<input v-model="k.boundary" :readonly="publishedIds.includes(k.id)" :disabled="busy"></label>
      <label>能力类型<select v-model="k.type" :disabled="busy||publishedIds.includes(k.id)"><option value="Procedure">计算与操作</option><option value="Concept">概念理解</option><option value="Application">应用与建模</option></select></label>
      <button @click="catalog!.kcs.splice(Number(index),1)" :disabled="busy||linked(k.id)">移除此能力</button><p v-if="linked(k.id)" class="muted">已有题目、资源、课时或前置关系引用，须先调整引用。</p>
    </div><button @click="addKC" :disabled="busy">新增独立能力</button>
  </details>
  <details open><summary>测量题与答案 · {{catalog?.questions.length}}</summary>
    <div v-for="(q,index) in catalog?.questions" :key="q.id" class="content-editor-row" :aria-label="`题目 ${Number(index)+1}`">
      <h3>第 {{Number(index)+1}} 题</h3><label>题干<textarea v-model="q.stem" :disabled="busy"></textarea></label>
      <div class="inline-fields"><label>参考答案<input v-model="q.answer" :disabled="busy"></label><label>判分方式<select v-model="q.type" :disabled="busy||q.policy==='ObservedSteps'"><option value="Numeric">精确数值</option><option value="ShortAnswer">家长判分</option><option value="Fill">精确文字</option><option value="MultiStep">多步骤，家长观察</option><option v-if="q.type==='Choice'" value="Choice">精确选项</option></select></label><label>难度<select v-model="q.difficulty" :disabled="busy"><option value="Easy">简单</option><option value="Medium">中等</option><option value="Hard">较难</option></select></label></div>
      <label>讲解说明<textarea v-model="q.explanation" :disabled="busy"></textarea></label><label>提示内容<input v-model="q.hint" :disabled="busy"></label>
      <label>如何产生能力证据<select v-model="q.policy" @change="policy(q)" :disabled="busy"><option value="SingleKC">整题只测量一个能力</option><option value="ObservedSteps">只按家长观察到的步骤</option><option value="NoEvidence">不产生能力证据</option></select></label>
      <label v-if="q.policy==='SingleKC'">测量能力<select v-model="q.mappings[0].kcId" :disabled="busy"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label>
      <div v-if="q.policy==='ObservedSteps'"><p class="muted">各步骤份额合计最多 1。未观察步骤不分配证据，也不重新分配其份额。</p><div v-for="(m,mi) in q.mappings" class="content-editor-row"><label>观察步骤名称<input v-model="m.step" :disabled="busy"></label><label>步骤测量能力<select v-model="m.kcId" :disabled="busy"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><label>测量份额<input v-model.number="m.share" type="number" min="0" max="1" step="0.05" :disabled="busy"></label><button @click="q.mappings.splice(Number(mi),1)" :disabled="busy">移除观察步骤</button></div><button @click="q.mappings.push({kcId:catalog!.kcs[0].id,role:'Secondary',share:0,mode:'StepObserved',step:''})" :disabled="busy">增加观察步骤</button></div>
      <div v-for="m in q.mappings" class="content-editor-row"><p>{{catalog?.kcs.find((k:Item)=>k.id===m.kcId)?.name}}<span v-if="m.step"> · {{m.step}}</span></p><CoverageField :catalog="catalog!" type="Question" :owner-id="q.id" :mapping="m" :busy="busy" /></div>
      <button @click="catalog!.questions.splice(Number(index),1)" :disabled="busy">移除此题</button>
    </div><button @click="addQuestion" :disabled="busy||!catalog?.kcs.length">新增测量题</button>
  </details>
  <details open><summary>讲解资源 · {{catalog?.resources.length}}</summary>
    <div v-for="(r,index) in catalog?.resources" :key="r.id" class="content-editor-row" :aria-label="`资源 ${Number(index)+1}`"><label>资源名称<input v-model="r.title" :disabled="busy"></label><label>纸笔材料与执行说明<textarea v-model="r.paperReference" :disabled="busy"></textarea></label><label>预计分钟<input v-model.number="r.minutes" type="number" min="1" max="180" :disabled="busy"></label><label>可选网页链接（HTTPS）<input v-model="r.url" type="url" :disabled="busy"></label><label>讲解哪些能力（可多选）<select v-model="r.kcIds" multiple :disabled="busy"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><div v-for="id in r.kcIds" class="content-editor-row"><p>{{catalog?.kcs.find((k:Item)=>k.id===id)?.name}}</p><CoverageField :catalog="catalog!" type="Resource" :owner-id="r.id" :mapping="teachingMapping(id)" :busy="busy" /></div><button @click="catalog!.resources.splice(Number(index),1)" :disabled="busy">移除此资源</button></div><button @click="addResource" :disabled="busy||!catalog?.kcs.length">新增讲解资源</button>
  </details>
  <details open><summary>课时与进度入口 · {{catalog?.lessons.length}}</summary>
    <div v-for="(l,index) in catalog?.lessons" :key="l.id" class="content-editor-row" :aria-label="`课时 ${Number(index)+1}`"><label>课时名称<input v-model="l.title" :disabled="busy"></label><label>课时归属<select :value="l.unitId?'unit:'+l.unitId:l.courseId?'course:'+l.courseId:''" @change="owner(l,($event.target as HTMLSelectElement).value)"><option value="">未记录归属</option><option v-for="u in catalog?.units||[]" :value="'unit:'+u.id">教材单元 · {{u.title}}</option><option v-for="c in catalog?.courses||[]" :value="'course:'+c.id">课程 · {{c.title}}</option></select></label><label>课时原始来源（可多选）<select v-model="l.sourceRefs" multiple><option v-for="s in sources" :value="s.id">{{s.title}}</option></select></label><label>课时预计分钟<input v-model.number="l.estimatedMinutes" type="number" min="1" max="180"></label><label>课时顺序<input v-model.number="l.sequence" type="number" min="1" :disabled="busy"></label><label>本课时能力（可多选）<select v-model="l.kcIds" multiple :disabled="busy"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><div v-for="id in l.kcIds" class="content-editor-row"><p>{{catalog?.kcs.find((k:Item)=>k.id===id)?.name}}</p><CoverageField :catalog="catalog!" type="Lesson" :owner-id="l.id" :mapping="teachingMapping(id)" :busy="busy" /></div><button @click="catalog!.lessons.splice(Number(index),1)" :disabled="busy">移除此课时</button></div><button @click="addLesson" :disabled="busy||!catalog?.kcs.length">新增课时</button>
  </details>
  <details><summary>前置能力关系 · {{catalog?.relations.length}}</summary><div v-for="(r,index) in catalog?.relations" class="content-editor-row"><label>需要先会<select v-model="r.from" :disabled="busy"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><label>然后学习<select v-model="r.to" :disabled="busy"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><button @click="catalog!.relations.splice(Number(index),1)" :disabled="busy">移除此关系</button></div><button @click="addRelation" :disabled="busy||(catalog?.kcs.length||0)<2">新增前置关系</button></details>
  <p v-if="error" class="warning" role="alert">{{error}}</p><button class="primary" @click="save" :disabled="busy">保存并退回待审核</button><button @click="emit('cancel')" :disabled="busy">取消</button>
</section>
</template>
<style scoped>
.content-authoring details{margin:24px 0}.content-authoring textarea{min-height:80px;resize:vertical}.content-authoring select[multiple]{min-height:100px}.content-authoring input[readonly]{background:#f2f4ef}.content-authoring .content-editor-row{padding:18px 0}
</style>
