using System.Runtime.InteropServices;

namespace SafeSeal.Core;

public sealed class SecureBufferScope : IDisposable
{
    private readonly GCHandle _handle;
    private bool _disposed;

    public SecureBufferScope(byte[] buffer)
    {
        Buffer = buffer ?? throw new ArgumentNullException(nameof(buffer));
        _handle = GCHandle.Alloc(Buffer, GCHandleType.Pinned);
    }

    public byte[] Buffer { get; }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        Array.Clear(Buffer, 0, Buffer.Length);
        if (_handle.IsAllocated)
        {
            _handle.Free();
        }

        _disposed = true;
    }
}
