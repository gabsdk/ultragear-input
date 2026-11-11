using System.Net.Sockets;
using System.Text;
using UltraGearInput.Linux.DBus;

namespace UltraGearInput.Tests.Linux;

public sealed class DBusConnectionTests
{
    [Theory]
    [InlineData("unix:path=/run/user/1000/bus", "/run/user/1000/bus", false)]
    [InlineData("unix:abstract=/tmp/dbus-abc,guid=0123", "/tmp/dbus-abc", true)]
    [InlineData("tcp:host=localhost,port=1;unix:path=/x", "/x", false)]
    [InlineData("unix:path=/tmp/with%20space", "/tmp/with space", false)]
    public void ParsesUnixAddresses(string address, string path, bool isAbstract) =>
        Assert.Equal(new DBusEndpoint(path, isAbstract), DBusAddress.Parse(address));

    [Theory]
    [InlineData("tcp:host=localhost,port=1")]
    [InlineData("unix:tmpdir=/tmp")]
    [InlineData("")]
    public void IgnoresUnsupportedAddresses(string address) => Assert.Null(DBusAddress.Parse(address));

    [Fact]
    public void FallsBackToTheRuntimeDirectory()
    {
        using var directory = new TemporaryDirectory();
        var endpoint = DBusAddress.Session(name => name == DBusAddress.RuntimeDirectoryVariable ? directory.Path : null);
        Assert.Equal(new DBusEndpoint(directory.Combine("bus"), false), endpoint);
        Assert.Null(DBusAddress.Session(_ => null));
    }

    [Fact]
    public void AbstractSocketNamesStartWithNul() =>
        Assert.Equal("\0name", new DBusEndpoint("name", true).SocketName);

    [Fact]
    public async Task AuthenticatesCallsAndDispatchesAgainstAFakeBus()
    {
        using var directory = new TemporaryDirectory();
        var socketPath = Path.Combine(directory.Path, "bus");
        using var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
        listener.Bind(new UnixDomainSocketEndPoint(socketPath));
        listener.Listen(1);
        var cancellation = TestContext.Current.CancellationToken;

        var server = Task.Run(async () =>
        {
            using var peer = await listener.AcceptAsync(cancellation);
            await using var stream = new NetworkStream(peer, ownsSocket: false);
            var greeting = await ReadUntilAsync(stream, "\r\n", cancellation);
            Assert.Equal("\0AUTH EXTERNAL 31303030\r\n", greeting);
            await stream.WriteAsync("OK 0123456789abcdef\r\n"u8.ToArray(), cancellation);
            Assert.Equal("BEGIN\r\n", await ReadUntilAsync(stream, "\r\n", cancellation));

            var hello = await ReadMessageAsync(stream, cancellation);
            Assert.Equal("Hello", hello.Member);
            await stream.WriteAsync((hello with { Sender = "org.freedesktop.DBus" }).Reply("s", ":1.42").Encode(1), cancellation);

            await stream.WriteAsync((DBusMessage.MethodCall(":1.42", "/x", "a.b", "Ping") with { Sender = ":1.7" }).Encode(2), cancellation);
            var reply = await ReadMessageAsync(stream, cancellation);
            Assert.Equal((DBusMessageType.MethodReturn, 2u, ":1.7"), (reply.Type, reply.ReplySerial!.Value, reply.Destination!));
        }, cancellation);

        await using var connection = await DBusConnection.ConnectAsync(new DBusEndpoint(socketPath, false), 1000, cancellation);
        var handler = new ReplyingHandler(connection);
        _ = connection.RunAsync(handler, cancellation);
        await connection.HelloAsync(cancellation);
        Assert.Equal(":1.42", connection.UniqueName);
        await server.WaitAsync(TimeSpan.FromSeconds(10), cancellation);
        Assert.Equal("Ping", Assert.Single(handler.Calls).Member);
    }

    [Fact]
    public async Task RejectedAuthenticationFails()
    {
        var input = new MemoryStream(Encoding.ASCII.GetBytes("REJECTED EXTERNAL\r\n"));
        await using var connection = DBusConnection.FromStream(new DuplexStream(input, new MemoryStream()));
        await Assert.ThrowsAsync<DBusException>(() => connection.AuthenticateAsync(1000, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task ErrorRepliesBecomeExceptions()
    {
        var output = new MemoryStream();
        var input = new BlockingStream();
        await using var connection = DBusConnection.FromStream(new DuplexStream(input, output));
        var cancellation = TestContext.Current.CancellationToken;
        _ = connection.RunAsync(new ReplyingHandler(connection), cancellation);
        var call = connection.CallAsync(DBusMessage.MethodCall("a.b", "/", "a.b", "Fail"), cancellation);
        input.Push(new DBusMessage(DBusMessageType.Error) { ReplySerial = 1, ErrorName = "a.b.Nope", Signature = "s", Body = ["no"] }.Encode(9));
        var exception = await Assert.ThrowsAsync<DBusException>(() => call);
        Assert.Equal(("a.b.Nope", "no"), (exception.Name, exception.Message));
    }

    private static async Task<string> ReadUntilAsync(Stream stream, string terminator, CancellationToken cancellationToken)
    {
        var builder = new StringBuilder();
        var single = new byte[1];
        while (!builder.ToString().EndsWith(terminator, StringComparison.Ordinal))
        {
            await stream.ReadExactlyAsync(single, cancellationToken);
            builder.Append((char)single[0]);
        }
        return builder.ToString();
    }

    private static async Task<DBusMessage> ReadMessageAsync(Stream stream, CancellationToken cancellationToken)
    {
        var header = new byte[DBusMessage.FixedHeaderLength];
        await stream.ReadExactlyAsync(header, cancellationToken);
        var frame = new byte[DBusMessage.FrameLength(header)!.Value];
        header.CopyTo(frame, 0);
        await stream.ReadExactlyAsync(frame.AsMemory(header.Length), cancellationToken);
        return DBusMessage.Decode(frame);
    }

    private sealed class ReplyingHandler(IDBusSender sender) : IDBusHandler
    {
        public List<DBusMessage> Calls { get; } = [];

        public Task HandleAsync(DBusMessage message)
        {
            Calls.Add(message);
            return message.ExpectsReply ? sender.SendAsync(message.Reply()) : Task.CompletedTask;
        }
    }
}
