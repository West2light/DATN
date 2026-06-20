using System;
using System.Threading.Tasks;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.Networking.Transport.Relay;
using Unity.Services.Authentication;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using UnityEngine;

public static class RelayManager
{
    public static bool IsInitialized { get; private set; }

    public static async Task<bool> InitAsync()
    {
        if (IsInitialized) return true;
        try
        {
            await UnityServices.InitializeAsync();
            if (!AuthenticationService.Instance.IsSignedIn)
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
            IsInitialized = true;
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[Relay] Init failed: {e.Message}");
            return false;
        }
    }

    public static async Task<(Allocation allocation, string joinCode)> CreateRoomAsync(int maxConnections)
    {
        Allocation allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        string joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        return (allocation, joinCode);
    }

    public static async Task<JoinAllocation> JoinRoomAsync(string joinCode)
    {
        return await RelayService.Instance.JoinAllocationAsync(joinCode);
    }

    public static void ApplyHostToTransport(Allocation allocation)
    {
        var transport = NetworkManager.Singleton?.GetComponent<UnityTransport>();
        if (transport == null) return;
        var data = new RelayServerData(
            allocation.RelayServer.IpV4,
            (ushort)allocation.RelayServer.Port,
            allocation.AllocationIdBytes,
            allocation.ConnectionData,
            allocation.ConnectionData,
            allocation.Key,
            false);
        transport.SetRelayServerData(data);
    }

    public static void ApplyClientToTransport(JoinAllocation joinAllocation)
    {
        var transport = NetworkManager.Singleton?.GetComponent<UnityTransport>();
        if (transport == null) return;
        var data = new RelayServerData(
            joinAllocation.RelayServer.IpV4,
            (ushort)joinAllocation.RelayServer.Port,
            joinAllocation.AllocationIdBytes,
            joinAllocation.ConnectionData,
            joinAllocation.HostConnectionData,
            joinAllocation.Key,
            false);
        transport.SetRelayServerData(data);
    }

    // Returns true if the string looks like a relay join code (6 alphanumeric chars)
    public static bool IsJoinCode(string input)
    {
        if (string.IsNullOrEmpty(input) || input.Length != 6) return false;
        foreach (char c in input)
            if (!char.IsLetterOrDigit(c)) return false;
        return true;
    }
}
