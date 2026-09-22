using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Bookworm.Core.Auth;

namespace Bookworm.Windows.Platform.Credentials;

/// <summary>
/// <see cref="ICredentialStore"/> backed by the Windows Credential Manager (generic credentials),
/// via direct P/Invoke rather than a third-party NuGet wrapper — the Win32 surface is tiny and stable,
/// and this avoids taking on an unmaintained dependency for something this small.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsCredentialManagerStore : ICredentialStore
{
    public Task<string?> GetSecretAsync(string key, CancellationToken ct = default) =>
        Task.FromResult(NativeCredentialManager.Read(key));

    public Task SetSecretAsync(string key, string secret, CancellationToken ct = default)
    {
        NativeCredentialManager.Write(key, secret);
        return Task.CompletedTask;
    }

    public Task DeleteSecretAsync(string key, CancellationToken ct = default)
    {
        NativeCredentialManager.Delete(key);
        return Task.CompletedTask;
    }
}

[SupportedOSPlatform("windows")]
internal static class NativeCredentialManager
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const int ErrorNotFound = 1168;

    public static string? Read(string target)
    {
        if (!CredRead(target, CredTypeGeneric, 0, out var credPtr))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ErrorNotFound)
            {
                return null;
            }
            throw new InvalidOperationException($"Failed to read Windows credential '{target}' (Win32 error {error}).");
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return null;
            }
            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, credential.CredentialBlobSize);
            return Encoding.Unicode.GetString(bytes);
        }
        finally
        {
            CredFree(credPtr);
        }
    }

    public static void Write(string target, string secret)
    {
        var bytes = Encoding.Unicode.GetBytes(secret);
        var credential = new Credential
        {
            Type = CredTypeGeneric,
            TargetName = Marshal.StringToCoTaskMemUni(target),
            CredentialBlobSize = bytes.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(Math.Max(bytes.Length, 1)),
            Persist = CredPersistLocalMachine,
            UserName = Marshal.StringToCoTaskMemUni("Bookworm"),
        };

        try
        {
            Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"Failed to write Windows credential '{target}' (Win32 error {Marshal.GetLastWin32Error()}).");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.TargetName);
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
            Marshal.FreeCoTaskMem(credential.UserName);
        }
    }

    public static void Delete(string target)
    {
        if (!CredDelete(target, CredTypeGeneric, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ErrorNotFound)
            {
                throw new InvalidOperationException($"Failed to delete Windows credential '{target}' (Win32 error {error}).");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public IntPtr TargetName;
        public IntPtr Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public IntPtr TargetAlias;
        public IntPtr UserName;
    }

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredWriteW")]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredReadW")]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CredFree(IntPtr credentialPtr);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CredDeleteW")]
    private static extern bool CredDelete(string target, int type, int flags);
}
