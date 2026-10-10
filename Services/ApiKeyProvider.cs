#nullable enable

using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace TerraWikiAI.Services;

internal sealed class ApiKeyProvider
{
    private readonly string[] _keyPaths;

    public ApiKeyProvider(string dataDirectory, params string[] additionalKeyPaths)
    {
        _keyPaths = new string[additionalKeyPaths.Length + 1];
        _keyPaths[0] = Path.Combine(dataDirectory, "key");
        Array.Copy(additionalKeyPaths, 0, _keyPaths, 1, additionalKeyPaths.Length);
    }

    public string KeyPath => _keyPaths[0];

    public bool SaveLocalKey(string candidate, out string message)
    {
        string key = candidate.Trim();
        if (!LooksValid(key))
        {
            message = "剪贴板中没有有效的 DeepSeek API Key（应以 sk- 开头）。";
            return false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(KeyPath)!);
            string temporaryPath = KeyPath + ".tmp";
            File.WriteAllText(temporaryPath, key, new UTF8Encoding(false));
            File.Move(temporaryPath, KeyPath, true);
            TryRestrictUnixPermissions(KeyPath);
            message = $"API Key 已保存到本机专用目录，指纹 {GetFingerprint(key)}。Key 不会进入程序发布包或聊天历史。";
            return true;
        }
        catch (Exception exception)
        {
            message = "保存 API Key 失败：" + exception.Message;
            return false;
        }
    }

    public bool RemoveLocalKey(out string message)
    {
        try
        {
            if (File.Exists(KeyPath))
                File.Delete(KeyPath);
            message = "本地 API Key 文件已删除。环境变量中的 Key（如果存在）不会被修改。";
            return true;
        }
        catch (Exception exception)
        {
            message = "删除本地 API Key 失败：" + exception.Message;
            return false;
        }
    }

    public string GetSafeStatus()
    {
        return TryGetKey(out string key, out string source)
            ? $"已配置；来源：{DescribeSource(source)}；指纹：{GetFingerprint(key)}"
            : $"未配置；本地路径：{KeyPath}";
    }

    public bool TryGetKey(out string key, out string source)
    {
        string? environmentKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY")?.Trim();
        if (LooksValid(environmentKey))
        {
            key = environmentKey!;
            source = "环境变量 DEEPSEEK_API_KEY";
            return true;
        }

        foreach (string keyPath in _keyPaths)
        {
            try
            {
                if (File.Exists(keyPath))
                {
                    string fileKey = File.ReadAllText(keyPath).Trim();
                    if (LooksValid(fileKey))
                    {
                        key = fileKey;
                        source = keyPath;
                        return true;
                    }
                }
            }
            catch
            {
                // Try the next local key source.
            }
        }

        key = "";
        source = _keyPaths[0];
        return false;
    }

    private static bool LooksValid(string? value)
    {
        return !string.IsNullOrWhiteSpace(value)
            && value.StartsWith("sk-", StringComparison.Ordinal)
            && value.Length >= 20;
    }

    private string DescribeSource(string source)
    {
        if (source.StartsWith("环境变量", StringComparison.Ordinal))
            return source;
        return string.Equals(source, KeyPath, StringComparison.OrdinalIgnoreCase)
            ? "本地专用 Key 文件"
            : "兼容的本地 Key 文件";
    }

    private static string GetFingerprint(string key)
    {
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(hash.AsSpan(0, 4));
    }

    private static void TryRestrictUnixPermissions(string path)
    {
        if (OperatingSystem.IsWindows())
            return;
        try
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
        catch
        {
        }
    }
}
