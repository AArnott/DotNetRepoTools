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
	private const string ContainsCategory = "Contains";
	private const string ProjectReferenceCategory = "ProjectReference";
	private const string SlnxExplicitProjectsContainerId = "solution:explicit-projects";

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
	/// Gets the directory paths that should be emitted as DGML containers.
	/// Relative paths are resolved against the current working directory.
	/// </summary>
	public string[]? GroupPaths { get; init; }

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
		Option<string[]> groupOption = new("--group", "-g")
		{
			Description = "One or more directory paths that should become DGML containers. Relative paths are resolved against the current working directory.",
			AllowMultipleArgumentsPerToken = true,
		};

		Command command = new("graph", "Builds an MSBuild project graph and writes it as DGML.")
		{
			inputArgument,
			outputArgument,
			excludeOption,
			groupOption,
		};
		command.SetAction((parseResult, cancellationToken) => new GraphCommand(parseResult, cancellationToken)
		{
			InputPath = parseResult.GetValue(inputArgument)!,
			OutputPath = parseResult.GetValue(outputArgument),
			ExcludedProjectPaths = parseResult.GetValue(excludeOption),
			GroupPaths = parseResult.GetValue(groupOption),
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
		HashSet<string> excludedProjectPaths = NormalizePathsRelativeTo(Environment.CurrentDirectory, this.ExcludedProjectPaths);
		HashSet<string> groupingPaths = NormalizePathsRelativeTo(Environment.CurrentDirectory, this.GroupPaths);

		try
		{
			GraphInput graphInput = await LoadGraphInputAsync(fullInputPath, excludedProjectPaths, this.CancellationToken);
			GraphModel graphModel = graphInput.EntryPoints.Count > 0
				? BuildGraphModel(new ProjectGraph(graphInput.EntryPoints), graphInput.ExplicitSolutionProjects, excludedProjectPaths, groupingPaths, fullInputPath, graphInput.InputKind)
				: new GraphModel([], []);
			XDocument dgml = CreateDgml(graphModel);

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
			string projectPath = NormalizePathRelativeTo(solutionDirectory, solutionProject.FilePath);
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

	private static GraphModel BuildGraphModel(ProjectGraph projectGraph, HashSet<string> explicitSolutionProjects, IReadOnlySet<string> excludedProjectPaths, IReadOnlySet<string> groupingPaths, string inputPath, InputKind inputKind)
	{
		Dictionary<string, GraphNodeModel> projectNodesByPath = new(StringComparer.OrdinalIgnoreCase);
		List<GraphNodeModel> containerNodes = [];
		Dictionary<string, GraphNodeModel> groupingContainerNodesByPath = new(StringComparer.OrdinalIgnoreCase);
		HashSet<(string SourceId, string TargetId, string Category)> edgeKeys = [];
		List<GraphEdgeModel> edges = [];

		foreach (ProjectGraphNode graphNode in projectGraph.ProjectNodes)
		{
			string sourcePath = NormalizePath(graphNode.ProjectInstance.FullPath);
			if (IsExcludedProjectPath(sourcePath, excludedProjectPaths))
			{
				continue;
			}

			GetOrCreateProjectNode(projectNodesByPath, sourcePath, explicitSolutionProjects.Contains(sourcePath));

			foreach (ProjectGraphNode projectReference in graphNode.ProjectReferences)
			{
				string targetPath = NormalizePath(projectReference.ProjectInstance.FullPath);
				if (IsExcludedProjectPath(targetPath, excludedProjectPaths))
				{
					continue;
				}

				GetOrCreateProjectNode(projectNodesByPath, targetPath, explicitSolutionProjects.Contains(targetPath));
				AddEdge(projectNodesByPath[sourcePath].Id, projectNodesByPath[targetPath].Id, ProjectReferenceCategory, edges, edgeKeys);
			}
		}

		if (groupingPaths.Count > 0)
		{
			AddGroupingContainers(groupingPaths, projectNodesByPath.Values, groupingContainerNodesByPath, containerNodes, edges, edgeKeys);
		}
		else if (inputKind == InputKind.Slnx && projectNodesByPath.Values.Any(node => node.IsExplicitSolutionProject))
		{
			AddSlnxExplicitProjectsContainer(inputPath, projectNodesByPath.Values.Where(node => node.IsExplicitSolutionProject), containerNodes, edges, edgeKeys);
		}

		return new GraphModel(
			projectNodesByPath.Values
				.Concat(containerNodes)
				.OrderBy(node => node.IsContainer ? 0 : 1)
				.ThenBy(node => node.Label, StringComparer.OrdinalIgnoreCase)
				.ThenBy(node => node.Path ?? string.Empty, StringComparer.OrdinalIgnoreCase)
				.ThenBy(node => node.Id, StringComparer.Ordinal)
				.ToList(),
			edges
				.OrderBy(edge => edge.Category, StringComparer.Ordinal)
				.ThenBy(edge => edge.SourceId, StringComparer.Ordinal)
				.ThenBy(edge => edge.TargetId, StringComparer.Ordinal)
				.ToList());
	}

	private static void AddGroupingContainers(
		IReadOnlySet<string> groupingPaths,
		IEnumerable<GraphNodeModel> projectNodes,
		IDictionary<string, GraphNodeModel> groupingContainerNodesByPath,
		ICollection<GraphNodeModel> containerNodes,
		ICollection<GraphEdgeModel> edges,
		ISet<(string SourceId, string TargetId, string Category)> edgeKeys)
	{
		foreach (string groupingPath in groupingPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			GetOrCreateContainerNode(groupingContainerNodesByPath, containerNodes, groupingPath);
		}

		foreach (string groupingPath in groupingPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase))
		{
			string? parentGroupingPath = FindContainingPath(groupingPath, groupingPaths, allowExactMatch: false);
			if (parentGroupingPath is not null)
			{
				AddEdge(groupingContainerNodesByPath[parentGroupingPath].Id, groupingContainerNodesByPath[groupingPath].Id, ContainsCategory, edges, edgeKeys);
			}
		}

		foreach (GraphNodeModel projectNode in projectNodes)
		{
			string? groupingPath = FindContainingPath(projectNode.Path!, groupingPaths, allowExactMatch: true);
			if (groupingPath is not null)
			{
				AddEdge(groupingContainerNodesByPath[groupingPath].Id, projectNode.Id, ContainsCategory, edges, edgeKeys);
			}
		}
	}

	private static void AddSlnxExplicitProjectsContainer(
		string inputPath,
		IEnumerable<GraphNodeModel> explicitProjectNodes,
		ICollection<GraphNodeModel> containerNodes,
		ICollection<GraphEdgeModel> edges,
		ISet<(string SourceId, string TargetId, string Category)> edgeKeys)
	{
		GraphNodeModel containerNode = new(SlnxExplicitProjectsContainerId, path: null, Path.GetFileName(inputPath), isExplicitSolutionProject: false, isContainer: true);
		containerNodes.Add(containerNode);
		foreach (GraphNodeModel node in explicitProjectNodes)
		{
			AddEdge(containerNode.Id, node.Id, ContainsCategory, edges, edgeKeys);
		}
	}

	private static GraphNodeModel GetOrCreateProjectNode(
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
			CreateDeterministicProjectNodeId(projectPath),
			projectPath,
			Path.GetFileName(projectPath),
			isExplicitSolutionProject,
			isContainer: false);
		nodesByPath.Add(projectPath, created);
		return created;
	}

	private static GraphNodeModel GetOrCreateContainerNode(
		IDictionary<string, GraphNodeModel> nodesByPath,
		ICollection<GraphNodeModel> containerNodes,
		string containerPath)
	{
		if (nodesByPath.TryGetValue(containerPath, out GraphNodeModel? existing))
		{
			return existing;
		}

		GraphNodeModel created = new(
			CreateDeterministicContainerNodeId(containerPath),
			containerPath,
			GetContainerLabel(containerPath),
			isExplicitSolutionProject: false,
			isContainer: true);
		nodesByPath.Add(containerPath, created);
		containerNodes.Add(created);
		return created;
	}

	private static void AddEdge(
		string sourceId,
		string targetId,
		string category,
		ICollection<GraphEdgeModel> edges,
		ISet<(string SourceId, string TargetId, string Category)> edgeKeys)
	{
		if (sourceId == targetId)
		{
			return;
		}

		if (edgeKeys.Add((sourceId, targetId, category)))
		{
			edges.Add(new GraphEdgeModel(sourceId, targetId, category));
		}
	}

	private static string? FindContainingPath(string path, IEnumerable<string> candidatePaths, bool allowExactMatch)
	{
		string? bestMatch = null;
		foreach (string candidatePath in candidatePaths)
		{
			if (!PathMatchesPrefix(path, candidatePath, allowExactMatch))
			{
				continue;
			}

			if (bestMatch is null || candidatePath.Length > bestMatch.Length)
			{
				bestMatch = candidatePath;
			}
		}

		return bestMatch;
	}

	private static string CreateDeterministicProjectNodeId(string projectPath)
		=> CreateDeterministicNodeId("project", projectPath);

	private static string CreateDeterministicContainerNodeId(string containerPath)
		=> CreateDeterministicNodeId("container", containerPath);

	private static string CreateDeterministicNodeId(string prefix, string value)
	{
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(value));
		return $"{prefix}:{Convert.ToHexString(hash)}";
	}

	private static string GetContainerLabel(string containerPath)
	{
		string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(containerPath));
		return string.IsNullOrEmpty(label) ? containerPath : label;
	}

	private static XDocument CreateDgml(GraphModel graphModel)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		XElement nodesElement = new(ns + "Nodes");
		foreach (GraphNodeModel node in graphModel.Nodes)
		{
			XElement nodeElement = new(
				ns + "Node",
				new XAttribute("Id", node.Id),
				new XAttribute("Label", node.Label));
			if (node.Path is not null)
			{
				nodeElement.Add(new XAttribute("Path", node.Path));
			}

			if (node.IsContainer)
			{
				nodeElement.Add(new XAttribute("Group", "Expanded"));
			}

			nodesElement.Add(nodeElement);
		}

		XElement linksElement = new(ns + "Links");
		foreach (GraphEdgeModel edge in graphModel.Edges)
		{
			linksElement.Add(new XElement(
			ns + "Link",
			new XAttribute("Source", edge.SourceId),
			new XAttribute("Target", edge.TargetId),
			new XAttribute("Category", edge.Category)));
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
						new XAttribute("Id", ContainsCategory),
						new XAttribute("Label", "Contains"),
						new XAttribute("IsContainment", "True")),
					new XElement(
						ns + "Category",
						new XAttribute("Id", ProjectReferenceCategory),
						new XAttribute("Label", "Project Reference"))),
				nodesElement,
				linksElement));
	}

	private static HashSet<string> NormalizePathsRelativeTo(string baseDirectory, IEnumerable<string>? paths)
	{
		HashSet<string> normalizedPaths = new(StringComparer.OrdinalIgnoreCase);
		if (paths is null)
		{
			return normalizedPaths;
		}

		foreach (string path in paths)
		{
			normalizedPaths.Add(NormalizePathRelativeTo(baseDirectory, path));
		}

		return normalizedPaths;
	}

	private static bool IsExcludedProjectPath(string projectPath, IReadOnlySet<string> excludedProjectPaths)
	{
		foreach (string excludedProjectPath in excludedProjectPaths)
		{
			if (IsPathEqualOrDescendantOf(projectPath, excludedProjectPath))
			{
				return true;
			}
		}

		return false;
	}

	private static string NormalizePathRelativeTo(string baseDirectory, string path)
		=> NormalizePath(Path.IsPathRooted(path) ? path : Path.Combine(baseDirectory, path));

	private static string NormalizePath(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

	private static bool IsPathEqualOrDescendantOf(string path, string candidatePath)
		=> path.Equals(candidatePath, StringComparison.OrdinalIgnoreCase)
		|| IsPathDescendantOf(path, candidatePath);

	private static bool PathMatchesPrefix(string path, string candidatePath, bool allowExactMatch)
		=> (allowExactMatch && path.Equals(candidatePath, StringComparison.OrdinalIgnoreCase))
		|| IsPathDescendantOf(path, candidatePath);

	private static bool IsPathDescendantOf(string path, string candidatePath)
		=> path.Length > candidatePath.Length
		&& path.StartsWith(candidatePath, StringComparison.OrdinalIgnoreCase)
		&& IsDirectorySeparator(path[candidatePath.Length]);

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
		public GraphEdgeModel(string sourceId, string targetId, string category)
		{
			this.SourceId = sourceId;
			this.TargetId = targetId;
			this.Category = category;
		}

		public string SourceId { get; }

		public string TargetId { get; }

		public string Category { get; }
	}

	private sealed class GraphNodeModel(string id, string? path, string label, bool isExplicitSolutionProject, bool isContainer)
	{
		public string Id { get; } = id;

		public string? Path { get; } = path;

		public string Label { get; } = label;

		public bool IsContainer { get; } = isContainer;

		public bool IsExplicitSolutionProject { get; set; } = isExplicitSolutionProject;
	}
}
