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

	private static readonly Option<string> OutputPathOption = new("--output-path", "-o")
	{
		Description = "The file path to write output to instead of standard output.",
	};

	private static readonly Option<bool> FindConsumersOption = new("--find-consumers", "-c")
	{
		Description = "Finds package consumers for packages built in the graph and includes them in the output.",
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
		this.IsFormatSpecified = parseResult.CommandResult.Children
			.OfType<System.CommandLine.Parsing.OptionResult>()
			.Any(optionResult => ReferenceEquals(optionResult.Option, FormatOption));
		this.OutputPath = parseResult.GetValue(OutputPathOption);
		this.FindConsumers = parseResult.GetValue(FindConsumersOption);
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
	/// Gets the output file path.
	/// </summary>
	public string? OutputPath { get; init; }

	/// <summary>
	/// Gets a value indicating whether package consumers should be discovered.
	/// </summary>
	public bool FindConsumers { get; init; }

	/// <summary>
	/// Gets a value indicating whether the output format was explicitly specified.
	/// </summary>
	internal bool IsFormatSpecified { get; init; }

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
			OutputPathOption,
			FindConsumersOption,
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

		PackingProjectsOutputFormat format = this.GetEffectiveFormat();
		using TextWriter outputWriter = this.CreateOutputWriter();

		ProjectGraphInput graphInput = await ProjectGraphInputLoader.LoadAsync(fullInputPath, static _ => false, this.CancellationToken);
		if (graphInput.EntryPoints.Count == 0)
		{
			this.WritePackingProjects([], new ConcurrentDictionary<string, string>(), [], format, outputWriter);
			return;
		}

		ConcurrentDictionary<string, string> failedProjects = new(StringComparer.OrdinalIgnoreCase);
		ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> consumedPackagesById = new(StringComparer.OrdinalIgnoreCase);
		List<ProjectInstance> evaluatedProjects = [];

		// Use ProjectGraph to expand traversal projects (like dirs.proj) into their referenced projects.
		// The factory function captures failures for individual projects while allowing successful projects to continue.
		ProjectGraph? projectGraph = null;
		try
		{
			projectGraph = new(
				graphInput.EntryPoints,
				this.MSBuild.ProjectCollection,
				(path, properties, collection) =>
				{
					try
					{
						ProjectInstance instance = ProjectInstance.FromFile(path, new ProjectOptions { GlobalProperties = properties, ProjectCollection = collection });
						if (this.FindConsumers)
						{
							CollectConsumedPackages(instance, consumedPackagesById);
						}

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

		string displayPathBaseDirectory = ResolveDisplayPathBaseDirectory(fullInputPath);
		IReadOnlyList<PackingProjectInfo> packingProjects = FindPackingProjects(projectsToAnalyze, displayPathBaseDirectory);
		IReadOnlyList<BuiltPackageConsumerInfo> builtPackageConsumers = this.FindConsumers
			? FindBuiltPackageConsumers(packingProjects, consumedPackagesById, displayPathBaseDirectory)
			: [];
		this.WritePackingProjects(packingProjects, failedProjects, builtPackageConsumers, format, outputWriter);
	}

	private static void CollectConsumedPackages(
		ProjectInstance project,
		ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> consumedPackagesById)
	{
		CollectConsumedPackages(project.GetItems("PackageReference"), project, consumedPackagesById);
		CollectConsumedPackages(project.GetItems("PackageVersion"), project, consumedPackagesById);
	}

	private static void CollectConsumedPackages(
		ICollection<ProjectItemInstance> items,
		ProjectInstance project,
		ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> consumedPackagesById)
	{
		foreach (ProjectItemInstance item in items)
		{
			string packageId = item.EvaluatedInclude;
			if (string.IsNullOrWhiteSpace(packageId))
			{
				continue;
			}

			string definingProjectPath = item.GetMetadataValue("DefiningProjectFullPath");
			if (string.IsNullOrWhiteSpace(definingProjectPath))
			{
				definingProjectPath = project.FullPath;
			}

			ConcurrentDictionary<string, byte> projectPaths = consumedPackagesById.GetOrAdd(packageId, static _ => new(StringComparer.OrdinalIgnoreCase));
			projectPaths.TryAdd(NormalizePath(definingProjectPath), 0);
		}
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

	private static IReadOnlyList<BuiltPackageConsumerInfo> FindBuiltPackageConsumers(
		IReadOnlyList<PackingProjectInfo> packingProjects,
		ConcurrentDictionary<string, ConcurrentDictionary<string, byte>> consumedPackagesById,
		string displayPathBaseDirectory)
	{
		List<BuiltPackageConsumerInfo> consumers = [];
		foreach (IGrouping<string, PackingProjectInfo> packageGroup in packingProjects.GroupBy(p => p.PackageId, StringComparer.OrdinalIgnoreCase).OrderBy(g => g.Key, StringComparer.OrdinalIgnoreCase))
		{
			if (!consumedPackagesById.TryGetValue(packageGroup.Key, out ConcurrentDictionary<string, byte>? consumerPaths))
			{
				continue;
			}

			consumers.Add(new BuiltPackageConsumerInfo(
				packageGroup.Key,
				consumerPaths.Keys
					.Select(path => GetPathRelativeTo(displayPathBaseDirectory, path))
					.OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
					.ToArray()));
		}

		return consumers;
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

	private PackingProjectsOutputFormat GetEffectiveFormat()
	{
		if (this.IsFormatSpecified || this.Format == PackingProjectsOutputFormat.Json)
		{
			return this.Format;
		}

		if (this.OutputPath is not null && Path.GetExtension(this.OutputPath).Equals(".json", StringComparison.OrdinalIgnoreCase))
		{
			return PackingProjectsOutputFormat.Json;
		}

		return PackingProjectsOutputFormat.Text;
	}

	private TextWriter CreateOutputWriter()
	{
		if (string.IsNullOrWhiteSpace(this.OutputPath))
		{
			return this.Out;
		}

		string fullOutputPath = Path.GetFullPath(this.OutputPath);
		string? outputDirectory = Path.GetDirectoryName(fullOutputPath);
		if (!string.IsNullOrWhiteSpace(outputDirectory))
		{
			Directory.CreateDirectory(outputDirectory);
		}

		return new StreamWriter(fullOutputPath, append: false);
	}

	private void WritePackingProjects(
		IReadOnlyList<PackingProjectInfo> packingProjects,
		ConcurrentDictionary<string, string> failedProjects,
		IReadOnlyList<BuiltPackageConsumerInfo> builtPackageConsumers,
		PackingProjectsOutputFormat format,
		TextWriter output)
	{
		IReadOnlyList<ProjectEvaluationFailure> failures = failedProjects
			.OrderBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase)
			.Select(kv => new ProjectEvaluationFailure(kv.Key, kv.Value))
			.ToArray();

		if (failures.Count > 0)
		{
			this.ExitCode = 1;
		}

		if (format == PackingProjectsOutputFormat.Json)
		{
			PackingProjectsOutput jsonOutput = new(packingProjects.ToArray(), failures.ToArray(), builtPackageConsumers.ToArray());
			output.WriteLine(JsonSerializer.Serialize(jsonOutput, SourceGenerationContext.Default.PackingProjectsOutput));
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
			output.WriteLine("No packing projects found.");
			return;
		}

		foreach (PackingProjectInfo packingProject in packingProjects)
		{
			output.WriteLine($"{packingProject.PackageId}: {packingProject.ProjectPath}");
		}

		if (this.FindConsumers)
		{
			output.WriteLine();
			if (builtPackageConsumers.Count == 0)
			{
				output.WriteLine("No built package IDs were consumed by projects in the graph.");
			}
			else
			{
				output.WriteLine("Consumers of built package IDs:");
				foreach (BuiltPackageConsumerInfo consumerInfo in builtPackageConsumers)
				{
					output.WriteLine($"{consumerInfo.PackageId}:");
					foreach (string projectPath in consumerInfo.ConsumerProjectPaths)
					{
						output.WriteLine($"  {projectPath}");
					}
				}
			}
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
	/// Describes projects that consume a package built within the project graph.
	/// </summary>
	/// <param name="PackageId">The built package ID that is consumed.</param>
	/// <param name="ConsumerProjectPaths">The project file paths that define references to the package.</param>
	internal sealed record BuiltPackageConsumerInfo(string PackageId, string[] ConsumerProjectPaths);

	/// <summary>
	/// The output of the packing projects command.
	/// </summary>
	/// <param name="PackingProjects">The projects that produce NuGet packages.</param>
	/// <param name="FailedProjects">The projects that failed to evaluate.</param>
	/// <param name="BuiltPackageConsumers">The package IDs built by this graph that are consumed by projects in the graph.</param>
	internal sealed record PackingProjectsOutput(PackingProjectInfo[] PackingProjects, ProjectEvaluationFailure[] FailedProjects, BuiltPackageConsumerInfo[] BuiltPackageConsumers);
}
