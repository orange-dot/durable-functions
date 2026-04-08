using System.Security.Cryptography;
using System.Text;

namespace Orchestration.Supabase.Internal;

internal static class WorkflowDefinitionIdentity
{
    private static readonly Guid NamespaceId = new("B7C5CE49-2B67-4A0F-A0C1-3F640CC5B252");

    internal static string Create(string workflowType, string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        ArgumentException.ThrowIfNullOrWhiteSpace(version);

        var nameBytes = Encoding.UTF8.GetBytes($"{workflowType}:{version}");
        var namespaceBytes = NamespaceId.ToByteArray();
        SwapByteOrder(namespaceBytes);

        var data = new byte[namespaceBytes.Length + nameBytes.Length];
        Buffer.BlockCopy(namespaceBytes, 0, data, 0, namespaceBytes.Length);
        Buffer.BlockCopy(nameBytes, 0, data, namespaceBytes.Length, nameBytes.Length);

        Span<byte> hash = stackalloc byte[20];
        SHA1.HashData(data, hash);

        var newGuid = hash[..16].ToArray();
        newGuid[6] = (byte)((newGuid[6] & 0x0F) | 0x50);
        newGuid[8] = (byte)((newGuid[8] & 0x3F) | 0x80);
        SwapByteOrder(newGuid);

        return new Guid(newGuid).ToString();
    }

    private static void SwapByteOrder(byte[] guid)
    {
        static void Swap(byte[] bytes, int left, int right)
        {
            (bytes[left], bytes[right]) = (bytes[right], bytes[left]);
        }

        Swap(guid, 0, 3);
        Swap(guid, 1, 2);
        Swap(guid, 4, 5);
        Swap(guid, 6, 7);
    }
}
