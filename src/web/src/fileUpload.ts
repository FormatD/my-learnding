import {apiWithVersion,captureFamilyVersion,uploadBytesWithVersion,completeUploadWithVersion} from './api';
type Receipt=Record<string,any>;
export type FileUploadAttempt={fingerprint:string,run:(onVersion?:(version:string)=>void)=>Promise<Receipt>};
export async function prepareFileUpload(file:File,purpose='Attachment',expectedVersion=captureFamilyVersion()):Promise<FileUploadAttempt>{
 if(!file.size||file.size>10_000_000)throw new Error('文件须在10 MB以内');
 const mime=(file.type||(/\.pdf$/i.test(file.name)?'application/pdf':/\.png$/i.test(file.name)?'image/png':/\.jpe?g$/i.test(file.name)?'image/jpeg':/\.wav$/i.test(file.name)?'audio/wav':/\.mp3$/i.test(file.name)?'audio/mpeg':'')).replace('audio/x-wav','audio/wav').replace('audio/mp3','audio/mpeg');
 const bytes=await file.arrayBuffer(),digest=new Uint8Array(await crypto.subtle.digest('SHA-256',bytes));const hash=Array.from(digest,b=>b.toString(16).padStart(2,'0')).join('');
 const fingerprint=JSON.stringify([file.name,mime,file.size,hash,purpose]),issueKey=crypto.randomUUID(),bytesKey=crypto.randomUUID(),completeKey=crypto.randomUUID();let ticket:Receipt|undefined,stored:Receipt|undefined,complete:Receipt|undefined;
 function version(receipt:Receipt){if(!/^[1-9]\d*$/.test(String(receipt.familyVersion)))throw new Error('无法确认上传结果版本，请刷新核对');return '"'+receipt.familyVersion+'"';}
 return {fingerprint,async run(onVersion){
  if(!ticket){const issued=await apiWithVersion('/files/upload-tickets',{name:file.name,mimeType:mime,size:file.size,sha256:hash,purpose},'POST',issueKey,expectedVersion);ticket=issued.data as Receipt;onVersion?.(version(ticket));if(!issued.version)await apiWithVersion('/me');}
  if(!stored){const stage=await uploadBytesWithVersion('/files/uploads/'+ticket.fileId,bytes,bytesKey,ticket.credential,version(ticket));stored=stage.data as Receipt;onVersion?.(version(stored));if(!stage.version)await apiWithVersion('/me');}
  if(!complete){const final=await completeUploadWithVersion('/files/'+ticket.fileId+':complete',completeKey,ticket.credential,version(stored));complete=final.data as Receipt;onVersion?.(version(complete));if(!final.version)await apiWithVersion('/me');}
  if(complete.id!==ticket.fileId||complete.hash!==hash||complete.size!==file.size||complete.mimeType!==mime)throw new Error('服务器文件结果与原文件不一致，请重新核对');
  return complete;
 }};
}
