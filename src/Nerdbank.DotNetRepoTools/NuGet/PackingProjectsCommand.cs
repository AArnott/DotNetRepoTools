// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Build.Definition;
using Microsoft.Build.Evaluation;
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
		command.SetAction(async (parseResult, cancellationToken) =>
		{
			using var cmd = new PackingProjectsCommand(parseResult, cancellationToken);
			await cmd.ExecuteAsync();
			return cmd.ExitCode;
		});
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

		ProjectGraphInput graphInput = await ProjectGraphInputLoader.LoadAsync(fullInputPath, static _ => false, this.CancellationToken);
		if (graphInput.EntryPoints.Count == 0)
		{
			this.WritePackingProjects([], new ConcurrentDictionary<string, string>());
			return;
		}

		ConcurrentDictionary<string, string> failedProjects = new(StringComparer.OrdinalIgnoreCase);
		List<ProjectInstance> evaluatedProjects = [];

		// Use ProjectGraph to expand traversal projects (like dirs.proj) into their referenced projects.
		// The factory function captures failures for individual projects while allowing successful projects to continue.
		ProjectGraph? projectGraph = null;
		try
		{
			projectGraph = new(
				graphInput.EntryPoints,
				ProjectCollection.GlobalProjectCollection,
				(path, properties, collection) =>
				{
					try
					{
						ProjectInstance instance = ProjectInstance.FromFile(path, new ProjectOptions { GlobalProperties = properties });
						lock (evaluatedProjects)
						{
							evaluatedProjects.Add(instance);
						}

						return instance;
					}
					catch (Exception ex)
					{
						failedProjects.TryAdd(path, ex.Message);

						// Return a minimal project instance to allow graph construction to continue.
						// This project won't be packable, so it will be filtered out later.
						return CreateMinimalProjectInstance(path, properties);
					}
				});
		}
		catch (Exception)
		{
			// Graph construction failed. We've captured what we could via the factory function.
		}

		// Prefer extracting from the graph itself when available (more complete), else use factory-captured instances.
		IReadOnlyList<ProjectInstance> projectsToAnalyze = projectGraph?.ProjectNodes.Select(n => n.ProjectInstance).ToList()
			?? (IReadOnlyList<ProjectInstance>)evaluatedProjects;

		IReadOnlyList<PackingProjectInfo> packingProjects = FindPackingProjects(projectsToAnalyze, ResolveDisplayPathBaseDirectory(fullInputPath));
		this.WritePackingProjects(packingProjects, failedProjects);
	}

	private static ProjectInstance CreateMinimalProjectInstance(string projectPath, IDictionary<string, string> globalProperties)
	{
		// Create a minimal in-memory project that won't be considered packable.
		string minimalProject = $"""
			<Project>
			  <PropertyGroup>
			    <IsPackable>false</IsPackable>
			  </PropertyGroup>
			</Project>
			""";
		using System.Xml.XmlReader reader = System.Xml.XmlReader.Create(new StringReader(minimalProject));
		return ProjectInstance.FromProjectRootElement(
			Microsoft.Build.Construction.ProjectRootElement.Create(reader),
			new ProjectOptions { GlobalProperties = globalProperties });
	}

	private static IReadOnlyList<PackingProjectInfo> FindPackingProjects(IReadOnlyList<ProjectInstance> projects, string displayPathBaseDirectory)
	{
		Dictionary<string, PackingProjectInfo> packingProjectsByPath = new(StringComparer.OrdinalIgnoreCase);
		foreach (ProjectInstance project in projects)
		{
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

	private static string ResolveDisplayPathBaseDirectory(string inputPath)
	{
		string inputDirectory = Path.GetDirectoryName(inputPath)!;
		return FindGitRepoRoot(inputDirectory) ?? inputDirectory;
	}

	private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	private static string GetPathRelativeTo(string baseDirectory, string path)
		=> Path.TrimEndingDirectorySeparator(Path.GetRelativePath(baseDirectory, path));

	private void WritePackingProjects(IReadOnlyList<PackingProjectInfo> packingProjects, ConcurrentDictionary<string, string> failedProjects)
	{
		IReadOnlyList<ProjectEvaluationFailure> failures = failedProjects
			.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
			.Select(kv => new ProjectEvaluationFailure(kv.Key, kv.Value))
			.ToArray();

		if (failures.Count > 0)
		{
			this.ExitCode = 1;
		}

		if (this.Format == PackingProjectsOutputFormat.Json)
		{
			PackingProjectsOutput output = new(packingProjects.ToArray(), failures.ToArray());
			this.Out.WriteLine(JsonSerializer.Serialize(output, SourceGenerationContext.Default.PackingProjectsOutput));
			return;
		}

		if (failures.Count > 0)
		{
			this.Error.WriteLine("The following projects failed to evaluate:");
			foreach (ProjectEvaluationFailure failure in failures)
			{
				this.Error.WriteLine($"  {failure.ProjectPath}");
				this.Error.WriteLine($"    {failure.ErrorMessage}");
			}

			this.Error.WriteLine();
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

	/// <summary>
	/// Describes a project that failed to evaluate.
	/// </summary>
	/// <param name="ProjectPath">The path to the project that failed to evaluate.</param>
	/// <param name="ErrorMessage">The error message describing the failure.</param>
	internal sealed record ProjectEvaluationFailure(string ProjectPath, string ErrorMessage);

	/// <summary>
	/// The output of the packing projects command.
	/// </summary>
	/// <param name="PackingProjects">The projects that produce NuGet packages.</param>
	/// <param name="FailedProjects">The projects that failed to evaluate.</param>
	internal sealed record PackingProjectsOutput(PackingProjectInfo[] PackingProjects, ProjectEvaluationFailure[] FailedProjects);
}
