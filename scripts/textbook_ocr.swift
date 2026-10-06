// Offline OCR of scanned textbook pages. Original PDF is never modified.
import Foundation
import PDFKit
import Vision
import AppKit
struct Line: Codable { let text: String; let confidence: Float; let x: CGFloat; let y: CGFloat; let width: CGFloat; let height: CGFloat }
struct Page: Codable { let pdfPage: Int; let text: String; let lines: [Line]; let needsReview: Bool }
let args=CommandLine.arguments
if args.count != 5 { fputs("Usage: textbook_ocr.swift PDF FIRST LAST OUTPUT_JSON\n",stderr);exit(2) }
guard let doc=PDFDocument(url:URL(fileURLWithPath:args[1])),let first=Int(args[2]),let last=Int(args[3]),first>=1,last>=first,last<=doc.pageCount else { fputs("Invalid PDF/page range\n",stderr);exit(2) }
var pages=[Page]()
for index in first...last {
 try autoreleasepool {
  guard let page=doc.page(at:index-1) else { throw NSError(domain:"OCR",code:1) }
  let rect=page.bounds(for:.mediaBox),scale:CGFloat=2
  let image=page.thumbnail(of:NSSize(width:rect.width*scale,height:rect.height*scale),for:.mediaBox)
  guard let cg=image.cgImage(forProposedRect:nil,context:nil,hints:nil) else { throw NSError(domain:"OCR",code:2) }
  let request=VNRecognizeTextRequest();request.recognitionLevel = .accurate;request.recognitionLanguages=["zh-Hans","en-US"];request.usesLanguageCorrection=false
  try VNImageRequestHandler(cgImage:cg).perform([request])
  let lines=(request.results ?? []).compactMap { result -> Line? in
   guard let candidate=result.topCandidates(1).first else { return nil };let r=result.boundingBox
   return Line(text:candidate.string,confidence:candidate.confidence,x:r.minX,y:r.minY,width:r.width,height:r.height)
  }
  pages.append(Page(pdfPage:index,text:lines.map{$0.text}.joined(separator:"\n"),lines:lines,needsReview:true))
  fputs("OCR page \(index)/\(last), \(lines.count) lines; mathematical symbols/reading order require review\n",stderr)
 }
}
let encoder=JSONEncoder();encoder.outputFormatting=[.prettyPrinted,.sortedKeys];try encoder.encode(pages).write(to:URL(fileURLWithPath:args[4]),options:.atomic)
