using System.ComponentModel;
using System.Net;
using System.Net.Sockets;
using DonutBoost.Models;
using MonoTorrent.Connections.Peer;
using ReusableTasks;

namespace DonutBoost.Engine;

internal sealed class TorrentPeerConnection : IPeerConnection
{
    private readonly Func<IReadOnlyList<NetworkAdapterInfo>> _getAdapters;
    private readonly CancellationToken _sessionToken;
    private readonly object _connectionLock = new();
    private NetworkAdapterInfo? _adapter;
    private Socket? _socket;
    private bool _disposed;

    public TorrentPeerConnection(Uri uri, Func<IReadOnlyList<NetworkAdapterInfo>> getAdapters,
        CancellationToken sessionToken)
    {
        Uri = uri;
        EndPoint = new IPEndPoint(IPAddress.Parse(uri.DnsSafeHost), uri.Port);
        AddressBytes = EndPoint.Address.GetAddressBytes();
        _getAdapters = getAdapters;
        _sessionToken = sessionToken;
    }

    public Uri Uri { get; }
    public IPEndPoint EndPoint { get; }
    public ReadOnlyMemory<byte> AddressBytes { get; }
    public bool IsIncoming => false;
    public bool CanReconnect => true;
    public bool Disposed { get { lock (_connectionLock) return _disposed; } }

    public async ReusableTask ConnectAsync()
    {
        bool isIPv6 = EndPoint.AddressFamily == AddressFamily.InterNetworkV6;
        foreach (var adapter in _getAdapters())
        {
            _sessionToken.ThrowIfCancellationRequested();
            var address = isIPv6 ? adapter.LocalIPv6 : adapter.LocalIp;
            int index = isIPv6 ? adapter.IPv6InterfaceIndex : adapter.InterfaceIndex;
            if (address == null || index <= 0) continue;

            Socket? socket = null;
            try
            {
                socket = NetworkBondingManager.CreateBoundSocket(address, index);
                lock (_connectionLock)
                {
                    if (_disposed) throw new ObjectDisposedException(nameof(TorrentPeerConnection));
                    if (!adapter.IsEnabled || !adapter.IsOnline)
                    {
                        socket.Dispose();
                        continue;
                    }
                    _socket = socket;
                    _adapter = adapter;
                    adapter.PropertyChanged += OnAdapterChanged;
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(_sessionToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(3));
                await socket.ConnectAsync(EndPoint, timeout.Token);
                return;
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException or ObjectDisposedException)
            {
                lock (_connectionLock)
                {
                    if (_socket == socket) CloseSocket();
                    else socket?.Dispose();
                    if (_disposed) throw new ObjectDisposedException(nameof(TorrentPeerConnection));
                }
                _sessionToken.ThrowIfCancellationRequested();
            }
        }
        throw new SocketException((int)SocketError.NetworkUnreachable);
    }

    public async ReusableTask<int> ReceiveAsync(Memory<byte> buffer)
    {
        Socket socket;
        NetworkAdapterInfo adapter;
        lock (_connectionLock)
        {
            socket = _socket ?? throw new SocketException((int)SocketError.NotConnected);
            adapter = _adapter!;
        }
        int count = await adapter.TransferDataLimit.ReadAsync(buffer,
            (destination, token) => socket.ReceiveAsync(destination, SocketFlags.None, token), _sessionToken);
        if (count < 0) throw new SocketException((int)SocketError.ConnectionAborted);
        if (count > 0)
            adapter.AddDownloadedBytes(count);
        return count;
    }

    public async ReusableTask<int> SendAsync(Memory<byte> buffer)
    {
        Socket socket;
        lock (_connectionLock)
            socket = _socket ?? throw new SocketException((int)SocketError.NotConnected);
        return await socket.SendAsync(buffer, SocketFlags.None, _sessionToken);
    }

    private void OnAdapterChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName is not (nameof(NetworkAdapterInfo.IsEnabled) or nameof(NetworkAdapterInfo.IsOnline)))
            return;
        lock (_connectionLock)
        {
            if (sender == _adapter && _adapter is { } adapter && (!adapter.IsEnabled || !adapter.IsOnline))
                CloseSocket();
        }
    }

    private void CloseSocket()
    {
        if (_adapter != null) _adapter.PropertyChanged -= OnAdapterChanged;
        _socket?.Dispose();
        _socket = null;
        _adapter = null;
    }

    public void Dispose()
    {
        lock (_connectionLock)
        {
            _disposed = true;
            CloseSocket();
        }
    }
}
