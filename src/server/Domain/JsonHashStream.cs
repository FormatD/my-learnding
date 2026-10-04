using System.Security.Cryptography;
namespace Learning;
internal sealed class JsonHashStream(IncrementalHash first,IncrementalHash? second=null):Stream
{
    public bool CaptureSecond{get;set;}=true;
    public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
    public override void Write(byte[] buffer,int offset,int count)=>Write(buffer.AsSpan(offset,count));
    public override void Write(ReadOnlySpan<byte> bytes){first.AppendData(bytes);if(CaptureSecond)second?.AppendData(bytes);}
    public override void Flush(){}
    public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long value)=>throw new NotSupportedException();
}
