// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.Json;
using Microsoft.Build.Execution;
using Microsoft.Build.Graph;

namespace Nerdbank.DotNetRepoTools.NuGet;

/// <summary>
/// Lists the projects that produce NuGet packages.
/// </summary>
public class PackingProjectsCommand : MSBuildCommandBase
{
	private static readonly Argument<string> InputArgument = new("input")
	{
		Description = "The path to a project file, .sln, or .slnx file.",
	};

	private static readonly Option<PackingProjectsOutputFormat> FormatOption = new("--format", "-f")
	{
		Description = "The output format. Defaults to text.",
	};

	/// <summary>
	/// Initializes a new instance of the <see cref="PackingProjectsCommand"/> class.
	/// </summary>
	public PackingProjectsCommand()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="PackingProjectsCommand"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(ParseResult, CancellationToken)"/>
	[SetsRequiredMembers]
	public PackingProjectsCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
		this.InputPath = parseResult.GetValue(InputArgument)!;
		this.Format = parseResult.GetValue(FormatOption);
	}

	/// <summary>
	/// Gets the input project or solution path.
	/// </summary>
	public required string InputPath { get; init; }

	/// <summary>
	/// Gets the output format.
	/// </summary>
	public PackingProjectsOutputFormat Format { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Command command = new("packing-projects", "Lists the projects that produce NuGet packages.")
		{
			InputArgument,
			FormatOption,
		};
		command.SetAction((parseResult, cancellationToken) => new PackingProjectsCommand(parseResult, cancellationToken).ExecuteAndDisposeAsync());
		return command;
	}

	/// <inheritdoc/>
	protected override async Task ExecuteCoreAsync()
	{
		string fullInputPath = Path.GetFullPath(this.InputPath);
		if (!File.Exists(fullInputPath))
		{
			this.Error.WriteLine($"Input file not found: {fullInputPath}");
			this.ExitCode = 1;
			return;
		}

		string extension = Path.GetExtension(fullInputPath);
		if (!ProjectGraphInputLoader.IsSupportedInput(extension))
		{
			this.Error.WriteLine($"Unsupported input type '{extension}'. Expected a project file, .sln, or .slnx.");
			this.ExitCode = 1;
			return;
		}

		try
		{
			ProjectGraphInput graphInput = await ProjectGraphInputLoader.LoadAsync(fullInputPath, static _ => false, this.CancellationToken);
			IReadOnlyList<PackingProjectInfo> packingProjects = graphInput.EntryPoints.Count > 0
				? FindPackingProjects(new ProjectGraph(graphInput.EntryPoints), ResolveDisplayPathBaseDirectory(fullInputPath))
				: [];

			this.WritePackingProjects(packingProjects);
		}
		catch (Exception ex)
		{
			this.Error.WriteLine(FormatException(ex));
			this.ExitCode = 1;
		}
	}

	private static IReadOnlyList<PackingProjectInfo> FindPackingProjects(ProjectGraph projectGraph, string displayPathBaseDirectory)
	{
		Dictionary<string, PackingProjectInfo> packingProjectsByPath = new(StringComparer.OrdinalIgnoreCase);
		foreach (ProjectGraphNode graphNode in projectGraph.ProjectNodes)
		{
			ProjectInstance project = graphNode.ProjectInstance;
			if (IsInnerBuild(project) || !ProducesPackage(project))
			{
				continue;
			}

			string fullProjectPath = NormalizePath(project.FullPath);
			packingProjectsByPath.TryAdd(
				fullProjectPath,
				new PackingProjectInfo(
					GetPackageId(project),
					GetPathRelativeTo(displayPathBaseDirectory, fullProjectPath)));
		}

		return packingProjectsByPath.Values
			.OrderBy(project => project.PackageId, StringComparer.OrdinalIgnoreCase)
			.ThenBy(project => project.ProjectPath, StringComparer.OrdinalIgnoreCase)
			.ToArray();
	}

	private static bool ProducesPackage(ProjectInstance project)
	{
		if (Path.GetExtension(project.FullPath).Equals(".nuproj", StringComparison.OrdinalIgnoreCase))
		{
			return true;
		}

		return IsTrue(project.GetPropertyValue("IsPackable"));
	}

	private static bool IsInnerBuild(ProjectInstance project)
		=> IsTrue(project.GetPropertyValue("IsInnerBuild"))
		|| (project.GetPropertyValue("TargetFramework").Length > 0 && project.GetPropertyValue("TargetFrameworks").Length > 0);

	private static bool IsTrue(string propertyValue)
		=> bool.TryParse(propertyValue, out bool result) && result;

	private static string GetPackageId(ProjectInstance project)
	{
		bool isNuProj = Path.GetExtension(project.FullPath).Equals(".nuproj", StringComparison.OrdinalIgnoreCase);
		string packageId = isNuProj
			? project.GetPropertyValue("PackageName")
			: project.GetPropertyValue("PackageId");
		if (string.IsNullOrWhiteSpace(packageId) && isNuProj)
		{
			packageId = project.GetPropertyValue("PackageId");
		}

		if (string.IsNullOrWhiteSpace(packageId) && isNuProj)
		{
			packageId = project.GetPropertyValue("Id");
		}

		if (string.IsNullOrWhiteSpace(packageId))
		{
			packageId = project.GetPropertyValue("MSBuildProjectName");
		}

		return string.IsNullOrWhiteSpace(packageId) ? Path.GetFileNameWithoutExtension(project.FullPath) : packageId;
	}

	private static string FormatException(Exception exception)
	{
		List<string> messages = [];
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			messages.Add(current.Message.Trim());
		}

		return string.Join(Environment.NewLine + "Caused by: ", messages);
	}

	private static string ResolveDisplayPathBaseDirectory(string inputPath)
	{
		string inputDirectory = Path.GetDirectoryName(inputPath)!;
		return FindGitRepoRoot(inputDirectory) ?? inputDirectory;
	}

	private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	private static string GetPathRelativeTo(string baseDirectory, string path)
		=> Path.TrimEndingDirectorySeparator(Path.GetRelativePath(baseDirectory, path));

	private void WritePackingProjects(IReadOnlyList<PackingProjectInfo> packingProjects)
	{
		if (this.Format == PackingProjectsOutputFormat.Json)
		{
			this.Out.WriteLine(JsonSerializer.Serialize(packingProjects, SourceGenerationContext.Default.PackingProjectInfoArray));
			return;
		}

		if (packingProjects.Count == 0)
		{
			this.Out.WriteLine("No packing projects found.");
			return;
		}

		foreach (PackingProjectInfo packingProject in packingProjects)
		{
			this.Out.WriteLine($"{packingProject.PackageId}: {packingProject.ProjectPath}");
		}
	}

	/// <summary>
	/// Identifies a NuGet package and the project that produces it.
	/// </summary>
	/// <param name="PackageId">The NuGet package ID.</param>
	/// <param name="ProjectPath">The path to the project that produces the package.</param>
	internal sealed record PackingProjectInfo(string PackageId, string ProjectPath);
}
