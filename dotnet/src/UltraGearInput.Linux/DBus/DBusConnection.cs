using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Sockets;
using System.Text;

namespace UltraGearInput.Linux.DBus;

internal interface IDBusSender
{
    Task SendAsync(DBusMessage message);
}

internal interface IDBusHandler
{
    Task HandleAsync(DBusMessage message);
}

internal sealed class DBusConnection : IDBusSender, IAsyncDisposable
{
    public const string BusName = "org.freedesktop.DBus";
    public const string BusPath = "/org/freedesktop/DBus";

    private const int ReadChunk = 64 * 1024;

    private readonly Stream stream;
    private readonly SemaphoreSlim writeLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<DBusMessage>> pending = new();
    private int serial;

    private DBusConnection(Stream stream)
    {
        this.stream = stream;
    }

    public string UniqueName { get; private set; } = "";

    public static async Task<DBusConnection> ConnectAsync(DBusEndpoint endpoint, uint userId, CancellationToken cancellationToken)
    {
        var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        try
        {
            await socket.ConnectAsync(new UnixDomainSocketEndPoint(endpoint.SocketName), cancellationToken).ConfigureAwait(false);
            var connection = new DBusConnection(new NetworkStream(socket, ownsSocket: true));
            await connection.AuthenticateAsync(userId, cancellationToken).ConfigureAwait(false);
            return connection;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public static DBusConnection FromStream(Stream stream) => new(stream);

    public async Task AuthenticateAsync(uint userId, CancellationToken cancellationToken)
    {
        var identity = Convert.ToHexStringLower(Encoding.ASCII.GetBytes(userId.ToString(CultureInfo.InvariantCulture)));
        await stream.WriteAsync(Encoding.ASCII.GetBytes($"\0AUTH EXTERNAL {identity}\r\n"), cancellationToken).ConfigureAwait(false);
        var response = await ReadLineAsync(cancellationToken).ConfigureAwait(false);
        if (!response.StartsWith("OK ", StringComparison.Ordinal))
        {
            throw new DBusException(DBusErrors.Failed, $"D-Bus authentication failed: {response}");
        }
        await stream.WriteAsync("BEGIN\r\n"u8.ToArray(), cancellationToken).ConfigureAwait(false);
    }

    public Task RunAsync(IDBusHandler handler, CancellationToken cancellationToken) =>
        Task.Run(() => ReadLoopAsync(handler, cancellationToken), cancellationToken);

    public async Task HelloAsync(CancellationToken cancellationToken)
    {
        var reply = await CallAsync(DBusMessage.MethodCall(BusName, BusPath, BusName, "Hello"), cancellationToken).ConfigureAwait(false);
        UniqueName = (string)reply.Body[0];
    }

    public async Task<DBusMessage> CallAsync(DBusMessage call, CancellationToken cancellationToken)
    {
        var id = NextSerial();
        var completion = new TaskCompletionSource<DBusMessage>(TaskCreationOptions.RunContinuationsAsynchronously);
        pending[id] = completion;
        using var registration = cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
        try
        {
            await WriteAsync(call.Encode(id)).ConfigureAwait(false);
            var reply = await completion.Task.ConfigureAwait(false);
            return reply.Type == DBusMessageType.Error
                ? throw new DBusException(reply.ErrorName ?? DBusErrors.Failed, (reply.Body.Count > 0 ? reply.Body[0] as string : null) ?? "")
                : reply;
        }
        finally
        {
            pending.TryRemove(id, out _);
        }
    }

    public Task SendAsync(DBusMessage message) => WriteAsync(message.Encode(NextSerial()));

    public async ValueTask DisposeAsync()
    {
        await stream.DisposeAsync().ConfigureAwait(false);
        writeLock.Dispose();
        foreach (var completion in pending.Values)
        {
            completion.TrySetCanceled();
        }
    }

    private uint NextSerial() => (uint)Interlocked.Increment(ref serial);

    private async Task WriteAsync(byte[] frame)
    {
        await writeLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await stream.WriteAsync(frame).ConfigureAwait(false);
            await stream.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            writeLock.Release();
        }
    }

    private async Task ReadLoopAsync(IDBusHandler handler, CancellationToken cancellationToken)
    {
        var buffer = new byte[ReadChunk];
        var filled = 0;
        while (!cancellationToken.IsCancellationRequested)
        {
            if (filled == buffer.Length)
            {
                Array.Resize(ref buffer, buffer.Length * 2);
            }
            var read = await stream.ReadAsync(buffer.AsMemory(filled), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return;
            }
            filled += read;
            var consumed = 0;
            while (DBusMessage.FrameLength(buffer.AsSpan(consumed, filled - consumed)) is { } length && filled - consumed >= length)
            {
                var message = DBusMessage.Decode(buffer.AsMemory(consumed, length).ToArray());
                consumed += length;
                await DispatchAsync(message, handler).ConfigureAwait(false);
            }
            if (consumed > 0)
            {
                Buffer.BlockCopy(buffer, consumed, buffer, 0, filled - consumed);
                filled -= consumed;
            }
        }
    }

    private async Task DispatchAsync(DBusMessage message, IDBusHandler handler)
    {
        if (message.Type is DBusMessageType.MethodReturn or DBusMessageType.Error
            && message.ReplySerial is { } replySerial
            && pending.TryGetValue(replySerial, out var completion))
        {
            completion.TrySetResult(message);
            return;
        }
        try
        {
            await handler.HandleAsync(message).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            if (message.ExpectsReply)
            {
                await SendAsync(message.ErrorReply(DBusErrors.Failed, exception.Message)).ConfigureAwait(false);
            }
            else
            {
                await Console.Error.WriteLineAsync($"Ignored D-Bus {message.Type} {message.Member}: {exception.Message}").ConfigureAwait(false);
            }
        }
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        var line = new List<byte>();
        var single = new byte[1];
        while (line.Count < 4096)
        {
            if (await stream.ReadAsync(single, cancellationToken).ConfigureAwait(false) == 0)
            {
                throw new DBusException(DBusErrors.Failed, "D-Bus closed the connection during authentication.");
            }
            if (single[0] == '\n' && line.Count > 0 && line[^1] == '\r')
            {
                return Encoding.ASCII.GetString([.. line.Take(line.Count - 1)]);
            }
            line.Add(single[0]);
        }
        throw new DBusException(DBusErrors.Failed, "D-Bus authentication line is too long.");
    }
}

internal static class DBusBus
{
    public const uint DoNotQueue = 4;
    public const uint PrimaryOwner = 1;

    public static async Task<bool> RequestNameAsync(this DBusConnection connection, string name, CancellationToken cancellationToken)
    {
        var reply = await connection.CallAsync(
            DBusMessage.MethodCall(DBusConnection.BusName, DBusConnection.BusPath, DBusConnection.BusName, "RequestName", "su", name, DoNotQueue),
            cancellationToken).ConfigureAwait(false);
        return (uint)reply.Body[0] == PrimaryOwner;
    }

    public static async Task<bool> NameHasOwnerAsync(this DBusConnection connection, string name, CancellationToken cancellationToken)
    {
        var reply = await connection.CallAsync(
            DBusMessage.MethodCall(DBusConnection.BusName, DBusConnection.BusPath, DBusConnection.BusName, "NameHasOwner", "s", name),
            cancellationToken).ConfigureAwait(false);
        return (bool)reply.Body[0];
    }

    public static Task AddMatchAsync(this DBusConnection connection, string rule, CancellationToken cancellationToken) =>
        connection.CallAsync(
            DBusMessage.MethodCall(DBusConnection.BusName, DBusConnection.BusPath, DBusConnection.BusName, "AddMatch", "s", rule),
            cancellationToken);

    public static string NameOwnerChangedRule(string name) =>
        $"type='signal',sender='{DBusConnection.BusName}',interface='{DBusConnection.BusName}',member='NameOwnerChanged',arg0='{name}'";
}
