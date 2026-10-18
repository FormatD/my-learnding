declare module 'pdfjs-dist' {
  export const GlobalWorkerOptions: { workerSrc: string };
  export function getDocument(src: any): { promise: Promise<PDFDocumentProxy> };
  interface PDFDocumentProxy {
    getPage(n: number): Promise<PDFPageProxy>;
    numPages: number;
  }
  interface PDFPageProxy {
    getViewport(params: { scale: number }): { width: number; height: number };
    render(params: { canvasContext: CanvasRenderingContext2D; viewport: any }): { promise: Promise<void> };
  }
}
