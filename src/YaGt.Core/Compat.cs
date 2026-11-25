using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace YaGt.Core;

/// <summary>
/// Piccole funzioni che si comportano allo stesso modo con .NET 10 (versione normale) e con .NET Framework 4.8
/// (versione per Windows 7, cartella Win7). Il codice comune le usa al posto delle API che il Framework non ha.
/// </summary>
public static class Compat
{
    public static Encoding Latin1 =>
#if NETFRAMEWORK
        Encoding.GetEncoding(28591);
#else
        Encoding.Latin1;
#endif

    /// <summary>Sposta un file sovrascrivendo la destinazione.</summary>
    public static void MoveOver(string from, string to)
    {
#if NETFRAMEWORK
        if (File.Exists(to)) File.Delete(to);
        File.Move(from, to);
#else
        File.Move(from, to, true);
#endif
    }

    public static string ProcessPath =>
#if NETFRAMEWORK
        System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
#else
        Environment.ProcessPath;
#endif

    public static bool IsWindows => RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
    public static bool IsMac => RuntimeInformation.IsOSPlatform(OSPlatform.OSX);

    public static string RelativePath(string root, string path)
    {
#if NETFRAMEWORK
        var r = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var p = Path.GetFullPath(path);
        return p.StartsWith(r, StringComparison.OrdinalIgnoreCase) ? p.Substring(r.Length) : p;
#else
        return Path.GetRelativePath(root, path);
#endif
    }

    public static Task<string> ReadString(HttpContent c, CancellationToken ct) =>
#if NETFRAMEWORK
        c.ReadAsStringAsync();
#else
        c.ReadAsStringAsync(ct);
#endif

    public static async Task<byte[]> GetBytes(HttpClient http, string url, CancellationToken ct)
    {
#if NETFRAMEWORK
        using var r = await http.GetAsync(url, ct);
        r.EnsureSuccessStatusCode();
        return await r.Content.ReadAsByteArrayAsync();
#else
        return await http.GetByteArrayAsync(url, ct);
#endif
    }

    public static Task WriteBytes(string path, byte[] data, CancellationToken ct)
    {
#if NETFRAMEWORK
        File.WriteAllBytes(path, data); return Task.CompletedTask;
#else
        return File.WriteAllBytesAsync(path, data, ct);
#endif
    }

    public static async Task<UdpReceiveResult> Receive(UdpClient u, CancellationToken ct)
    {
#if NETFRAMEWORK
        var t = u.ReceiveAsync();
        var done = await Task.WhenAny(t, Task.Delay(Timeout.Infinite, ct));
        if (done != t) { _ = t.ContinueWith(x => _ = x.Exception, TaskContinuationOptions.OnlyOnFaulted); throw new OperationCanceledException(ct); }
        return await t;
#else
        return await u.ReceiveAsync(ct);
#endif
    }

    public static async Task Connect(TcpClient c, string host, int port, CancellationToken ct)
    {
#if NETFRAMEWORK
        var t = c.ConnectAsync(host, port);
        var done = await Task.WhenAny(t, Task.Delay(Timeout.Infinite, ct));
        if (done != t) { try { c.Close(); } catch { } throw new OperationCanceledException(ct); }
        await t;
#else
        await c.ConnectAsync(host, port, ct);
#endif
    }
}
