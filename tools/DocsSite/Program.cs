using Pyrop.DocsSite;

var root = new DirectoryInfo(AppContext.BaseDirectory);
while (root is not null && !File.Exists(Path.Combine(root.FullName, "Pyrop.sln")))
{
    root = root.Parent;
}

if (root is null)
{
    Console.Error.WriteLine("The Pyrop repository was not found above the tool's folder.");
    return 1;
}

var output = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(root.FullName, "publish", "site"));
var builder = new SiteBuilder(Path.Combine(root.FullName, "docs"), Path.Combine(root.FullName, "editors", "vscode", "icons", "pyrop.svg"));
var problems = builder.Build(output);
foreach (var problem in problems)
{
    Console.Error.WriteLine(problem);
}

if (problems.Count > 0)
{
    Console.Error.WriteLine($"Broken links: {problems.Count}. Nothing was written.");
    return 1;
}

Console.WriteLine($"The site for Pyrop {SiteBuilder.Version} is in {output}");
return 0;
