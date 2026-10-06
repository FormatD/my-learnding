#!/usr/bin/env python3
"""Create a page-traceable, pending OCR source for the existing local Builder API."""
import argparse,hashlib,json,os,subprocess
from pathlib import Path

def main():
 p=argparse.ArgumentParser();p.add_argument('pdf',type=Path);p.add_argument('--first',type=int,required=True);p.add_argument('--last',type=int,required=True);p.add_argument('--page-offset',type=int,default=0);p.add_argument('--title',required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 if a.first<1 or a.last<a.first or a.last-a.first>=50:p.error('Choose 1–50 PDF pages per unit')
 os.umask(0o077);a.output.mkdir(parents=True,exist_ok=True);root=Path(__file__).resolve().parents[1];ocr=a.output/'pages-ocr.json'
 subprocess.run(['swift','-module-cache-path',str(root/'.local/swift-cache'),str(root/'scripts/textbook_ocr.swift'),str(a.pdf.resolve()),str(a.first),str(a.last),str(ocr)],check=True)
 pages=json.loads(ocr.read_text());digest=hashlib.sha256(a.pdf.read_bytes()).hexdigest()
 lines=[f"{a.title}；PDF第{x['pdfPage']}页，书内第{x['pdfPage']-a.page_offset}页；未校对OCR："+' '.join(x['text'].splitlines()) for x in pages]
 if not any(x['text'].strip() for x in pages):raise ValueError('No recognizable text; manual page review required')
 source={'title':a.title+'·OCR待核对','text':'\n'.join(lines),'allowExternalAI':False,'usageScope':'FamilyOnly'}
 manifest={'originalPdf':str(a.pdf.resolve()),'pdfSha256':digest,'pdfPages':[a.first,a.last],'printedPageOffset':a.page_offset,'ocr':'macOS Vision','reviewStatus':'Pending','sourceHash':hashlib.sha256(source['text'].encode()).hexdigest()}
 for name,data in [('source-input.json',source),('manifest.json',manifest)]:
  target=a.output/name;target.write_text(json.dumps(data,ensure_ascii=False,indent=2));target.chmod(0o600)
 folder=a.output/'source-pages';folder.mkdir(exist_ok=True)
 for x,line in zip(pages,lines):
  item=source|{'title':f"{a.title}·PDF第{x['pdfPage']}页·OCR待核对",'text':line};target=folder/f"pdf-{x['pdfPage']:03}.json";target.write_text(json.dumps(item,ensure_ascii=False,indent=2));target.chmod(0o600)
 print('Prepared',len(pages),'pages. OCR symbols/layout need review; sources are private. Batch pages before model calls.')
if __name__=='__main__':main()
