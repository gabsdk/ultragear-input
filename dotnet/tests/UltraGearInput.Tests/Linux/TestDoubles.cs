using System.Threading.Channels;
using UltraGearInput.Core;
using UltraGearInput.Linux.DBus;
using UltraGearInput.Linux.Tray;

namespace UltraGearInput.Tests.Linux;

internal sealed class RecordingSender : IDBusSender
{
    public List<DBusMessage> Sent { get; } = [];

    public Task SendAsync(DBusMessage message)
    {
        Sent.Add(message);
        return Task.CompletedTask;
    }
}

internal sealed class FakeBackend : ITrayBackend
{
    public IReadOnlyList<TrayMenuEntry> Menu { get; set; } = [];

    public List<TrayMenuEntry> Activated { get; } = [];

    public IReadOnlyList<TrayMenuEntry> BuildMenu() => Menu;

    public void Activate(TrayMenuEntry entry) => Activated.Add(entry);
}

internal sealed class DuplexStream(Stream input, Stream output) : Stream
{
    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush() => output.Flush();

    public override int Read(byte[] buffer, int offset, int count) => input.Read(buffer, offset, count);

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        input.ReadAsync(buffer, cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => output.Write(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}

internal sealed class BlockingStream : Stream
{
    private readonly Channel<byte[]> chunks = Channel.CreateUnbounded<byte[]>();
    private byte[] current = [];
    private int offset;

    public void Push(byte[] chunk) => chunks.Writer.TryWrite(chunk);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (offset == current.Length)
        {
            current = await chunks.Reader.ReadAsync(cancellationToken);
            offset = 0;
        }
        var count = Math.Min(buffer.Length, current.Length - offset);
        current.AsMemory(offset, count).CopyTo(buffer);
        offset += count;
        return count;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override void Flush()
    {
    }

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();
}
