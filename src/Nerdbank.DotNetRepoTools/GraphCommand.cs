// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.Build.Graph;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

namespace Nerdbank.DotNetRepoTools;

/// <summary>
/// Builds an MSBuild project graph and writes it to a file.
/// </summary>
public class GraphCommand : MSBuildCommandBase
{
	private const string ContainsCategory = "Contains";
	private const string ProjectReferenceCategory = "ProjectReference";
	private const string HighlightProjectCategoryPrefix = "HighlightProject";
	private const string SlnxExplicitProjectsContainerId = "solution:explicit-projects";
	private static readonly IReadOnlyList<GraphHighlightStyleModel> HighlightStyles =
	[
		new("#DBEAFE", "#2563EB", "#172554"),
		new("#DCFCE7", "#16A34A", "#052E16"),
		new("#FCE7F3", "#DB2777", "#500724"),
		new("#FEF3C7", "#D97706", "#451A03"),
		new("#EDE9FE", "#7C3AED", "#2E1065"),
		new("#CCFBF1", "#0F766E", "#042F2E"),
		new("#FEE2E2", "#DC2626", "#450A0A"),
		new("#FFEDD5", "#EA580C", "#431407"),
	];

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

	/// <summary>
	/// Specifies the output format for the rendered graph.
	/// </summary>
	public enum GraphOutputFormat
	{
		/// <summary>
		/// Writes the graph as DGML.
		/// </summary>
		Dgml,

		/// <summary>
		/// Writes the graph as a Mermaid flowchart.
		/// </summary>
		Mermaid,
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
	/// Gets the output file path.
	/// </summary>
	public string? OutputPath { get; init; }

	/// <summary>
	/// Gets the output format.
	/// </summary>
	public GraphOutputFormat OutputFormat { get; init; }

	/// <summary>
	/// Gets a value indicating whether the output format was explicitly specified.
	/// </summary>
	public bool IsOutputFormatSpecified { get; init; }

	/// <summary>
	/// Gets the glob patterns for project files that should be omitted from the rendered graph.
	/// Relative patterns are resolved against the current working directory.
	/// </summary>
	public string[]? ExcludedProjectPaths { get; init; }

	/// <summary>
	/// Gets the directory paths that should be emitted as group containers.
	/// Relative paths are resolved against the current working directory.
	/// </summary>
	public string[]? GroupPaths { get; init; }

	/// <summary>
	/// Gets the globbing patterns used to assign projects to highlight categories.
	/// Relative patterns are resolved against the current working directory.
	/// </summary>
	public string[]? HighlightProjectPatterns { get; init; }

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
			Description = "The output file to write. Defaults to the input path with an extension that matches the selected format.",
		};
		Option<GraphOutputFormat> formatOption = new("--format", "-f")
		{
			Description = "The output format to write. Supported values include Dgml and Mermaid.",
		};
		Option<string[]> excludeOption = new("--exclude", "-e")
		{
			Description = "One or more glob patterns matched against project paths to omit from the rendered graph. Relative patterns are resolved against the current working directory.",
			AllowMultipleArgumentsPerToken = true,
		};
		Option<string[]> groupOption = new("--group", "-g")
		{
			Description = "One or more directory paths that should become group containers. Relative paths are resolved against the current working directory.",
			AllowMultipleArgumentsPerToken = true,
		};
		Option<string[]> highlightProjectsOption = new("--highlight-projects", "-s")
		{
			Description = "One or more glob patterns matched against project paths. Each pattern assigns matching projects to a distinct highlight category. Relative patterns are resolved against the current working directory.",
			AllowMultipleArgumentsPerToken = true,
		};

		Command command = new("graph", "Builds an MSBuild project graph and writes it as DGML or Mermaid.")
		{
			inputArgument,
			outputArgument,
			formatOption,
			excludeOption,
			groupOption,
			highlightProjectsOption,
		};
		command.SetAction((parseResult, cancellationToken) => new GraphCommand(parseResult, cancellationToken)
		{
			InputPath = parseResult.GetValue(inputArgument)!,
			OutputPath = parseResult.GetValue(outputArgument),
			OutputFormat = parseResult.GetValue(formatOption),
			IsOutputFormatSpecified = parseResult.CommandResult.Children.OfType<System.CommandLine.Parsing.OptionResult>().Any(optionResult => ReferenceEquals(optionResult.Option, formatOption)),
			ExcludedProjectPaths = parseResult.GetValue(excludeOption),
			GroupPaths = parseResult.GetValue(groupOption),
			HighlightProjectPatterns = parseResult.GetValue(highlightProjectsOption),
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

		GraphOutputFormat outputFormat = ResolveOutputFormat(this.OutputPath, this.OutputFormat, this.IsOutputFormatSpecified);
		string outputPath = this.OutputPath is not null
			? Path.GetFullPath(this.OutputPath)
			: Path.ChangeExtension(fullInputPath, GetDefaultFileExtension(outputFormat));
		HashSet<string> groupingPaths = NormalizePathsRelativeTo(Environment.CurrentDirectory, this.GroupPaths);
		IReadOnlyList<Regex> excludedProjectPathPatterns = CreatePathGlobPatternsRelativeTo(Environment.CurrentDirectory, this.ExcludedProjectPaths);
		IReadOnlyList<ProjectHighlightRuleModel> highlightRules = CreateProjectHighlightRulesRelativeTo(Environment.CurrentDirectory, this.HighlightProjectPatterns);

		try
		{
			GraphInput graphInput = await LoadGraphInputAsync(fullInputPath, excludedProjectPathPatterns, this.CancellationToken);
			GraphModel graphModel = graphInput.EntryPoints.Count > 0
				? BuildGraphModel(new ProjectGraph(graphInput.EntryPoints), graphInput.ExplicitSolutionProjects, excludedProjectPathPatterns, groupingPaths, highlightRules, fullInputPath, graphInput.InputKind)
				: new GraphModel([], [], []);

			Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);
			WriteGraph(outputPath, graphModel, outputFormat);
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

	private static async Task<GraphInput> LoadGraphInputAsync(string inputPath, IReadOnlyList<Regex> excludedProjectPathPatterns, CancellationToken cancellationToken)
	{
		string extension = Path.GetExtension(inputPath);
		if (!extension.Equals(".sln", StringComparison.OrdinalIgnoreCase)
			&& !extension.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
		{
			return new GraphInput(
			InputKind.Project,
			IsExcludedProjectPath(inputPath, excludedProjectPathPatterns) ? [] : [new ProjectGraphEntryPoint(inputPath)],
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
			if (IsExcludedProjectPath(projectPath, excludedProjectPathPatterns))
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

	private static GraphModel BuildGraphModel(ProjectGraph projectGraph, HashSet<string> explicitSolutionProjects, IReadOnlyList<Regex> excludedProjectPathPatterns, IReadOnlySet<string> groupingPaths, IReadOnlyList<ProjectHighlightRuleModel> highlightRules, string inputPath, InputKind inputKind)
	{
		Dictionary<string, GraphNodeModel> projectNodesByPath = new(StringComparer.OrdinalIgnoreCase);
		List<GraphNodeModel> containerNodes = [];
		Dictionary<string, GraphNodeModel> groupingContainerNodesByPath = new(StringComparer.OrdinalIgnoreCase);
		HashSet<(string SourceId, string TargetId, string Category)> edgeKeys = [];
		List<GraphEdgeModel> edges = [];

		foreach (ProjectGraphNode graphNode in projectGraph.ProjectNodes)
		{
			string sourcePath = NormalizePath(graphNode.ProjectInstance.FullPath);
			if (IsExcludedProjectPath(sourcePath, excludedProjectPathPatterns))
			{
				continue;
			}

			GetOrCreateProjectNode(projectNodesByPath, sourcePath, explicitSolutionProjects.Contains(sourcePath), FindHighlightCategoryId(sourcePath, highlightRules));

			foreach (ProjectGraphNode projectReference in graphNode.ProjectReferences)
			{
				string targetPath = NormalizePath(projectReference.ProjectInstance.FullPath);
				if (IsExcludedProjectPath(targetPath, excludedProjectPathPatterns))
				{
					continue;
				}

				GetOrCreateProjectNode(projectNodesByPath, targetPath, explicitSolutionProjects.Contains(targetPath), FindHighlightCategoryId(targetPath, highlightRules));
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
				.ToList(),
			highlightRules.Select(rule => rule.Category).ToList());
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
		GraphNodeModel containerNode = new(SlnxExplicitProjectsContainerId, path: null, Path.GetFileName(inputPath), isExplicitSolutionProject: false, isContainer: true, highlightCategoryId: null);
		containerNodes.Add(containerNode);
		foreach (GraphNodeModel node in explicitProjectNodes)
		{
			AddEdge(containerNode.Id, node.Id, ContainsCategory, edges, edgeKeys);
		}
	}

	private static GraphNodeModel GetOrCreateProjectNode(
		IDictionary<string, GraphNodeModel> nodesByPath,
		string projectPath,
		bool isExplicitSolutionProject,
		string? highlightCategoryId)
	{
		if (nodesByPath.TryGetValue(projectPath, out GraphNodeModel? existing))
		{
			if (isExplicitSolutionProject)
			{
				existing.IsExplicitSolutionProject = true;
			}

			if (highlightCategoryId is not null)
			{
				existing.HighlightCategoryId = highlightCategoryId;
			}

			return existing;
		}

		GraphNodeModel created = new(
			CreateDeterministicProjectNodeId(projectPath),
			projectPath,
			Path.GetFileName(projectPath),
			isExplicitSolutionProject,
			isContainer: false,
			highlightCategoryId);
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
			isContainer: true,
			highlightCategoryId: null);
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
		string relativePath = Path.GetRelativePath(Environment.CurrentDirectory, value);
		string normalizedRelativePath = relativePath
			.Replace(Path.DirectorySeparatorChar, '/')
			.Replace(Path.AltDirectorySeparatorChar, '/');
		byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedRelativePath));
		return $"{prefix}:{Convert.ToHexString(hash)}";
	}

	private static string GetContainerLabel(string containerPath)
	{
		string label = Path.GetFileName(Path.TrimEndingDirectorySeparator(containerPath));
		return string.IsNullOrEmpty(label) ? containerPath : label;
	}

	private static GraphOutputFormat ResolveOutputFormat(string? outputPath, GraphOutputFormat outputFormat, bool isOutputFormatSpecified)
	{
		if (isOutputFormatSpecified || string.IsNullOrEmpty(outputPath))
		{
			return outputFormat;
		}

		return TryGetOutputFormatFromFileExtension(outputPath, out GraphOutputFormat inferredFormat)
			? inferredFormat
			: outputFormat;
	}

	private static bool TryGetOutputFormatFromFileExtension(string outputPath, out GraphOutputFormat outputFormat)
	{
		string extension = Path.GetExtension(outputPath);
		if (extension.Equals(".dgml", StringComparison.OrdinalIgnoreCase))
		{
			outputFormat = GraphOutputFormat.Dgml;
			return true;
		}

		if (extension.Equals(".mmd", StringComparison.OrdinalIgnoreCase)
			|| extension.Equals(".mermaid", StringComparison.OrdinalIgnoreCase))
		{
			outputFormat = GraphOutputFormat.Mermaid;
			return true;
		}

		outputFormat = default;
		return false;
	}

	private static string GetDefaultFileExtension(GraphOutputFormat outputFormat)
		=> outputFormat switch
		{
			GraphOutputFormat.Dgml => ".dgml",
			GraphOutputFormat.Mermaid => ".mmd",
			_ => throw new InvalidOperationException($"Unsupported graph output format '{outputFormat}'."),
		};

	private static void WriteGraph(string outputPath, GraphModel graphModel, GraphOutputFormat outputFormat)
	{
		switch (outputFormat)
		{
			case GraphOutputFormat.Dgml:
				CreateDgml(graphModel).Save(outputPath);
				break;
			case GraphOutputFormat.Mermaid:
				File.WriteAllText(outputPath, CreateMermaidFlowchart(graphModel));
				break;
			default:
				throw new InvalidOperationException($"Unsupported graph output format '{outputFormat}'.");
		}
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

			if (node.HighlightCategoryId is not null)
			{
				nodeElement.Add(new XAttribute("Category", node.HighlightCategoryId));
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

		List<object> directedGraphChildren =
		[
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
					new XAttribute("Label", "Project Reference")),
				graphModel.HighlightCategories.Select(category =>
					new XElement(
						ns + "Category",
						new XAttribute("Id", category.Id),
						new XAttribute("Label", category.Label)))),
		];
		if (graphModel.HighlightCategories.Count > 0)
		{
			directedGraphChildren.Add(
				new XElement(
					ns + "Styles",
					graphModel.HighlightCategories.Select(category =>
						new XElement(
							ns + "Style",
							new XAttribute("TargetType", "Node"),
							new XAttribute("GroupLabel", "Project Highlights"),
							new XAttribute("ValueLabel", category.Label),
							new XElement(
								ns + "Condition",
								new XAttribute("Expression", $"HasCategory('{category.Id}')")),
							new XElement(
								ns + "Setter",
								new XAttribute("Property", "Background"),
								new XAttribute("Value", category.Style.Background)),
							new XElement(
								ns + "Setter",
								new XAttribute("Property", "Stroke"),
								new XAttribute("Value", category.Style.Stroke)),
							new XElement(
								ns + "Setter",
								new XAttribute("Property", "Foreground"),
								new XAttribute("Value", category.Style.Foreground))))));
		}

		directedGraphChildren.Add(nodesElement);
		directedGraphChildren.Add(linksElement);

		return new XDocument(
			new XDeclaration("1.0", "utf-8", "yes"),
			new XElement(
				ns + "DirectedGraph",
				new XAttribute("GraphDirection", "LeftToRight"),
				directedGraphChildren));
	}

	private static string CreateMermaidFlowchart(GraphModel graphModel)
	{
		StringBuilder builder = new();
		builder.AppendLine("flowchart TD");

		IReadOnlyDictionary<string, GraphNodeModel> nodesById = graphModel.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);
		Dictionary<string, int> orderById = graphModel.Nodes
			.Select((node, index) => new KeyValuePair<string, int>(node.Id, index))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		Dictionary<string, string> mermaidIdsByGraphId = graphModel.Nodes
			.Select((node, index) => new KeyValuePair<string, string>(node.Id, $"n{index + 1}"))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
		ILookup<string, string> containedChildrenBySource = graphModel.Edges
			.Where(edge => edge.Category == ContainsCategory)
			.ToLookup(edge => edge.SourceId, edge => edge.TargetId, StringComparer.Ordinal);
		HashSet<string> containedNodeIds = graphModel.Edges
			.Where(edge => edge.Category == ContainsCategory)
			.Select(edge => edge.TargetId)
			.ToHashSet(StringComparer.Ordinal);
		HashSet<string> emptyContainerPlaceholderIds = [];

		foreach (GraphNodeModel rootNode in graphModel.Nodes.Where(node => !containedNodeIds.Contains(node.Id)))
		{
			AppendMermaidNode(builder, rootNode, nodesById, mermaidIdsByGraphId, containedChildrenBySource, orderById, emptyContainerPlaceholderIds, indentationLevel: 1);
		}

		foreach (GraphEdgeModel edge in graphModel.Edges.Where(edge => edge.Category == ProjectReferenceCategory))
		{
			builder.Append("    ");
			builder.Append(mermaidIdsByGraphId[edge.SourceId]);
			builder.Append(" --> ");
			builder.Append(mermaidIdsByGraphId[edge.TargetId]);
			builder.AppendLine();
		}

		AppendMermaidHighlightStyles(builder, graphModel, mermaidIdsByGraphId);
		if (emptyContainerPlaceholderIds.Count > 0)
		{
			builder.AppendLine("    classDef hidden fill:transparent,stroke:transparent,color:transparent;");
			builder.Append("    class ");
			builder.Append(string.Join(',', emptyContainerPlaceholderIds.OrderBy(id => id, StringComparer.Ordinal)));
			builder.AppendLine(" hidden;");
		}

		return builder.ToString();
	}

	private static void AppendMermaidNode(
		StringBuilder builder,
		GraphNodeModel node,
		IReadOnlyDictionary<string, GraphNodeModel> nodesById,
		IReadOnlyDictionary<string, string> mermaidIdsByGraphId,
		ILookup<string, string> containedChildrenBySource,
		IReadOnlyDictionary<string, int> orderById,
		ISet<string> emptyContainerPlaceholderIds,
		int indentationLevel)
	{
		string indentation = new(' ', indentationLevel * 4);
		string mermaidId = mermaidIdsByGraphId[node.Id];
		if (node.IsContainer)
		{
			builder.Append(indentation);
			builder.Append("subgraph ");
			builder.Append(mermaidId);
			builder.Append("[\"");
			builder.Append(EscapeMermaidLabel(node.Label));
			builder.AppendLine("\"]");

			List<GraphNodeModel> children = containedChildrenBySource[node.Id]
				.Select(childId => nodesById[childId])
				.OrderBy(child => orderById[child.Id])
				.ToList();
			if (children.Count == 0)
			{
				string placeholderId = $"{mermaidId}_empty";
				emptyContainerPlaceholderIds.Add(placeholderId);
				builder.Append(indentation);
				builder.Append("    ");
				builder.Append(placeholderId);
				builder.AppendLine("[\"\"]");
			}
			else
			{
				foreach (GraphNodeModel child in children)
				{
					AppendMermaidNode(builder, child, nodesById, mermaidIdsByGraphId, containedChildrenBySource, orderById, emptyContainerPlaceholderIds, indentationLevel + 1);
				}
			}

			builder.Append(indentation);
			builder.AppendLine("end");
		}
		else
		{
			builder.Append(indentation);
			builder.Append(mermaidId);
			builder.Append("[\"");
			builder.Append(EscapeMermaidLabel(node.Label));
			builder.AppendLine("\"]");
		}
	}

	private static void AppendMermaidHighlightStyles(StringBuilder builder, GraphModel graphModel, IReadOnlyDictionary<string, string> mermaidIdsByGraphId)
	{
		IReadOnlyDictionary<string, string> cssClassesByCategoryId = graphModel.HighlightCategories
			.Select((category, index) => new KeyValuePair<string, string>(category.Id, $"highlight{index + 1}"))
			.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);

		foreach (GraphHighlightCategoryModel category in graphModel.HighlightCategories)
		{
			builder.Append("    classDef ");
			builder.Append(cssClassesByCategoryId[category.Id]);
			builder.Append(" fill:");
			builder.Append(category.Style.Background);
			builder.Append(",stroke:");
			builder.Append(category.Style.Stroke);
			builder.Append(",color:");
			builder.Append(category.Style.Foreground);
			builder.AppendLine(";");
		}

		foreach (IGrouping<string, GraphNodeModel> nodesByClass in graphModel.Nodes
			.Where(node => node.HighlightCategoryId is not null)
			.GroupBy(node => cssClassesByCategoryId[node.HighlightCategoryId!], StringComparer.Ordinal)
			.OrderBy(group => group.Key, StringComparer.Ordinal))
		{
			builder.Append("    class ");
			builder.Append(string.Join(',', nodesByClass.Select(node => mermaidIdsByGraphId[node.Id])));
			builder.Append(' ');
			builder.Append(nodesByClass.Key);
			builder.AppendLine(";");
		}
	}

	private static string EscapeMermaidLabel(string value)
		=> value
			.Replace("\\", "\\\\", StringComparison.Ordinal)
			.Replace("\"", "\\\"", StringComparison.Ordinal)
			.Replace("\r", " ", StringComparison.Ordinal)
			.Replace("\n", " ", StringComparison.Ordinal);

	private static IReadOnlyList<ProjectHighlightRuleModel> CreateProjectHighlightRulesRelativeTo(string baseDirectory, IEnumerable<string>? patterns)
	{
		if (patterns is null)
		{
			return [];
		}

		List<ProjectHighlightRuleModel> rules = [];
		int index = 0;
		foreach (Regex pattern in CreatePathGlobPatternsRelativeTo(baseDirectory, patterns))
		{
			GraphHighlightStyleModel style = HighlightStyles[index % HighlightStyles.Count];
			GraphHighlightCategoryModel category = new(
				$"{HighlightProjectCategoryPrefix}:{index + 1}",
				$"Project Highlight {index + 1}",
				style);
			rules.Add(new ProjectHighlightRuleModel(pattern, category));
			index++;
		}

		return rules;
	}

	private static IReadOnlyList<Regex> CreatePathGlobPatternsRelativeTo(string baseDirectory, IEnumerable<string>? patterns)
	{
		if (patterns is null)
		{
			return [];
		}

		List<Regex> rules = [];
		foreach (string pattern in patterns)
		{
			rules.Add(CreateGlobRegex(NormalizeGlobPatternRelativeTo(baseDirectory, pattern)));
		}

		return rules;
	}

	private static string? FindHighlightCategoryId(string projectPath, IReadOnlyList<ProjectHighlightRuleModel> highlightRules)
	{
		string comparablePath = NormalizeGlobComparablePath(projectPath);
		foreach (ProjectHighlightRuleModel rule in highlightRules)
		{
			if (rule.Pattern.IsMatch(comparablePath))
			{
				return rule.Category.Id;
			}
		}

		return null;
	}

	private static Regex CreateGlobRegex(string pattern)
	{
		StringBuilder regexBuilder = new("^");
		for (int i = 0; i < pattern.Length; i++)
		{
			char current = pattern[i];
			if (current == '*')
			{
				bool isDoubleStar = i + 1 < pattern.Length && pattern[i + 1] == '*';
				if (isDoubleStar)
				{
					bool isDoubleStarDirectory = i + 2 < pattern.Length && pattern[i + 2] == '/';
					regexBuilder.Append(isDoubleStarDirectory ? "(?:.*/)?" : ".*");
					i += isDoubleStarDirectory ? 2 : 1;
				}
				else
				{
					regexBuilder.Append("[^/]*");
				}
			}
			else if (current == '?')
			{
				regexBuilder.Append("[^/]");
			}
			else
			{
				regexBuilder.Append(Regex.Escape(current.ToString()));
			}
		}

		regexBuilder.Append('$');
		return new Regex(regexBuilder.ToString(), RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
	}

	private static string NormalizeGlobPatternRelativeTo(string baseDirectory, string pattern)
	{
		string normalizedPattern = pattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		if (!Path.IsPathRooted(normalizedPattern) && StartsWithRecursiveDirectoryGlob(normalizedPattern))
		{
			return normalizedPattern.Replace(Path.DirectorySeparatorChar, '/');
		}

		string rootedPattern = Path.IsPathRooted(normalizedPattern) ? normalizedPattern : Path.Combine(baseDirectory, normalizedPattern);
		string normalizedSeparators = rootedPattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
		string root = Path.GetPathRoot(normalizedSeparators)!;
		string[] segments = normalizedSeparators[root.Length..].Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);
		int firstWildcardSegment = Array.FindIndex(segments, segment => segment.Contains('*') || segment.Contains('?'));
		if (firstWildcardSegment < 0)
		{
			return NormalizeGlobComparablePath(normalizedSeparators);
		}

		string fixedPrefix = firstWildcardSegment == 0
			? root
			: Path.Combine(root, Path.Combine(segments[..firstWildcardSegment]));
		string normalizedPrefix = NormalizeGlobComparablePath(fixedPrefix);
		string wildcardSuffix = string.Join('/', segments[firstWildcardSegment..].Select(segment => segment.Replace(Path.AltDirectorySeparatorChar, '/').Replace(Path.DirectorySeparatorChar, '/')));
		string separator = normalizedPrefix.EndsWith("/", StringComparison.Ordinal) ? string.Empty : "/";
		return string.IsNullOrEmpty(wildcardSuffix) ? normalizedPrefix : $"{normalizedPrefix}{separator}{wildcardSuffix}";
	}

	private static bool StartsWithRecursiveDirectoryGlob(string pattern)
		=> pattern.Equals("**", StringComparison.Ordinal)
		|| pattern.StartsWith($"**{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

	private static string NormalizeGlobComparablePath(string path)
		=> NormalizePath(path).Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');

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

	private static bool IsExcludedProjectPath(string projectPath, IReadOnlyList<Regex> excludedProjectPathPatterns)
	{
		string comparablePath = NormalizeGlobComparablePath(projectPath);
		foreach (Regex excludedProjectPathPattern in excludedProjectPathPatterns)
		{
			if (excludedProjectPathPattern.IsMatch(comparablePath))
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
		=> allowExactMatch
			? IsPathEqualOrDescendantOf(path, candidatePath)
			: IsPathDescendantOf(path, candidatePath);

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
		public GraphModel(IReadOnlyList<GraphNodeModel> nodes, IReadOnlyList<GraphEdgeModel> edges, IReadOnlyList<GraphHighlightCategoryModel> highlightCategories)
		{
			this.Nodes = nodes;
			this.Edges = edges;
			this.HighlightCategories = highlightCategories;
		}

		public IReadOnlyList<GraphNodeModel> Nodes { get; }

		public IReadOnlyList<GraphEdgeModel> Edges { get; }

		public IReadOnlyList<GraphHighlightCategoryModel> HighlightCategories { get; }
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

	private sealed class GraphNodeModel(string id, string? path, string label, bool isExplicitSolutionProject, bool isContainer, string? highlightCategoryId)
	{
		public string Id { get; } = id;

		public string? Path { get; } = path;

		public string Label { get; } = label;

		public bool IsContainer { get; } = isContainer;

		public bool IsExplicitSolutionProject { get; set; } = isExplicitSolutionProject;

		public string? HighlightCategoryId { get; set; } = highlightCategoryId;
	}

	private sealed class ProjectHighlightRuleModel(Regex pattern, GraphHighlightCategoryModel category)
	{
		public Regex Pattern { get; } = pattern;

		public GraphHighlightCategoryModel Category { get; } = category;
	}

	private sealed class GraphHighlightCategoryModel(string id, string label, GraphHighlightStyleModel style)
	{
		public string Id { get; } = id;

		public string Label { get; } = label;

		public GraphHighlightStyleModel Style { get; } = style;
	}

	private sealed class GraphHighlightStyleModel(string background, string stroke, string foreground)
	{
		public string Background { get; } = background;

		public string Stroke { get; } = stroke;

		public string Foreground { get; } = foreground;
	}
}
