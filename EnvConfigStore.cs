using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace CodexReconnectTool;

internal sealed class EnvConfigStore
{
    private const string Marker = "# codex-reconnect-fix";
    private static readonly Regex ProxyLine = new(
        @"^\s*(?:export\s+)?(?:HTTP_PROXY|HTTPS_PROXY|ALL_PROXY|NO_PROXY|WS_PROXY|WSS_PROXY)\s*=",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly UTF8Encoding Utf8NoBom = new(false);
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string Home { get; }
    public string EnvPath { get; }
    public string StatePath { get; }

    public EnvConfigStore()
    {
        Home = GetCodexHome();
        EnvPath = Path.Combine(Home, ".env");
        StatePath = Path.Combine(Home, ".codex-reconnect-fix.state.json");
    }

    public bool HasUndoState => File.Exists(StatePath);

    public bool HasUserEditsSinceApply()
    {
        if (!File.Exists(StatePath))
            return false;

        var state = ReadState();
        if (string.IsNullOrWhiteSpace(state.AppliedHash) || !File.Exists(EnvPath))
            return true;

        return !string.Equals(state.AppliedHash, HashFile(EnvPath), StringComparison.OrdinalIgnoreCase);
    }

    public string Apply(int port)
    {
        Directory.CreateDirectory(Home);
        var state = File.Exists(StatePath) ? ReadState() : CreateOriginalState();

        if (state.OriginalFileExisted &&
            (string.IsNullOrWhiteSpace(state.BackupPath) || !File.Exists(state.BackupPath)))
            throw new InvalidOperationException("找不到原始 .env 备份，未修改配置。");

        var lines = File.Exists(EnvPath) ? File.ReadAllLines(EnvPath).ToList() : new List<string>();
        lines = lines.Where(line => line != Marker && !ProxyLine.IsMatch(line)).ToList();
        while (lines.Count > 0 && string.IsNullOrWhiteSpace(lines[^1]))
            lines.RemoveAt(lines.Count - 1);

        if (lines.Count > 0)
            lines.Add(string.Empty);
        lines.Add(Marker);
        lines.Add($"HTTP_PROXY=http://127.0.0.1:{port}");
        lines.Add($"HTTPS_PROXY=http://127.0.0.1:{port}");
        lines.Add("NO_PROXY=localhost,127.0.0.1,::1");

        var content = string.Join("\r\n", lines) + "\r\n";
        WriteAtomically(EnvPath, content);
        state.AppliedHash = HashFile(EnvPath);
        WriteState(state);

        return state.OriginalFileExisted
            ? $"已备份原配置：{state.BackupPath}"
            : "原先没有 .env；撤销时会移除本工具新建的配置文件。";
    }

    public string Undo(bool allowReplacingEditedFile)
    {
        if (!File.Exists(StatePath))
            throw new InvalidOperationException("没有找到本工具保存的撤销记录。");

        var state = ReadState();
        var changed = HasUserEditsSinceApply();
        if (changed && !allowReplacingEditedFile)
            throw new InvalidOperationException(".env 在修复后发生过变化；请先确认是否仍要恢复备份。");

        if (state.OriginalFileExisted)
        {
            if (string.IsNullOrWhiteSpace(state.BackupPath) || !File.Exists(state.BackupPath))
                throw new InvalidOperationException("找不到原始 .env 备份，未撤销配置。");
            File.Copy(state.BackupPath, EnvPath, true);
        }
        else if (File.Exists(EnvPath))
        {
            File.Delete(EnvPath);
        }

        File.Delete(StatePath);
        return state.OriginalFileExisted
            ? $"已恢复原 .env。备份副本保留在：{state.BackupPath}"
            : "已删除本工具新建的 .env；原先没有配置文件。";
    }

    private StateFile CreateOriginalState()
    {
        var existed = File.Exists(EnvPath);
        string? backupPath = null;
        if (existed)
        {
            backupPath = EnvPath + ".codex-reconnect-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fff") + ".bak";
            File.Copy(EnvPath, backupPath, false);
        }

        var state = new StateFile
        {
            Version = 1,
            OriginalFileExisted = existed,
            BackupPath = backupPath
        };
        WriteState(state);
        return state;
    }

    private StateFile ReadState()
    {
        try
        {
            var state = JsonSerializer.Deserialize<StateFile>(File.ReadAllText(StatePath), JsonOptions);
            if (state is null || state.Version != 1)
                throw new InvalidOperationException("撤销记录格式不受支持。");
            return state;
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("撤销记录损坏，未修改配置。", ex);
        }
    }

    private void WriteState(StateFile state)
    {
        var json = JsonSerializer.Serialize(state, JsonOptions);
        WriteAtomically(StatePath, json + Environment.NewLine);
    }

    private static void WriteAtomically(string path, string content)
    {
        var tempPath = path + ".tmp-" + Guid.NewGuid().ToString("N");
        File.WriteAllText(tempPath, content, Utf8NoBom);
        File.Move(tempPath, path, true);
    }

    private static string HashFile(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    private static string GetCodexHome()
    {
        var configured = Environment.GetEnvironmentVariable("CODEX_HOME");
        return !string.IsNullOrWhiteSpace(configured)
            ? Path.GetFullPath(Environment.ExpandEnvironmentVariables(configured))
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex");
    }

    private sealed class StateFile
    {
        public int Version { get; set; }
        public bool OriginalFileExisted { get; set; }
        public string? BackupPath { get; set; }
        public string? AppliedHash { get; set; }
    }
}
