using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace DeLienCalendar.Desktop.Services;

/// <summary>端末トークンを Windows DPAPI（現在ユーザー）で暗号化して保存する。</summary>
public static class SecureStore
{
    private static string TokenPath => Path.Combine(AppPaths.DataDirectory, "token.dat");

    public static string? LoadToken()
    {
        try
        {
            if (!File.Exists(TokenPath)) return null;
            var encrypted = File.ReadAllBytes(TokenPath);
            var plain = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
            var token = Encoding.UTF8.GetString(plain);
            return string.IsNullOrWhiteSpace(token) ? null : token;
        }
        catch (Exception ex)
        {
            AppLog.Write("端末トークンの復号に失敗しました（再ログインが必要です）", ex);
            return null;
        }
    }

    public static void SaveToken(string token)
    {
        Directory.CreateDirectory(AppPaths.DataDirectory);
        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(token), null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(TokenPath, encrypted);
    }

    public static void ClearToken()
    {
        try
        {
            if (File.Exists(TokenPath)) File.Delete(TokenPath);
        }
        catch (Exception ex)
        {
            AppLog.Write("端末トークンの削除に失敗しました", ex);
        }
    }
}
