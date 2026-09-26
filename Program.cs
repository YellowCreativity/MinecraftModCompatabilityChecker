using System.Text;
using Modrinth;
using Modrinth.Models;
using Spectre.Console;
using File = System.IO.File;
using Version = Modrinth.Models.Version;

namespace MinecraftModCompatabilityChecker;

internal class Program
{
    private static readonly ModrinthClient Client = new(new ModrinthClientConfig
    {
        UserAgent = "MinecraftModCompatabilityChecker"
    });

    private static readonly HashSet<string> Loaders = new(StringComparer.OrdinalIgnoreCase)
    {
        "fabric",
        "quilt",
        "forge",
        "neoforge",
        "liteloader",
        "bukkit",
        "bungeecord",
        "folia",
        "paper",
        "purpur",
        "spigot",
        "velocity",
        "waterfall",
        "sponge",
        "ornithe",
        "bta-babric",
        "nilloader"
    };

    private static async Task<int> Main(string[] args)
    {
        try
        {
            Console.OutputEncoding = Encoding.UTF8;

            if (args.Length == 0)
                throw new InvalidOperationException("You must provide a target loader.");

            var targetLoader = args[0].Trim();
            if (!Loaders.Contains(targetLoader))
                throw new InvalidOperationException($"Unknown loader '{targetLoader}'.");

            var modlistFilePath = "modlist.txt";
            if (args.Length > 1)
                modlistFilePath = args[1];

            Project[] projects = [];
            await AnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue"))
                .StartAsync("Fetching modlist...", async _ =>
                {
                    projects = await Client.Project.GetMultipleAsync(
                        ModListFileToUrlArray(modlistFilePath)
                            .Select(mod => mod.Split("/")[^1]));
                });

            var projectVersions = new Dictionary<Project, Version[]>();
            await AnsiConsole.Status()
                .SpinnerStyle(Style.Parse("blue"))
                .StartAsync("Fetching project versions...", async ctx =>
                {
                    foreach (var project in projects)
                    {
                        ctx.Status(
                            $"{Markup.Escape($"[{projectVersions.Count,3}/{projects.Length}]")} Fetching project versions for {project.Title}");
                        projectVersions[project] =
                            await Client.Version.GetProjectVersionListAsync(
                                project.Id,
                                [targetLoader, "minecraft", "datapack", "iris", "optifine"]
                            );
                    }
                });

            var uniqueVersions = GetUniqueGameVersionsAcrossProjects(projects);

            var universallySupported = uniqueVersions
                .Where(version => projects.All(project =>
                    projectVersions[project].Any(projectVersion =>
                        IsProjectSupported(projectVersion, targetLoader, version))))
                .ToHashSet();

            var table = new Table()
                .RoundedBorder()
                .ShowRowSeparators()
                .AddColumn("Name");

            foreach (var version in uniqueVersions)
            {
                var header = universallySupported.Contains(version)
                    ? $"[bold green]{version}[/]"
                    : version;

                table.AddColumn(header, col => col.Centered());
            }

            var sortedProjects = projects
                .OrderByDescending(project =>
                    uniqueVersions.Count(version =>
                        projectVersions[project].Any(projectVersion =>
                            IsProjectSupported(projectVersion, targetLoader, version))))
                .ThenByDescending(project => project.Id)
                .ToArray();

            foreach (var project in sortedProjects)
            {
                var title = $"[link=https://modrinth.com/mod/{project.Id}] {Markup.Escape(project.Title)}[/]";

                var versions = projectVersions[project];

                var isLoaderSupported = versions.Any(version =>
                    IsProjectSupported(version, targetLoader));
                if (!isLoaderSupported)
                    AnsiConsole.MarkupLine(
                        $"[red]✗ [bold]{title}[/] does not support {ColorfulLoaderText(targetLoader)}, supported loader(s):[/] {string.Join(", ", project.Loaders.Select(ColorfulLoaderText))}");

                var supportedVersions = uniqueVersions
                    .Select(version =>
                        versions.Any(projectVersion =>
                            IsProjectSupported(projectVersion, targetLoader, version)))
                    .ToArray();

                var supportedVersionMarkup = supportedVersions
                    .Select(supported =>
                        supported
                            ? "[green]✓[/]"
                            : "[red]✗[/]")
                    .ToArray();

                var isUniversal = supportedVersions.All(supported => supported);

                table.AddRow([
                    isLoaderSupported
                        ? isUniversal ? $"[green]{title}[/]" : title
                        : $"[red]{title}[/]",
                    .. supportedVersionMarkup
                ]);
            }

            AnsiConsole.WriteLine();
            AnsiConsole.Write(table);

            var versionSupport = uniqueVersions
                .Select(version => new
                {
                    Version = version,
                    Count = projects.Count(project =>
                        projectVersions[project].Any(projectVersion =>
                            IsProjectSupported(projectVersion, targetLoader, version)))
                })
                .OrderByDescending(x => x.Count)
                .ThenByDescending(x => x.Version)
                .ToArray();

            AnsiConsole.MarkupLine(
                $"Most supported versions in order: {string.Join(", ", versionSupport
                    .Select(x =>
                        $"[blue]{x.Version}[/] ([yellow]{x.Count}[/])"))}");

            return 0;
        }
        catch (Exception ex)
        {
            AnsiConsole.WriteException(ex, ExceptionFormats.ShortenPaths);
            return 1;
        }
    }

    private static bool IsProjectSupported(Version version, string loader, string? gameVersion = null)
    {
        return (gameVersion is null || version.GameVersions.Contains(gameVersion)) &&
               (version.Loaders.Contains(loader) ||
                version.Loaders.Contains("minecraft") ||
                version.Loaders.Contains("iris"));
    }

    private static string[] GetUniqueGameVersionsAcrossProjects(IEnumerable<Project> projects)
    {
        return projects
            .SelectMany(project => project.GameVersions)
            .Distinct()
            .Where(version =>
            {
                if (version.Contains("rc") || version.Contains("pre"))
                    return false;

                var parts = version.Split('.');

                if (parts.Length < 2)
                    return false;

                if (!int.TryParse(parts[0], out var major))
                    return false;

                if (!int.TryParse(parts[1], out var minor))
                    return false;

                return major > 1 || (major == 1 && minor >= 20);
            })
            .OrderBy(version =>
            {
                var parts = version.Split('.');
                return int.Parse(parts[0]);
            })
            .ThenBy(version =>
            {
                var parts = version.Split('.');
                return int.Parse(parts[1]);
            })
            .ThenBy(version =>
            {
                var parts = version.Split('.');
                return parts.Length > 2 && int.TryParse(parts[2], out var patch)
                    ? patch
                    : 0;
            }).ToArray();
    }

    private static string[] ModListFileToUrlArray(string modListFilePath)
    {
        return !File.Exists(modListFilePath)
            ? throw new InvalidOperationException("Mod list file does not exist.")
            : File.ReadAllLines(modListFilePath)
                .Where(line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#')).ToArray();
    }

    private static string ColorfulLoaderText(string loaderText)
    {
        var normalized = loaderText.ToLowerInvariant();

        var (displayName, color) = normalized switch
        {
            "fabric" => ("Fabric", "#dbb69b"),
            "quilt" => ("Quilt", "#c796f9"),
            "forge" => ("Forge", "#959eef"),
            "neoforge" => ("NeoForge", "#f99e6b"),
            "liteloader" => ("LiteLoader", "#7ab0ee"),
            "bukkit" => ("Bukkit", "#f6af7b"),
            "bungeecord" => ("BungeeCord", "#d2c080"),
            "folia" => ("Folia", "#a5e388"),
            "paper" => ("Paper", "#eaa"),
            "purpur" => ("Purpur", "#c3abf7"),
            "spigot" => ("Spigot", "#f1cc84"),
            "velocity" => ("Velocity", "#83d5ef"),
            "waterfall" => ("Waterfall", "#78a4fb"),
            "sponge" => ("Sponge", "#f9e580"),
            "ornithe" => ("Ornithe", "#87c7ff"),
            "bta-babric" => ("BTA-Babric", "#72cc4a"),
            "nilloader" => ("NilLoader", "#f45e9a"),
            "datapack" => ("Datapack", "#ffffff"),
            "minecraft" => ("Minecraft", "#ffffff"),
            "iris" => ("Iris", "#ffffff"),
            "optifine" => ("Optifine", "#ffffff"),
            _ => throw new NotSupportedException($"Unsupported loader: {loaderText}")
        };

        return $"[{color}]{displayName}[/]";
    }
}