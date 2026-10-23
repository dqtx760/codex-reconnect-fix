using Microsoft.Win32;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.RegularExpressions;

namespace CodexReconnectTool;

internal sealed record ProxyCandidate(int Port, string Source, int Priority);
internal sealed record ProxyProbeResult(int? StatusCode, string? Error)
{
    public bool IsWorking => StatusCode == 200;
}
internal sealed record ReachabilityResult(int? StatusCode, string? Error)
{
    public bool ReceivedResponse => StatusCode.HasValue;
}

internal static class ProxyService
{
    private static readonly int[] CommonPorts =
    [
        7897, 7890, 7899, 10809, 10808, 2080, 1080, 1081, 7891,
        20171, 20172, 33210, 8889, 8080
    ];

    private static readonly Regex LoopbackPort = new(
        @"(?:127\.0\.0\.1|localhost|\[::1\]|::1)\s*:\s*(\d{1,5})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static List<ProxyCandidate> GetCandidates(string codexHome)
    {
        var candidates = new List<ProxyCandidate>();
        var ports = new HashSet<int>();

        void Add(int port, string source, int priority)
        {
            if (port is < 1 or > 65535 || !ports.Add(port))
                return;
            candidates.Add(new ProxyCandidate(port, source, priority));
        }

        var proxyNames = new[]
        {
            "HTTP_PROXY", "HTTPS_PROXY", "ALL_PROXY", "http_proxy", "https_proxy", "all_proxy"
        };

        foreach (var name in proxyNames)
        {
            AddPorts(Environment.GetEnvironmentVariable(name), $"当前进程 {name}", 0, Add);
            AddPorts(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User), $"用户变量 {name}", 1, Add);
            AddPorts(Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.Machine), $"系统变量 {name}", 2, Add);
        }

        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                @"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (key?.GetValue("ProxyServer") is string proxyServer)
                AddPorts(proxyServer, "Windows 系统代理", 3, Add);
        }
        catch
        {
            // Registry access is optional; the remaining discovery sources still work.
        }

        var envPath = Path.Combine(codexHome, ".env");
        if (File.Exists(envPath))
        {
            try
            {
                foreach (var line in File.ReadLines(envPath))
                {
                    var match = Regex.Match(line,
                        @"^\s*(?:export\s+)?(?:HTTP_PROXY|HTTPS_PROXY|ALL_PROXY|http_proxy|https_proxy|all_proxy)\s*=\s*(.+?)\s*$",
                        RegexOptions.IgnoreCase);
                    if (match.Success)
                        AddPorts(match.Groups[1].Value.Trim('"', '\''), "Codex .env", 4, Add);
                }
            }
            catch
            {
                // A malformed or unreadable .env must not prevent a manual port entry.
            }
        }

        foreach (var port in CommonPorts)
            Add(port, "常见端口", 5);

        try
        {
            foreach (var endpoint in IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners())
            {
                if (IPAddress.IsLoopback(endpoint.Address))
                    Add(endpoint.Port, "本机监听端口", 6);
            }
        }
        catch
        {
            // Manual entry remains available if listener enumeration is unavailable.
        }

        return candidates.OrderBy(x => x.Priority).ToList();
    }

    public static async Task<List<(ProxyCandidate Candidate, ProxyProbeResult Result)>> ScanAsync(
        string codexHome, CancellationToken cancellationToken)
    {
        var candidates = GetCandidates(codexHome);
        using var semaphore = new SemaphoreSlim(16);
        var tasks = candidates.Select(async candidate =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                return (candidate, await ProbeHttpProxyAsync(candidate.Port, cancellationToken));
            }
            finally
            {
                semaphore.Release();
            }
        });

        var results = await Task.WhenAll(tasks);
        return results.ToList();
    }

    public static async Task<ProxyProbeResult> ProbeHttpProxyAsync(int port, CancellationToken cancellationToken)
    {
        if (port is < 1 or > 65535)
            return new ProxyProbeResult(null, "端口号超出范围");

        using var client = new TcpClient();
        try
        {
            await client.ConnectAsync(IPAddress.Loopback, port)
                .WaitAsync(TimeSpan.FromMilliseconds(500), cancellationToken);

            await using var stream = client.GetStream();
            var request = Encoding.ASCII.GetBytes(
                "CONNECT chatgpt.com:443 HTTP/1.1\r\nHost: chatgpt.com:443\r\n\r\n");
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(2));
            await stream.WriteAsync(request, timeout.Token);

            var buffer = new byte[512];
            var count = await stream.ReadAsync(buffer, timeout.Token);
            if (count == 0)
                return new ProxyProbeResult(null, "代理端口没有返回数据");

            var firstLine = Encoding.ASCII.GetString(buffer, 0, count).Split("\r\n")[0];
            var match = Regex.Match(firstLine, @"^HTTP/1\.[01]\s+(\d{3})");
            return match.Success
                ? new ProxyProbeResult(int.Parse(match.Groups[1].Value), null)
                : new ProxyProbeResult(null, "端口没有返回 HTTP 代理响应");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new ProxyProbeResult(null, "连接或响应超时");
        }
        catch (Exception ex) when (ex is SocketException or IOException or ObjectDisposedException or TimeoutException)
        {
            return new ProxyProbeResult(null, "端口不可用");
        }
    }

    public static async Task<ReachabilityResult> TestChatGptAsync(int port, CancellationToken cancellationToken)
    {
        try
        {
            using var handler = new HttpClientHandler
            {
                Proxy = new WebProxy($"http://127.0.0.1:{port}"),
                UseProxy = true
            };
            using var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(10) };
            using var request = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/");
            request.Headers.UserAgent.ParseAdd("CodexReconnectTool/1.0");
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            return new ReachabilityResult((int)response.StatusCode, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return new ReachabilityResult(null, ex is TaskCanceledException ? "请求超时" : "连接失败");
        }
    }

    private static void AddPorts(string? value, string source, int priority, Action<int, string, int> add)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        foreach (Match match in LoopbackPort.Matches(value))
        {
            if (int.TryParse(match.Groups[1].Value, out var port))
                add(port, source, priority);
        }
    }
}
