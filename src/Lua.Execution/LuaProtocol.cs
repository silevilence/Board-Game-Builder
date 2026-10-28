using System.Buffers.Binary;
using System.Text;
namespace BoardGame.Lua;

// Bounded length-prefixed UTF-8 frames allow multiple requests without unbounded ReadLine buffers.
internal static class LuaProtocol
{
    internal const int RequestLimit=16*1024*1024;
    internal const int ResponseLimit=1024*1024;
    internal static async Task Write(Stream stream,string text,int limit,CancellationToken token=default)
    {
        var size=Encoding.UTF8.GetByteCount(text);
        if(size>limit) throw new InvalidDataException("Lua frame budget exceeded.");
        var header=new byte[4];BinaryPrimitives.WriteInt32LittleEndian(header,size);
        await stream.WriteAsync(header,token).ConfigureAwait(false);
        await stream.WriteAsync(Encoding.UTF8.GetBytes(text),token).ConfigureAwait(false);
        await stream.FlushAsync(token).ConfigureAwait(false);
    }
    internal static async Task<string> Read(Stream stream,int limit,CancellationToken token=default)
    {
        var header=new byte[4];
        await stream.ReadExactlyAsync(header,token).ConfigureAwait(false);
        var size=BinaryPrimitives.ReadInt32LittleEndian(header);
        if(size<0 || size>limit) throw new InvalidDataException("Lua frame budget exceeded.");
        var bytes=new byte[size];
        await stream.ReadExactlyAsync(bytes,token).ConfigureAwait(false);
        return new UTF8Encoding(false,true).GetString(bytes);
    }
}
