<script setup lang="ts">
import { computed, onMounted, onUnmounted, ref, watch } from 'vue';
import {api} from '../api';
import {prepareFileUpload,type FileUploadAttempt} from '../fileUpload';
import CoverageField from './CoverageField.vue';
import {pruneCoverage,teachingMapping} from '../coverage';
const sources=ref<Record<string,any>[]>([]);onMounted(async()=>{try{sources.value=await api('/content/directory-sources')}catch(e){error.value=(e as Error).message}});
type Item=Record<string,any>;
const props=defineProps<{modelValue:Item|null,title:string,busy:boolean,publishedIds:string[],expectedVersion:string}>();
const emit=defineEmits<{save:[],cancel:[],'update:title':[string],version:[string]}>();
const catalog=computed(()=>props.modelValue),error=ref('');
const uploading=ref(false),blocked=computed(()=>props.busy||uploading.value);const uploadAttempts=new Map<string,FileUploadAttempt>();let alive=true;onUnmounted(()=>{alive=false;uploadAttempts.clear()});
watch(()=>props.modelValue,()=>uploadAttempts.clear());
const identity=()=>crypto.randomUUID();
function directories(){const c=catalog.value!;c.textbooks??=[];c.units??=[];c.courses??=[];return c;}
function addTextbook(){directories().textbooks.push({id:identity(),revisionId:identity(),publisher:'',edition:'具体版本待核对',subject:'Math',grade:3,semester:'上册',sourceId:null});}
function addUnit(){const c=directories();c.units.push({id:identity(),revisionId:identity(),textbookId:c.textbooks[0]?.id,title:'',sequence:c.units.length+1});}
function addCourse(){directories().courses.push({id:identity(),revisionId:identity(),provider:'',subject:'English',title:'',sourceRefs:[]});}
function owner(l:Item,value:string){l.unitId=null;l.courseId=null;if(value.startsWith('unit:'))l.unitId=value.slice(5);if(value.startsWith('course:'))l.courseId=value.slice(7);}

function addKC(){catalog.value!.kcs.push({id:identity(),revisionId:identity(),code:'MATH.CUSTOM.'+identity().replaceAll('-',''),name:'',behavior:'',boundary:'',type:'Procedure',requiredCoverage:['Basic'],subject:'MATH',gradeMin:3,gradeMax:3});}
function linked(id:string){const c=catalog.value!;return c.questions.some((q:Item)=>q.mappings.some((m:Item)=>m.kcId===id))||c.resources.some((r:Item)=>r.kcIds.includes(id))||c.lessons.some((l:Item)=>l.kcIds.includes(id))||c.relations.some((r:Item)=>r.from===id||r.to===id);}
function addQuestion(){catalog.value!.questions.push({id:identity(),revisionId:identity(),stem:'',answer:'',explanation:'',type:'Numeric',difficulty:'Medium',policy:'SingleKC',mappings:[{kcId:catalog.value!.kcs[0].id,role:'Primary',share:1,mode:'WholeItem',step:null}],coverage:'Basic',variantGroupId:null,hint:''});}
function policy(q:Item){const kc=q.mappings.find((m:Item)=>m.role==='Primary')?.kcId||catalog.value!.kcs[0]?.id;if(q.policy==='SingleKC')q.mappings=[{kcId:kc,role:'Primary',share:1,mode:'WholeItem',step:null}];if(q.policy==='NoEvidence')q.mappings=[{kcId:kc,role:'Context',share:0,mode:'None',step:null}];if(q.policy==='ObservedSteps'){q.type='MultiStep';q.mappings=[{kcId:kc,role:'Primary',share:1,mode:'StepObserved',step:'步骤1'}];}}
async function uploadResource(event:Event,r:Item){
 const input=event.target as HTMLInputElement,file=input.files?.[0];if(!file||blocked.value)return;
 const model=props.modelValue,acceptedVersions=new Set([props.expectedVersion]);uploading.value=true;error.value='';
 const advance=(v:string)=>{if(alive&&props.modelValue===model&&acceptedVersions.has(props.expectedVersion)){acceptedVersions.add(v);emit('version',v);}};
 try{const prepared=await prepareFileUpload(file,'LearningResource',props.expectedVersion),prior=uploadAttempts.get(r.id);const attempt=prior?.fingerprint===prepared.fingerprint?prior:prepared;uploadAttempts.set(r.id,attempt);
 const uploaded=await attempt.run(advance);
 if(alive&&props.modelValue===model&&acceptedVersions.has(props.expectedVersion)){r.fileId=uploaded.id;r.fileSnapshotHash=uploaded.fileSnapshotHash;r.revisionId=identity();if(!r.title)r.title=file.name;uploadAttempts.delete(r.id);}
 }catch(e){if(alive)error.value=(e as Error).message}finally{uploading.value=false;input.value='';}
}
function addResource(){catalog.value!.resources.push({id:identity(),revisionId:identity(),title:'',paperReference:'',minutes:5,kcIds:[catalog.value!.kcs[0].id],url:null});}
function addLesson(){catalog.value!.lessons.push({id:identity(),title:'',sequence:catalog.value!.lessons.length+1,revisionId:identity(),unitId:null,courseId:null,estimatedMinutes:5,sourceRefs:[],kcIds:[catalog.value!.kcs[0].id]});}
function addRelation(){catalog.value!.relations.push({from:catalog.value!.kcs[0].id,to:catalog.value!.kcs[1].id,type:'Prerequisite'});}
function save(){if(blocked.value)return;error.value='';for(const k of catalog.value!.kcs){for(const key of ['domain','cognitiveLevel','difficultyLevel']){if(typeof k[key]==='string')k[key]=k[key].trim();if(k[key]==null||k[key]==='')delete k[key];}if((k.domain?.length||0)>200||(k.cognitiveLevel?.length||0)>100||/[\u0000-\u001f\u007f-\u009f]/u.test((k.domain||'')+(k.cognitiveLevel||''))||(k.difficultyLevel&&!['Easy','Medium','Hard'].includes(k.difficultyLevel))){error.value='请检查能力领域、复杂度和认知层级，描述须为单行且不超过标注字数。';return;}if(k.gradeMin==='')delete k.gradeMin;if(k.gradeMax==='')delete k.gradeMax;if(k.subject==null&&k.gradeMin==null&&k.gradeMax==null)continue;if(!k.subject||!Number.isInteger(k.gradeMin)||!Number.isInteger(k.gradeMax)||k.gradeMin<1||k.gradeMax>12||k.gradeMin>k.gradeMax){error.value='请同时填写能力学科与1～12年级的适用范围，起始年级不能超过结束年级。';return;}}if(!props.title.trim()){error.value='请填写草稿标题。';return;}pruneCoverage(catalog.value!);emit('save');}

</script>
<template>
<section class="card content-authoring" aria-label="内容草稿编辑器">
  <h2>编辑内容草稿</h2><p class="muted">可以先保存未完成的草稿，发布前必须补齐并审核测量题。保存会退回待审核。已有能力的测量含义保持固定；改变测量范围时请新增独立能力，历史证据不会复制过去。</p>
  <p class="muted">教学覆盖权重与作答证据份额分别核对。既有映射可继承真实权重；关联能力、观察点或份额变化后采用新的默认值，保存前请核对。</p>
  <label>标题<input :value="title" @input="emit('update:title',($event.target as HTMLInputElement).value)" :disabled="blocked"></label>
  <details open><summary>教材、单元与课程目录</summary><p class="muted">版本说明应填写实际教材信息。目录只组织内容，不代表孩子已掌握能力；来源引用由辅助建库导入后记录。</p>
    <div v-for="b in catalog?.textbooks||[]" :key="b.id" class="content-editor-row"><label>出版社或教材版本<input v-model="b.publisher" :disabled="blocked"></label><label>教材印次与版本说明<input v-model="b.edition" :disabled="blocked"></label><label>教材学科<select v-model="b.subject"><option value="Math">数学</option><option value="English">英语</option><option value="Reading">阅读</option><option value="Other">其他</option></select></label><label>教材年级<input v-model.number="b.grade" type="number" min="1" max="12"></label><label>教材学期<input v-model="b.semester"></label><label>教材原始来源<select v-model="b.sourceId"><option :value="null">尚未记录原始来源</option><option v-for="s in sources" :value="s.id">{{s.title}}</option></select></label><button @click="catalog!.textbooks.splice(catalog!.textbooks.indexOf(b),1)" :disabled="blocked||(catalog?.units||[]).some((u:Item)=>u.textbookId===b.id)">移除教材</button></div><button @click="addTextbook" :disabled="blocked">新增教材</button>
    <div v-for="u in catalog?.units||[]" :key="u.id" class="content-editor-row"><label>单元所属教材<select v-model="u.textbookId"><option v-for="b in catalog?.textbooks||[]" :value="b.id">{{b.publisher}} · {{b.grade}} 年级{{b.semester}} · {{b.edition}}</option></select></label><label>单元名称<input v-model="u.title"></label><label>单元顺序<input v-model.number="u.sequence" type="number" min="1"></label><button @click="catalog!.units.splice(catalog!.units.indexOf(u),1)" :disabled="blocked||catalog?.lessons.some((l:Item)=>l.unitId===u.id)">移除单元</button></div><button @click="addUnit" :disabled="blocked||!catalog?.textbooks?.length">新增教材单元</button>
    <div v-for="c in catalog?.courses||[]" :key="c.id" class="content-editor-row"><label>课程名称<input v-model="c.title"></label><label>课程提供方<input v-model="c.provider"></label><label>课程学科<select v-model="c.subject"><option value="Math">数学</option><option value="English">英语</option><option value="Reading">阅读</option><option value="Other">其他</option></select></label><label>课程原始来源（可多选）<select v-model="c.sourceRefs" multiple><option v-for="s in sources" :value="s.id">{{s.title}}</option></select></label><button @click="catalog!.courses.splice(catalog!.courses.indexOf(c),1)" :disabled="blocked||catalog?.lessons.some((l:Item)=>l.courseId===c.id)">移除课程</button></div><button @click="addCourse" :disabled="blocked">新增课程</button>
  </details>
  <details open><summary>能力定义 · {{catalog?.kcs.length}}</summary>
    <div v-for="(k,index) in catalog?.kcs" :key="k.id" class="content-editor-row" :aria-label="`能力 ${Number(index)+1}`">
      <label>能力名称<input v-model="k.name" :disabled="blocked"></label>
      <label>能力学科<select v-model="k.subject" :disabled="blocked"><option :value="null">未记录学科（旧定义）</option><option value="MATH">数学</option><option value="ENGLISH">英语</option><option value="CHINESE">语文</option><option value="SCIENCE">科学</option><option value="OTHER">其他</option></select></label>
      <label>适用起始年级<input v-model.number="k.gradeMin" type="number" min="1" max="12" :disabled="blocked" placeholder="旧定义未记录"></label><label>适用结束年级<input v-model.number="k.gradeMax" type="number" min="1" max="12" :disabled="blocked" placeholder="旧定义未记录"></label>
      <p class="muted">学科和年级属于本能力定义，不从教材或编码推定。补充旧定义需重新审核发布；已记录的正式能力不能改换或清空学科。年级范围调整不复制历史证据。</p>
      <label>能力领域<input v-model="k.domain" maxlength="200" :disabled="blocked" placeholder="例如：数与运算；未知可留空"></label>
      <label>能力复杂度<select v-model="k.difficultyLevel" :disabled="blocked"><option :value="null">未记录</option><option value="Easy">简单</option><option value="Medium">中等</option><option value="Hard">较复杂</option></select></label>
      <label>能力认知层级<input v-model="k.cognitiveLevel" maxlength="100" :disabled="blocked" placeholder="例如：理解与应用；未知可留空"></label>
      <p class="muted">这些描述由审核人填写，不从题目或教材推定。能力复杂度与每道题的难度分开记录；修改描述需要重新审核发布，不改变历史判分。</p>
      <label>独立可测行为<input v-model="k.behavior" :readonly="publishedIds.includes(k.id)" :disabled="blocked"></label>
      <label>测量边界与排除范围<input v-model="k.boundary" :readonly="publishedIds.includes(k.id)" :disabled="blocked"></label>
      <label>能力类型<select v-model="k.type" :disabled="blocked||publishedIds.includes(k.id)"><option value="Procedure">计算与操作</option><option value="Concept">概念理解</option><option value="Representation">表征与转换</option><option value="Strategy">解题策略</option><option value="Application">应用与建模</option><option value="Expression">表达与说明</option><option value="Misconception">误区辨析（兼容已有类型）</option></select></label>
      <button @click="catalog!.kcs.splice(Number(index),1)" :disabled="blocked||linked(k.id)">移除此能力</button><p v-if="linked(k.id)" class="muted">已有题目、资源、课时或前置关系引用，须先调整引用。</p>
    </div><button @click="addKC" :disabled="blocked">新增独立能力</button>
  </details>
  <details open><summary>测量题与答案 · {{catalog?.questions.length}}</summary>
    <div v-for="(q,index) in catalog?.questions" :key="q.id" class="content-editor-row" :aria-label="`题目 ${Number(index)+1}`">
      <h3>第 {{Number(index)+1}} 题</h3><label>题干<textarea v-model="q.stem" :disabled="blocked"></textarea></label>
      <div class="inline-fields"><label>参考答案<input v-model="q.answer" :disabled="blocked"></label><label>判分方式<select v-model="q.type" :disabled="blocked||q.policy==='ObservedSteps'"><option value="Numeric">精确数值</option><option value="ShortAnswer">家长判分</option><option value="Fill">精确文字</option><option value="MultiStep">多步骤，家长观察</option><option v-if="q.type==='Choice'" value="Choice">精确选项</option></select></label><label>难度<select v-model="q.difficulty" :disabled="blocked"><option value="Easy">简单</option><option value="Medium">中等</option><option value="Hard">较难</option></select></label></div>
      <label>讲解说明<textarea v-model="q.explanation" :disabled="blocked"></textarea></label><label>提示内容<input v-model="q.hint" :disabled="blocked"></label>
      <label>如何产生能力证据<select v-model="q.policy" @change="policy(q)" :disabled="blocked"><option value="SingleKC">整题只测量一个能力</option><option value="ObservedSteps">只按家长观察到的步骤</option><option value="NoEvidence">不产生能力证据</option></select></label>
      <label v-if="q.policy==='SingleKC'">测量能力<select v-model="q.mappings[0].kcId" :disabled="blocked"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label>
      <div v-if="q.policy==='ObservedSteps'"><p class="muted">各步骤份额合计最多 1。未观察步骤不分配证据，也不重新分配其份额。</p><div v-for="(m,mi) in q.mappings" class="content-editor-row"><label>观察步骤名称<input v-model="m.step" :disabled="blocked"></label><label>步骤测量能力<select v-model="m.kcId" :disabled="blocked"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><label>测量份额<input v-model.number="m.share" type="number" min="0" max="1" step="0.05" :disabled="blocked"></label><button @click="q.mappings.splice(Number(mi),1)" :disabled="blocked">移除观察步骤</button></div><button @click="q.mappings.push({kcId:catalog!.kcs[0].id,role:'Secondary',share:0,mode:'StepObserved',step:''})" :disabled="blocked">增加观察步骤</button></div>
      <div v-for="m in q.mappings" class="content-editor-row"><p>{{catalog?.kcs.find((k:Item)=>k.id===m.kcId)?.name}}<span v-if="m.step"> · {{m.step}}</span></p><CoverageField :catalog="catalog!" type="Question" :owner-id="q.id" :mapping="m" :busy="blocked" /></div>
      <button @click="catalog!.questions.splice(Number(index),1)" :disabled="blocked">移除此题</button>
    </div><button @click="addQuestion" :disabled="blocked||!catalog?.kcs.length">新增测量题</button>
  </details>
  <details open><summary>讲解资源 · {{catalog?.resources.length}}</summary>
    <div v-for="(r,index) in catalog?.resources" :key="r.id" class="content-editor-row" :aria-label="`资源 ${Number(index)+1}`"><label>资源名称<input v-model="r.title" :disabled="blocked"></label><label>纸笔材料与执行说明<textarea v-model="r.paperReference" :disabled="blocked"></textarea></label><label>预计分钟<input v-model.number="r.minutes" type="number" min="1" max="180" :disabled="blocked"></label><label>上传私有学习文件（PDF、图片、WAV或MP3）<input type="file" accept="application/pdf,image/png,image/jpeg,audio/wav,audio/mpeg" :disabled="blocked" @change="uploadResource($event,r)"></label><p v-if="r.fileId">已关联私有材料，须审核发布后才能供孩子使用。 <a :href="'/api/v1/files/'+r.fileId" target="_blank" rel="noopener noreferrer">核对已关联文件 ↗</a></p><button v-if="r.fileId" type="button" :disabled="blocked" @click="delete r.fileId;delete r.fileSnapshotHash;r.revisionId=identity()">移除文件关联</button><label>可选网页链接（HTTPS）<input v-model="r.url" type="url" :disabled="blocked"></label><label>讲解哪些能力（可多选）<select v-model="r.kcIds" multiple :disabled="blocked"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><div v-for="id in r.kcIds" class="content-editor-row"><p>{{catalog?.kcs.find((k:Item)=>k.id===id)?.name}}</p><CoverageField :catalog="catalog!" type="Resource" :owner-id="r.id" :mapping="teachingMapping(id)" :busy="blocked" /></div><button @click="catalog!.resources.splice(Number(index),1)" :disabled="blocked">移除此资源</button></div><button @click="addResource" :disabled="blocked||!catalog?.kcs.length">新增讲解资源</button>
  </details>
  <details open><summary>课时与进度入口 · {{catalog?.lessons.length}}</summary>
    <div v-for="(l,index) in catalog?.lessons" :key="l.id" class="content-editor-row" :aria-label="`课时 ${Number(index)+1}`"><label>课时名称<input v-model="l.title" :disabled="blocked"></label><label>课时归属<select :value="l.unitId?'unit:'+l.unitId:l.courseId?'course:'+l.courseId:''" @change="owner(l,($event.target as HTMLSelectElement).value)"><option value="">未记录归属</option><option v-for="u in catalog?.units||[]" :value="'unit:'+u.id">教材单元 · {{u.title}}</option><option v-for="c in catalog?.courses||[]" :value="'course:'+c.id">课程 · {{c.title}}</option></select></label><label>课时原始来源（可多选）<select v-model="l.sourceRefs" multiple><option v-for="s in sources" :value="s.id">{{s.title}}</option></select></label><label>课时预计分钟<input v-model.number="l.estimatedMinutes" type="number" min="1" max="180"></label><label>课时顺序<input v-model.number="l.sequence" type="number" min="1" :disabled="blocked"></label><label>本课时能力（可多选）<select v-model="l.kcIds" multiple :disabled="blocked"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><div v-for="id in l.kcIds" class="content-editor-row"><p>{{catalog?.kcs.find((k:Item)=>k.id===id)?.name}}</p><CoverageField :catalog="catalog!" type="Lesson" :owner-id="l.id" :mapping="teachingMapping(id)" :busy="blocked" /></div><button @click="catalog!.lessons.splice(Number(index),1)" :disabled="blocked">移除此课时</button></div><button @click="addLesson" :disabled="blocked||!catalog?.kcs.length">新增课时</button>
  </details>
  <details><summary>前置能力关系 · {{catalog?.relations.length}}</summary><div v-for="(r,index) in catalog?.relations" class="content-editor-row"><label>需要先会<select v-model="r.from" :disabled="blocked"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><label>然后学习<select v-model="r.to" :disabled="blocked"><option v-for="k in catalog?.kcs" :value="k.id">{{k.name}}</option></select></label><button @click="catalog!.relations.splice(Number(index),1)" :disabled="blocked">移除此关系</button></div><button @click="addRelation" :disabled="blocked||(catalog?.kcs.length||0)<2">新增前置关系</button></details>
  <p v-if="error" class="warning" role="alert">{{error}}</p><button class="primary" @click="save" :disabled="blocked">保存并退回待审核</button><button @click="emit('cancel')" :disabled="blocked">取消</button>
</section>
</template>
<style scoped>
.content-authoring details{margin:24px 0}.content-authoring textarea{min-height:80px;resize:vertical}.content-authoring select[multiple]{min-height:100px}.content-authoring input[readonly]{background:#f2f4ef}.content-authoring .content-editor-row{padding:18px 0}
</style>
