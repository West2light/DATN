using System;
using System.Text;
using UnityEngine;

[Serializable]
public class JoinApprovalPayload
{
    public string sessionCode;
    public string clientVersion;
    public int variantIndex;

    public static byte[] ToBytes(JoinApprovalPayload payload)
    {
        if (payload == null)
            return Array.Empty<byte>();

        return Encoding.UTF8.GetBytes(JsonUtility.ToJson(payload));
    }

    public static bool TryFromBytes(byte[] bytes, out JoinApprovalPayload payload)
    {
        payload = null;

        if (bytes == null || bytes.Length == 0)
            return false;

        try
        {
            string json = Encoding.UTF8.GetString(bytes);
            payload = JsonUtility.FromJson<JoinApprovalPayload>(json);
            return payload != null;
        }
        catch
        {
            return false;
        }
    }
}
