type Item=Record<string,any>;
export type ContentImport={title:string,catalog:Item};
const fail=()=>{throw new Error('题包结构无法读取，请使用包含 title 和 catalog 的本地 JSON 题包。');};
const object=(value:any):value is Item=>value!==null&&typeof value==='object'&&!Array.isArray(value);
const strings=(value:any)=>Array.isArray(value)&&value.every(x=>typeof x==='string');
const uuid=(value:any)=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
export function parseContentImport(text:string):ContentImport {
 let input:any;try{input=JSON.parse(text);}catch{fail();}
 if(!object(input)||typeof input.title!=='string'||!input.title.trim()||input.title.length>200||!object(input.catalog))fail();
 const c=input.catalog;
 for(const field of ['kcs','questions','resources','lessons','relations'])if(!Array.isArray(c[field])||c[field].length>1000||!c[field].every(object))fail();
 for(const field of ['textbooks','units','courses','mappingCoverage'])if(c[field]!=null&&(!Array.isArray(c[field])||c[field].length>1000||!c[field].every(object)))fail();
 for(const k of c.kcs){if(!uuid(k.id)||!uuid(k.revisionId)||!['code','name','behavior','boundary','type'].every(f=>typeof k[f]==='string')||(k.requiredCoverage!=null&&!strings(k.requiredCoverage)))fail();}
 for(const q of c.questions){
  if(!uuid(q.id)||!uuid(q.revisionId)||!['stem','answer','explanation','type','difficulty','policy'].every(f=>typeof q[f]==='string')||!Array.isArray(q.mappings)||q.mappings.length>100)fail();
  for(const m of q.mappings)if(!object(m)||!uuid(m.kcId)||typeof m.role!=='string'||typeof m.mode!=='string'||typeof m.share!=='number'||!Number.isFinite(m.share)||(m.step!=null&&typeof m.step!=='string'))fail();
 }
 for(const r of c.resources)if(!uuid(r.id)||typeof r.title!=='string'||typeof r.paperReference!=='string'||!Number.isInteger(r.minutes)||!strings(r.kcIds)||!r.kcIds.every(uuid))fail();
 for(const l of c.lessons)if(!uuid(l.id)||typeof l.title!=='string'||!Number.isInteger(l.sequence)||!strings(l.kcIds)||!l.kcIds.every(uuid)||(l.sourceRefs!=null&&(!strings(l.sourceRefs)||!l.sourceRefs.every(uuid))))fail();
 for(const r of c.relations)if(!uuid(r.from)||!uuid(r.to)||typeof r.type!=='string')fail();
 return {title:input.title.trim(),catalog:c};
}
