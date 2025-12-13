namespace LGTVSwitcher.Daemon.Windows;

internal static class CliArgs
{
    public static string[] Normalize(string[]? args)
    {
        if (args is null || args.Length == 0)
        {
            return new[] { "run" };
        }

        var first = args[0];
        if (first.StartsWith("--", StringComparison.OrdinalIgnoreCase))
        {
            if (first.StartsWith("--discover", StringComparison.OrdinalIgnoreCase) ||
                first.StartsWith("--pair", StringComparison.OrdinalIgnoreCase))
            {
                var rest = first.StartsWith("--discover", StringComparison.OrdinalIgnoreCase)
                    ? args.Skip(1)
                    : args.AsEnumerable();
                return new[] { "discover" }.Concat(rest).ToArray();
            }

            if (first.StartsWith("--run", StringComparison.OrdinalIgnoreCase))
            {
                return new[] { "run" }.Concat(args.Skip(1)).ToArray();
            }
        }

        return args;
    }
}
