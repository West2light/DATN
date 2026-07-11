using System;
using System.Collections.Generic;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using UnityEngine;

/// <summary>
/// UDP LAN discovery.
/// Host broadcasts its game port + LAN IP every second on every physical interface;
/// client listens and fires OnHostFound(ip).
///
/// Payload:  "TANK_MAPF_HOST:&lt;gamePort&gt;:&lt;hostLanIp&gt;"
/// The embedded host IP is preferred over the datagram's source address, so the client
/// dials the interface the host actually listens on even on multi-NIC machines.
/// Old payloads ("TANK_MAPF_HOST:&lt;gamePort&gt;") stay compatible (falls back to sender IP).
/// </summary>
public class LanDiscovery : MonoBehaviour
{
    public const int    BroadcastPort = 47776;
    private const string Signature   = "TANK_MAPF_HOST:";

    // Virtual/tunnel adapters whose IPv4 other LAN machines cannot reach. WSL shows up
    // as "vEthernet (WSL ...)". Matched against NIC Name + Description (lower-cased).
    private static readonly string[] VirtualNicHints =
    {
        "wsl", "hyper-v", "hyperv", "virtual", "vethernet", "vpn",
        "loopback", "vmware", "virtualbox", "docker", "tap", "tunnel"
    };

    public event Action<string> OnHostFound;

    private Thread    _thread;
    private UdpClient _listenUdp;
    private bool      _running;
    private string    _pendingIP;
    private bool      _pendingNotify;
    private ushort    _gamePort;
    private List<KeyValuePair<IPAddress, IPAddress>> _targets;   // local IP → directed broadcast

    // ── Host side ─────────────────────────────────────────────────────────────

    public void StartBroadcasting(ushort gamePort)
    {
        _gamePort = gamePort;
        _targets  = EnumerateBroadcastTargets();

        if (_targets.Count == 0)
            Debug.LogWarning("[LanDiscovery] No physical LAN interface found — falling back to limited broadcast only.");
        else
        {
            var parts = new List<string>();
            foreach (var t in _targets) parts.Add($"{t.Key} → {t.Value}");
            Debug.Log("[LanDiscovery] Broadcasting host presence on: " + string.Join(",  ", parts));
        }

        InvokeRepeating(nameof(SendBroadcast), 0f, 1f);
    }

    private void SendBroadcast()
    {
        if (_targets != null && _targets.Count > 0)
        {
            foreach (var t in _targets)
            {
                IPAddress local = t.Key, directed = t.Value;
                try
                {
                    // Bind to the NIC's own IP so the datagram leaves that interface, not
                    // whichever one Windows picks by metric for 255.255.255.255.
                    using var udp = new UdpClient(new IPEndPoint(local, 0)) { EnableBroadcast = true };
                    byte[] data = Encoding.UTF8.GetBytes($"{Signature}{_gamePort}:{local}");
                    udp.Send(data, data.Length, new IPEndPoint(directed, BroadcastPort));
                    udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
                }
                catch (Exception e) { Debug.LogWarning($"[LanDiscovery] Broadcast via {local}: {e.Message}"); }
            }
        }
        else
        {
            // Fallback: unbound limited broadcast (legacy behavior) for single-NIC/edge cases.
            try
            {
                using var udp = new UdpClient { EnableBroadcast = true };
                byte[] data = Encoding.UTF8.GetBytes($"{Signature}{_gamePort}");
                udp.Send(data, data.Length, new IPEndPoint(IPAddress.Broadcast, BroadcastPort));
            }
            catch (Exception e) { Debug.LogWarning("[LanDiscovery] Broadcast fallback: " + e.Message); }
        }
    }

    public void StopBroadcasting() => CancelInvoke(nameof(SendBroadcast));

    /// Physical-interface unicast IPs paired with their subnet-directed broadcast address.
    private static List<KeyValuePair<IPAddress, IPAddress>> EnumerateBroadcastTargets()
    {
        var list = new List<KeyValuePair<IPAddress, IPAddress>>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up) continue;
                    if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                    string desc = (nic.Name + " " + nic.Description).ToLowerInvariant();
                    bool isVirtual = false;
                    foreach (var hint in VirtualNicHints)
                        if (desc.Contains(hint)) { isVirtual = true; break; }
                    if (isVirtual) continue;

                    foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                    {
                        if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                        if (IPAddress.IsLoopback(ua.Address)) continue;
                        if (ua.Address.ToString().StartsWith("169.254.")) continue;   // APIPA

                        IPAddress mask = ua.IPv4Mask;
                        if (mask == null || mask.Equals(IPAddress.Any))
                            mask = IPAddress.Parse("255.255.255.0");
                        list.Add(new KeyValuePair<IPAddress, IPAddress>(
                            ua.Address, DirectedBroadcast(ua.Address, mask)));
                    }
                }
                catch { /* skip this NIC */ }
            }
        }
        catch (Exception e) { Debug.LogWarning("[LanDiscovery] Enumerate interfaces: " + e.Message); }
        return list;
    }

    private static IPAddress DirectedBroadcast(IPAddress ip, IPAddress mask)
    {
        byte[] a = ip.GetAddressBytes();
        byte[] m = mask.GetAddressBytes();
        var bc = new byte[4];
        for (int i = 0; i < 4; i++) bc[i] = (byte)(a[i] | (~m[i] & 0xFF));
        return new IPAddress(bc);
    }

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
            // ReuseAddress lets a host + client (two builds) share port 47776 on one test
            // machine, and avoids a hard bind failure if the port lingers in TIME_WAIT.
            _listenUdp = new UdpClient();
            _listenUdp.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _listenUdp.Client.Bind(new IPEndPoint(IPAddress.Any, BroadcastPort));

            while (_running)
            {
                IPEndPoint ep   = new IPEndPoint(IPAddress.Any, 0);
                byte[]     data = _listenUdp.Receive(ref ep);
                string     msg  = Encoding.UTF8.GetString(data);
                if (!msg.StartsWith(Signature)) continue;

                // Prefer the host IP embedded in the payload; fall back to the sender's
                // address for legacy hosts that only broadcast the port.
                string hostIp = ep.Address.ToString();
                string[] parts = msg.Substring(Signature.Length).Split(':');
                if (parts.Length >= 2 && !string.IsNullOrWhiteSpace(parts[1]))
                    hostIp = parts[1].Trim();

                Debug.Log($"[LanDiscovery] Host found: {hostIp} (from {ep.Address})");
                _pendingIP     = hostIp;
                _pendingNotify = true;
                _running       = false;
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
