using System.Diagnostics;
using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.Extensions.Logging;

namespace LgtvSwitcher.MacOS;

/// <summary>m1ddc が列挙した外部ディスプレイ 1 台分。</summary>
/// <param name="Index">m1ddc の <c>display N</c> 指定に使う 1 始まりの番号。</param>
/// <param name="Name">ディスプレイ名（例: <c>DELL U2725QE</c>）。</param>
/// <param name="Uuid">macOS が振るディスプレイ UUID。再接続しても変わらない。</param>
public sealed record M1ddcDisplay(int Index, string Name, string Uuid);

/// <summary>
/// Apple Silicon で DDC/CI を読むために <c>m1ddc</c> CLI（Homebrew）を呼び出すクライアント。
/// </summary>
/// <remarks>
/// Apple Silicon の DDC は IOAVService の非公開 API でしか叩けない。自前で P/Invoke するより、
/// 実績のある m1ddc に委ねる方が macOS 更新への追従を任せられる。
/// I2C は同時アクセスに弱いため、プロセス内では呼び出しを直列化する。
/// </remarks>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public sealed partial class M1ddcClient
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(3);

    // launchd は PATH に Homebrew を含めないため、既知のインストール先を順に探す。
    private static readonly string[] CandidatePaths =
    [
        "/opt/homebrew/bin/m1ddc",
        "/usr/local/bin/m1ddc",
    ];

    private readonly ILogger<M1ddcClient> _logger;
    private readonly Lock _gate = new();
    private readonly string? _executable;

    public M1ddcClient(ILogger<M1ddcClient> logger)
    {
        _logger = logger;
        _executable = ResolveExecutable();
        if (_executable is null)
        {
            _logger.LogWarning("m1ddc が見つかりません。`brew install m1ddc` で導入してください。入力ソース判定は無効になります。");
        }
    }

    public bool IsAvailable => _executable is not null;

    /// <summary>外部ディスプレイを列挙する。m1ddc が無い・失敗したときは null。</summary>
    public IReadOnlyList<M1ddcDisplay>? ListDisplays()
    {
        var output = Run("display", "list");
        if (output is null)
        {
            return null;
        }

        var displays = new List<M1ddcDisplay>();
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var match = DisplayLinePattern().Match(line);
            if (match.Success)
            {
                displays.Add(new M1ddcDisplay(
                    int.Parse(match.Groups["index"].Value, CultureInfo.InvariantCulture),
                    match.Groups["name"].Value.Trim(),
                    match.Groups["uuid"].Value));
            }
        }

        return displays;
    }

    /// <summary>VCP 0x60（Input Source）を読む。失敗時は null。</summary>
    public int? GetInputSource(int displayIndex)
    {
        var output = Run("display", displayIndex.ToString(CultureInfo.InvariantCulture), "get", "input");
        if (output is null)
        {
            return null;
        }

        return int.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }

    private string? Run(params string[] args)
    {
        if (_executable is null)
        {
            return null;
        }

        lock (_gate)
        {
            try
            {
                var psi = new ProcessStartInfo(_executable)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                };
                foreach (var arg in args)
                {
                    psi.ArgumentList.Add(arg);
                }

                using var process = Process.Start(psi);
                if (process is null)
                {
                    return null;
                }

                var stdout = process.StandardOutput.ReadToEndAsync();
                var stderr = process.StandardError.ReadToEndAsync();
                if (!process.WaitForExit(CommandTimeout))
                {
                    process.Kill(entireProcessTree: true);
                    _logger.LogDebug("m1ddc {Args} timed out.", string.Join(' ', args));
                    return null;
                }

                var text = stdout.GetAwaiter().GetResult();
                // m1ddc は DDC 失敗時も exit 0 で "DDC communication failure" を出すことがある。
                if (process.ExitCode != 0 || text.Contains("failure", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug(
                        "m1ddc {Args} failed (exit={Exit}): {Output}{Error}",
                        string.Join(' ', args),
                        process.ExitCode,
                        text.Trim(),
                        stderr.GetAwaiter().GetResult().Trim());
                    return null;
                }

                return text;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "m1ddc {Args} threw.", string.Join(' ', args));
                return null;
            }
        }
    }

    private static string? ResolveExecutable()
    {
        foreach (var path in CandidatePaths)
        {
            if (File.Exists(path))
            {
                return path;
            }
        }

        var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathEnv.Split(':', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = Path.Combine(dir, "m1ddc");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    // 例: "[2] DELL U2725QE (A2C7AD5E-FA53-4969-A0FB-105CA62A3482)"
    [GeneratedRegex(@"^\[(?<index>\d+)\]\s+(?<name>.+?)\s+\((?<uuid>[0-9A-Fa-f-]{36})\)$")]
    private static partial Regex DisplayLinePattern();
}
