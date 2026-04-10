// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using Microsoft.Build.Graph;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// Builds an MSBuild project graph and writes it to a DGML file.
/// </summary>
public class GraphCommand : MSBuildCommandBase
{
	/// <summary>
	/// Initializes a new instance of the <see cref="GraphCommand"/> class.
	/// </summary>
	public GraphCommand()
	{
	}

	/// <summary>
	/// Initializes a new instance of the <see cref="GraphCommand"/> class.
	/// </summary>
	/// <inheritdoc cref="CommandBase(ParseResult, CancellationToken)"/>
	public GraphCommand(ParseResult parseResult, CancellationToken cancellationToken)
		: base(parseResult, cancellationToken)
	{
	}

	private enum InputKind
	{
		Project,
		Sln,
		Slnx,
	}

	/// <summary>
	/// Gets the input project or solution path.
	/// </summary>
	public required string InputPath { get; init; }

	/// <summary>
	/// Gets the output DGML file path.
	/// </summary>
	public string? OutputPath { get; init; }

	/// <summary>
	/// Gets the paths or path prefixes to project files that should be omitted from the DGML.
	/// Relative paths are resolved against the current working directory.
	/// </summary>
	public string[]? ExcludedProjectPaths { get; init; }

	/// <summary>
	/// Creates the command.
	/// </summary>
	/// <returns>The command.</returns>
	internal static Command CreateCommand()
	{
		Argument<string> inputArgument = new("input")
		{
			Description = "The path to a project file, .sln, or .slnx file.",
		};
		Argument<string?> outputArgument = new("output")
		{
			Arity = ArgumentArity.ZeroOrOne,
			Description = "The DGML file to write. Defaults to the input path with a .dgml extension.",
		};
		Option<string[]> excludeOption = new("--exclude", "-e")
		{
			Description = "One or more project file paths or path prefixes to omit from the DGML. Relative paths are resolved against the current working directory.",
			AllowMultipleArgumentsPerToken = true,
		};

		Command command = new("graph", "Builds an MSBuild project graph and writes it as DGML.")
		{
			inputArgument,
			outputArgument,
			excludeOption,
		};
		command.SetAction((parseResult, cancellationToken) => new GraphCommand(parseResult, cancellationToken)
		{
			InputPath = parseResult.GetValue(inputArgument)!,
			OutputPath = parseResult.GetValue(outputArgument),
			ExcludedProjectPaths = parseResult.GetValue(excludeOption),
		}.ExecuteAndDisposeAsync());

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
		if (!IsSupportedInput(extension))
		{
			this.Error.WriteLine($"Unsupported input type '{extension}'. Expected a project file, .sln, or .slnx.");
			this.ExitCode = 1;
			return;
		}

		string outputPath = this.OutputPath is not null
			? Path.GetFullPath(this.OutputPath)
			: Path.ChangeExtension(fullInputPath, ".dgml");
		HashSet<string> excludedProjectPaths = NormalizeProjectPathsRelativeTo(Environment.CurrentDirectory, this.ExcludedProjectPaths);

		try
		{
			GraphInput graphInput = await LoadGraphInputAsync(fullInputPath, excludedProjectPaths, this.CancellationToken);
			GraphModel graphModel = graphInput.EntryPoints.Count > 0
				? BuildGraphModel(new ProjectGraph(graphInput.EntryPoints), graphInput.ExplicitSolutionProjects, excludedProjectPaths)
				: new GraphModel([], []);
			XDocument dgml = CreateDgml(graphModel, fullInputPath, graphInput.InputKind);

			Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
			dgml.Save(outputPath);
			this.Out.WriteLine($"Wrote {graphModel.Nodes.Count} node(s) and {graphModel.Edges.Count} edge(s) to {outputPath}");
		}
		catch (Exception ex)
		{
			this.Error.WriteLine(FormatException(ex));
			this.ExitCode = 1;
		}
	}

	private static bool IsSupportedInput(string extension)
		=> extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase)
		|| extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
		|| extension.EndsWith("proj", StringComparison.OrdinalIgnoreCase);

	private static string FormatException(Exception exception)
	{
		List<string> messages = [];
		for (Exception? current = exception; current is not null; current = current.InnerException)
		{
			messages.Add(current.Message.Trim());
		}

		return string.Join(Environment.NewLine + "Caused by: ", messages);
	}

	private static async Task<GraphInput> LoadGraphInputAsync(string inputPath, IReadOnlySet<string> excludedProjectPaths, CancellationToken cancellationToken)
	{
		string extension = Path.GetExtension(inputPath);
		if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
			&& !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
		{
			return new GraphInput(
			InputKind.Project,
			IsExcludedProjectPath(inputPath, excludedProjectPaths) ? [] : [new ProjectGraphEntryPoint(inputPath)],
			new HashSet<string>(StringComparer.OrdinalIgnoreCase));
		}

		ISolutionSerializer serializer = SolutionSerializers.GetSerializerByMoniker(inputPath) ?? throw new InvalidOperationException($"No solution serializer is available for '{inputPath}'.");
		SolutionModel solutionModel = await serializer.OpenAsync(inputPath, cancellationToken);
		string solutionDirectory = Path.GetDirectoryName(inputPath)!;

		HashSet<string> explicitProjects = new(StringComparer.OrdinalIgnoreCase);
		List<ProjectGraphEntryPoint> entryPoints = [];
		foreach (SolutionProjectModel solutionProject in solutionModel.SolutionProjects)
		{
			string projectPath = NormalizeProjectPathRelativeTo(solutionDirectory, solutionProject.FilePath);
			if (IsExcludedProjectPath(projectPath, excludedProjectPaths))
			{
				continue;
			}

			explicitProjects.Add(projectPath);
			entryPoints.Add(new ProjectGraphEntryPoint(projectPath));
		}

		return new GraphInput(
			extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase) ? InputKind.Slnx : InputKind.Sln,
			entryPoints,
			explicitProjects);
	}

	private static GraphModel BuildGraphModel(ProjectGraph projectGraph, HashSet<string> explicitSolutionProjects, IReadOnlySet<string> excludedProjectPaths)
	{
		Dictionary<string, GraphNodeModel> nodesByPath = new(StringComparer.OrdinalIgnoreCase);
		HashSet<(string SourcePath, string TargetPath)> edgeKeys = [];
		List<GraphEdgeModel> edges = [];

		foreach (ProjectGraphNode graphNode in projectGraph.ProjectNodes)
		{
			string sourcePath = NormalizeProjectPath(graphNode.ProjectInstance.FullPath);
			if (IsExcludedProjectPath(sourcePath, excludedProjectPaths))
			{
				continue;
			}

			GetOrCreateNode(nodesByPath, sourcePath, explicitSolutionProjects.Contains(sourcePath));

			foreach (ProjectGraphNode projectReference in graphNode.ProjectReferences)
			{
				string targetPath = NormalizeProjectPath(projectReference.ProjectInstance.FullPath);
				if (IsExcludedProjectPath(targetPath, excludedProjectPaths))
				{
					continue;
				}

				GetOrCreateNode(nodesByPath, targetPath, explicitSolutionProjects.Contains(targetPath));

				if (edgeKeys.Add((sourcePath, targetPath)))
				{
					edges.Add(new GraphEdgeModel(nodesByPath[sourcePath].Id, nodesByPath[targetPath].Id));
				}
			}
		}

		return new GraphModel(
			nodesByPath.Values
				.OrderBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
				.ThenBy(node => node.Path, StringComparer.OrdinalIgnoreCase)
				.ToList(),
			edges
				.OrderBy(edge => edge.SourceId, StringComparer.Ordinal)
				.ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
				.ToList());
	}

	private static GraphNodeModel GetOrCreateNode(
		IDictionary<string, GraphNodeModel> nodesByPath,
		string projectPath,
		bool isExplicitSolutionProject)
	{
		if (nodesByPath.TryGetValue(projectPath, out GraphNodeModel? existing))
		{
			if (isExplicitSolutionProject)
			{
				existing.IsExplicitSolutionProject = true;
			}

			return existing;
		}

		GraphNodeModel created = new(
			CreateDeterministicNodeId(projectPath),
			projectPath,
			Path.GetFileName(projectPath),
			isExplicitSolutionProject);
		nodesByPath.Add(projectPath, created);
		return created;
	}

	private static string CreateDeterministicNodeId(string projectPath)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(projectPath));
		return $"project:{Convert.ToHexString(hash)}";
	}

	private static XDocument CreateDgml(GraphModel graphModel, string inputPath, InputKind inputKind)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		XElement nodesElement = new(ns + "Nodes");
		foreach (GraphNodeModel node in graphModel.Nodes)
		{
			nodesElement.Add(new XElement(
			ns + "Node",
			new XAttribute("Id", node.Id),
			new XAttribute("Label", node.Label),
			new XAttribute("Path", node.Path)));
		}

		XElement linksElement = new(ns + "Links");
		foreach (GraphEdgeModel edge in graphModel.Edges)
		{
			linksElement.Add(new XElement(
			ns + "Link",
			new XAttribute("Source", edge.SourceId),
			new XAttribute("Target", edge.TargetId),
			new XAttribute("Category", "ProjectReference")));
		}

		if (inputKind == InputKind.Slnx && graphModel.Nodes.Any(node => node.IsExplicitSolutionProject))
		{
			const string containerId = "solution:explicit-projects";
			nodesElement.Add(new XElement(
			ns + "Node",
			new XAttribute("Id", containerId),
			new XAttribute("Label", Path.GetFileName(inputPath)),
			new XAttribute("Group", "Expanded")));

			foreach (GraphNodeModel node in graphModel.Nodes.Where(node => node.IsExplicitSolutionProject))
			{
				linksElement.Add(new XElement(
				ns + "Link",
				new XAttribute("Source", containerId),
				new XAttribute("Target", node.Id),
				new XAttribute("Category", "Contains")));
			}
		}

		return new XDocument(
			new XDeclaration("1.0", "utf-8", "yes"),
			new XElement(
				ns + "DirectedGraph",
				new XAttribute("GraphDirection", "LeftToRight"),
				new XElement(
					ns + "Properties",
					new XElement(
						ns + "Property",
						new XAttribute("Id", "Path"),
						new XAttribute("Label", "Path"),
						new XAttribute("DataType", "System.String"))),
				new XElement(
					ns + "Categories",
					new XElement(
						ns + "Category",
						new XAttribute("Id", "Contains"),
						new XAttribute("Label", "Contains"),
						new XAttribute("IsContainment", "True")),
					new XElement(
					ns + "Category",
					new XAttribute("Id", "ProjectReference"),
					new XAttribute("Label", "Project Reference"))),
				nodesElement,
				linksElement));
	}

	private static HashSet<string> NormalizeProjectPathsRelativeTo(string baseDirectory, IEnumerable<string>? paths)
	{
		HashSet<string> normalizedPaths = new(StringComparer.OrdinalIgnoreCase);
		if (paths is null)
		{
			return normalizedPaths;
		}

		foreach (string path in paths)
		{
			normalizedPaths.Add(NormalizeProjectPathRelativeTo(baseDirectory, path));
		}

		return normalizedPaths;
	}

	private static bool IsExcludedProjectPath(string projectPath, IReadOnlySet<string> excludedProjectPaths)
	{
		foreach (string excludedProjectPath in excludedProjectPaths)
		{
			if (projectPath.Equals(excludedProjectPath, StringComparison.OrdinalIgnoreCase))
			{
				return true;
			}

			if (projectPath.Length > excludedProjectPath.Length
				&& projectPath.StartsWith(excludedProjectPath, StringComparison.OrdinalIgnoreCase)
				&& IsDirectorySeparator(projectPath[excludedProjectPath.Length]))
			{
				return true;
			}
		}

		return false;
	}

	private static string NormalizeProjectPathRelativeTo(string baseDirectory, string path)
		=> NormalizeProjectPath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

	private static string NormalizeProjectPath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	private static bool IsDirectorySeparator(char value)
		=> value == Path.DirectorySeparatorChar || value == Path.AltDirectorySeparatorChar;

	private sealed class GraphInput
	{
		public GraphInput(InputKind inputKind, IReadOnlyList<ProjectGraphEntryPoint> entryPoints, HashSet<string> explicitSolutionProjects)
		{
			this.InputKind = inputKind;
			this.EntryPoints = entryPoints;
			this.ExplicitSolutionProjects = explicitSolutionProjects;
		}

		public InputKind InputKind { get; }

		public IReadOnlyList<ProjectGraphEntryPoint> EntryPoints { get; }

		public HashSet<string> ExplicitSolutionProjects { get; }
	}

	private sealed class GraphModel
	{
		public GraphModel(IReadOnlyList<GraphNodeModel> nodes, IReadOnlyList<GraphEdgeModel> edges)
		{
			this.Nodes = nodes;
			this.Edges = edges;
		}

		public IReadOnlyList<GraphNodeModel> Nodes { get; }

		public IReadOnlyList<GraphEdgeModel> Edges { get; }
	}

	private sealed class GraphEdgeModel
	{
		public GraphEdgeModel(string sourceId, string targetId)
		{
			this.SourceId = sourceId;
			this.TargetId = targetId;
		}

		public string SourceId { get; }

		public string TargetId { get; }
	}

	private sealed class GraphNodeModel(string id, string path, string label, bool isExplicitSolutionProject)
	{
		public string Id { get; } = id;

		public string Path { get; } = path;

		public string Label { get; } = label;

		public bool IsExplicitSolutionProject { get; set; } = isExplicitSolutionProject;
	}
}
