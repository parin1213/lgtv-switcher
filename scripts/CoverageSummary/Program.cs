using System.Globalization;
using System.Xml.Linq;

var files = Directory
    .EnumerateFiles("tests", "coverage.cobertura.xml", SearchOption.AllDirectories)
    .Select(path => new FileInfo(path))
    .OrderByDescending(file => file.LastWriteTimeUtc)
    .ToList();

if (files.Count == 0)
{
    Console.Error.WriteLine("coverage.cobertura.xml が見つかりません。先に dotnet test --settings coverlet.runsettings --collect:\"XPlat Code Coverage\" を実行してください。");
    return 1;
}

var latestByProject = new Dictionary<string, FileInfo>(StringComparer.OrdinalIgnoreCase);
foreach (var file in files)
{
    var projectDir = file.Directory?.Parent?.Parent?.FullName;
    if (projectDir is not null)
    {
        latestByProject.TryAdd(projectDir, file);
    }
}

var lineHits = new Dictionary<(string File, int Line), int>();
var fileLines = new Dictionary<string, Dictionary<int, int>>(StringComparer.OrdinalIgnoreCase);
var branchCovered = 0;
var branchTotal = 0;

Console.WriteLine("Per project coverage:");
foreach (var entry in latestByProject.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
{
    var doc = XDocument.Load(entry.Value.FullName);
    var root = doc.Root ?? throw new InvalidOperationException("coverage XML root がありません。");
    var projectName = Path.GetFileName(entry.Key);
    var lineRate = ReadDouble(root, "line-rate") * 100;
    var branchRate = ReadDouble(root, "branch-rate") * 100;
    Console.WriteLine($"  {projectName}: line={lineRate:F2}% branch={branchRate:F2}% ({entry.Value.FullName})");

    foreach (var classElement in doc.Descendants("class"))
    {
        var className = (string?)classElement.Attribute("name") ?? string.Empty;
        if (!className.StartsWith("LGTVSwitcher.", StringComparison.Ordinal) ||
            className.StartsWith("LGTVSwitcher.Daemon.Windows.Program", StringComparison.Ordinal))
        {
            continue;
        }

        var filename = (string?)classElement.Attribute("filename");
        if (string.IsNullOrWhiteSpace(filename))
        {
            continue;
        }
        filename = NormalizeFilename(filename);

        foreach (var lineElement in classElement.Element("lines")?.Elements("line") ?? [])
        {
            var line = int.Parse((string)lineElement.Attribute("number")!, CultureInfo.InvariantCulture);
            var hits = int.Parse((string?)lineElement.Attribute("hits") ?? "0", CultureInfo.InvariantCulture);
            var key = (filename, line);
            lineHits[key] = Math.Max(lineHits.GetValueOrDefault(key), hits);
            if (!fileLines.TryGetValue(filename, out var fileLineHits))
            {
                fileLineHits = new Dictionary<int, int>();
                fileLines[filename] = fileLineHits;
            }
            fileLineHits[line] = Math.Max(fileLineHits.GetValueOrDefault(line), hits);

            if (string.Equals((string?)lineElement.Attribute("branch"), "true", StringComparison.OrdinalIgnoreCase))
            {
                var conditionCoverage = (string?)lineElement.Attribute("condition-coverage") ?? string.Empty;
                var start = conditionCoverage.IndexOf('(');
                var slash = conditionCoverage.IndexOf('/');
                var end = conditionCoverage.IndexOf(')');
                if (start >= 0 && slash > start && end > slash)
                {
                    branchCovered += int.Parse(conditionCoverage[(start + 1)..slash], CultureInfo.InvariantCulture);
                    branchTotal += int.Parse(conditionCoverage[(slash + 1)..end], CultureInfo.InvariantCulture);
                }
            }
        }
    }
}

if (lineHits.Count == 0)
{
    Console.Error.WriteLine("カバレッジ行を集計できませんでした。");
    return 1;
}

var lineCovered = lineHits.Values.Count(hits => hits > 0);
var mergedLineRate = (double)lineCovered / lineHits.Count * 100;
var mergedBranchRate = branchTotal == 0 ? 0 : (double)branchCovered / branchTotal * 100;

Console.WriteLine();
Console.WriteLine($"Merged coverage: line={mergedLineRate:F2}% ({lineCovered}/{lineHits.Count}) branch={mergedBranchRate:F2}% ({branchCovered}/{branchTotal})");

Console.WriteLine();
Console.WriteLine("Lowest files:");
foreach (var item in fileLines
    .Select(entry =>
    {
        var total = entry.Value.Count;
        var covered = entry.Value.Values.Count(hits => hits > 0);
        var rate = (double)covered / total * 100;
        return new { File = entry.Key, Total = total, Covered = covered, Rate = rate };
    })
    .OrderBy(item => item.Rate)
    .ThenByDescending(item => item.Total)
    .Take(15))
{
    Console.WriteLine($"  {item.Rate,6:F2}% {item.Covered,4}/{item.Total,-4} {item.File}");
}
return 0;

static double ReadDouble(XElement element, string attribute)
{
    var value = (string?)element.Attribute(attribute) ?? "0";
    return double.Parse(value, CultureInfo.InvariantCulture);
}

static string NormalizeFilename(string filename)
{
    var normalized = filename.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
    var prefixes = new[]
    {
        "LGTVSwitcher.Core",
        "LGTVSwitcher.Daemon.Windows",
        "LgtvSwitcher.MacOS",
    };

    foreach (var prefix in prefixes)
    {
        var marker = prefix + Path.DirectorySeparatorChar;
        var index = normalized.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index >= 0)
        {
            return normalized[(index + marker.Length)..];
        }
    }

    return normalized;
}
