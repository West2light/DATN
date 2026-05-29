using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// UDP LAN discovery.
/// Host broadcasts its game port every second; client listens and fires OnHostFound(ip).
/// </summary>
public class LanDiscovery : MonoBehaviour
{
    public const int    BroadcastPort = 47776;
    private const string Signature   = "TANK_MAPF_HOST:";

    public event Action<string> OnHostFound;

    private Thread    _thread;
    private UdpClient _listenUdp;
    private bool      _running;
    private string    _pendingIP;
    private bool      _pendingNotify;
    private string    _broadcastMsg;

    // ── Host side ─────────────────────────────────────────────────────────────

    public void StartBroadcasting(ushort gamePort)
    {
        _broadcastMsg = $"{Signature}{gamePort}";
        InvokeRepeating(nameof(SendBroadcast), 0f, 1f);
    }

    private void SendBroadcast()
    {
        try
        {
            using var udp = new UdpClient { EnableBroadcast = true };
            byte[] data = Encoding.UTF8.GetBytes(_broadcastMsg);
            udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
        }
        catch (Exception e) { Debug.LogWarning("[LanDiscovery] Broadcast: " + e.Message); }
    }

    public void StopBroadcasting() => CancelInvoke(nameof(SendBroadcast));

    // ── Client side ───────────────────────────────────────────────────────────

    public void StartListening()
    {
        _running = true;
        _thread  = new Thread(ListenLoop) { IsBackground = true };
        _thread.Start();
    }

    private void ListenLoop()
    {
        try
        {
            _listenUdp = new UdpClient(BroadcastPort);
            while (_running)
            {
                IPEndPoint ep   = new IPEndPoint(IPAddress.Any, 0);
                byte[]     data = _listenUdp.Receive(ref ep);
                string     msg  = Encoding.UTF8.GetString(data);
                if (msg.StartsWith(Signature))
                {
                    _pendingIP     = ep.Address.ToString();
                    _pendingNotify = true;
                    _running       = false;
                }
            }
        }
        catch (Exception e)
        {
            if (_running) Debug.LogWarning("[LanDiscovery] Listen: " + e.Message);
        }
    }

    private void Update()
    {
        if (!_pendingNotify) return;
        _pendingNotify = false;
        OnHostFound?.Invoke(_pendingIP);
    }

    public void StopListening()
    {
        _running = false;
        _listenUdp?.Close();
    }

    public void Stop()
    {
        StopBroadcasting();
        StopListening();
    }

    private void OnDestroy() => Stop();
}
