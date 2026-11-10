using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

// Synthetic fixture only: no host ports, Docker networks or production configuration.
// PostgreSQL backend messages are framed so only a complete COMMIT acknowledgement is dropped.
sealed class CommitReplyLossProxy : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentBag<Task> relays = new();
    private readonly Task accepting;
    private int armed;
    public TaskCompletionSource ServerCommitObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Port { get; }
    public CommitReplyLossProxy()
    {
        listener.Start(); Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        accepting = AcceptAsync();
    }
    public void Arm() => Interlocked.Exchange(ref armed, 1);
    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
                relays.Add(RelayAsync(await listener.AcceptTcpClientAsync(stopping.Token)));
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
    }
    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var server = new TcpClient())
        using (var cancel = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            try
            {
                await server.ConnectAsync(IPAddress.Loopback, 5432, cancel.Token);
                var outgoing = client.GetStream().CopyToAsync(server.GetStream(), cancel.Token);
                var incoming = BackendAsync(server.GetStream(), client.GetStream(), cancel.Token);
                await Task.WhenAny(outgoing, incoming);
                cancel.Cancel(); client.Close(); server.Close();
                try { await Task.WhenAll(outgoing, incoming); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
            }
            catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException or SocketException) { }
        }
    }
    private async Task BackendAsync(Stream source, Stream destination, CancellationToken ct)
    {
        var header = new byte[5];
        while (true)
        {
            await source.ReadExactlyAsync(header, ct);
            var size = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(1)) - 4;
            if (size < 0 || size > 8 * 1024 * 1024) throw new IOException("Invalid synthetic PostgreSQL frame.");
            var payload = new byte[size];
            await source.ReadExactlyAsync(payload, ct);
            if (header[0] == (byte)'C' && payload.AsSpan().SequenceEqual("COMMIT\0"u8) && Interlocked.Exchange(ref armed, 0) == 1)
            {
                // PostgreSQL has completed COMMIT; the client receives neither CommandComplete nor ReadyForQuery.
                ServerCommitObserved.TrySetResult();
                return;
            }
            await destination.WriteAsync(header, ct);
            await destination.WriteAsync(payload, ct);
            await destination.FlushAsync(ct);
        }
    }
    public async ValueTask DisposeAsync()
    {
        stopping.Cancel(); listener.Stop();
        await accepting.WaitAsync(TimeSpan.FromSeconds(5));
        await Task.WhenAll(relays).WaitAsync(TimeSpan.FromSeconds(5));
        stopping.Dispose();
    }
}