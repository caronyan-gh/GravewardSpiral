using System.Runtime.InteropServices;
using System.Text;

namespace GravewardSpiral.Game.Ai;

/// <summary>
/// AI の API キーの置き場所。Windows の資格情報マネージャー(汎用の資格情報 GravewardSpiral/&lt;provider&gt;)に入れる。
/// 設定ファイル、セーブ、ログには書かない。資格情報が無ければ、環境変数 ANTHROPIC_API_KEY を見る(開発用)。
/// </summary>
public static class ApiKeyStore
{
    public const string Claude = "claude";

    private static string Target(string provider) => $"GravewardSpiral/{provider}";

    public static string? Load(string provider)
    {
        try
        {
            if (CredRead(Target(provider), CredTypeGeneric, 0, out var ptr))
            {
                try
                {
                    var cred = Marshal.PtrToStructure<Credential>(ptr);
                    if (cred.CredentialBlobSize > 0 && cred.CredentialBlob != IntPtr.Zero)
                    {
                        var bytes = new byte[cred.CredentialBlobSize];
                        Marshal.Copy(cred.CredentialBlob, bytes, 0, bytes.Length);
                        string key = Encoding.UTF8.GetString(bytes).Trim();
                        if (key.Length > 0) return key;
                    }
                }
                finally
                {
                    CredFree(ptr);
                }
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            // Windows 以外: 資格情報マネージャーは無い
        }
        string? env = provider == Claude ? Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") : null;
        return string.IsNullOrWhiteSpace(env) ? null : env.Trim();
    }

    /// <summary>キーを保存する。できたら true。</summary>
    public static bool Save(string provider, string key)
    {
        var bytes = Encoding.UTF8.GetBytes(key.Trim());
        var blob = Marshal.AllocHGlobal(bytes.Length);
        try
        {
            Marshal.Copy(bytes, 0, blob, bytes.Length);
            var cred = new Credential
            {
                Type = CredTypeGeneric,
                TargetName = Target(provider),
                CredentialBlobSize = (uint)bytes.Length,
                CredentialBlob = blob,
                Persist = CredPersistLocalMachine,
                UserName = "GravewardSpiral",
            };
            return CredWrite(ref cred, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            Marshal.FreeHGlobal(blob);
        }
    }

    public static void Delete(string provider)
    {
        try
        {
            CredDelete(Target(provider), CredTypeGeneric, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
        }
    }

    /// <summary>キーの見せ方(先頭と末尾だけ)。</summary>
    public static string Mask(string key) => key.Length <= 12 ? "****" : $"{key[..7]}…{key[^4..]}";

    private const uint CredTypeGeneric = 1;
    private const uint CredPersistLocalMachine = 2;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public uint Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, uint type, uint flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, uint type, uint flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}

/// <summary>クリップボードの文字を読む(Windows)。API キーのような長い文を、画面のキーボードで打たずに入れるのに使う。</summary>
public static class ClipboardText
{
    private const uint CfUnicodeText = 13;

    public static string? Read()
    {
        try
        {
            if (!OpenClipboard(IntPtr.Zero)) return null;
            try
            {
                var handle = GetClipboardData(CfUnicodeText);
                if (handle == IntPtr.Zero) return null;
                var ptr = GlobalLock(handle);
                if (ptr == IntPtr.Zero) return null;
                try
                {
                    return Marshal.PtrToStringUni(ptr);
                }
                finally
                {
                    GlobalUnlock(handle);
                }
            }
            finally
            {
                CloseClipboard();
            }
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool OpenClipboard(IntPtr owner);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool CloseClipboard();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr GetClipboardData(uint format);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GlobalLock(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalUnlock(IntPtr handle);
}
