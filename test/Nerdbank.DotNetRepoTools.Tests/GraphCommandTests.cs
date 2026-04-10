// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.CommandLine;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

public class GraphCommandTests : CommandTestBase<GraphCommand>
{
	public GraphCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public async Task WritesDgmlForProjectInput()
	{
		(string projectPath, _) = await this.CreateProjectGraphAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph.dgml");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Equal(2, document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node").Count());
		Assert.Single(document.Root.Element(ns + "Links")!.Elements(ns + "Link"));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Label") == "App.csproj");
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Label") == "Lib.csproj");
		Assert.Contains("Wrote 2 node(s) and 1 edge(s)", ((StringWriter)this.Command.Out).ToString());
	}

	[Fact]
	public async Task WritesDgmlForSolutionInput()
	{
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync();
		string solutionPath = Path.Combine(this.StagingDirectory, "Repo.sln");
		await File.WriteAllTextAsync(
			solutionPath,
			CreateSolutionFileContent(solutionPath, projectPath, referencedProjectPath),
			TestContext.Current.CancellationToken);
		string outputPath = Path.Combine(this.StagingDirectory, "solution.dgml");
		this.Command = new()
		{
			InputPath = solutionPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Equal(2, document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node").Count());
		Assert.Single(document.Root.Element(ns + "Links")!.Elements(ns + "Link"), link => (string?)link.Attribute("Category") == "ProjectReference");
		Assert.DoesNotContain(document.Root.Element(ns + "Links")!.Elements(ns + "Link"), link => (string?)link.Attribute("Category") == "Contains");
	}

	[Fact]
	public async Task WritesDeterministicDgmlWhenSolutionProjectOrderChanges()
	{
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync();
		string firstSolutionPath = Path.Combine(this.StagingDirectory, "RepoA.sln");
		string secondSolutionPath = Path.Combine(this.StagingDirectory, "RepoB.sln");
		await File.WriteAllTextAsync(
			firstSolutionPath,
			CreateSolutionFileContent(firstSolutionPath, projectPath, referencedProjectPath, reverseProjectOrder: false),
			TestContext.Current.CancellationToken);
		await File.WriteAllTextAsync(
			secondSolutionPath,
			CreateSolutionFileContent(secondSolutionPath, projectPath, referencedProjectPath, reverseProjectOrder: true),
			TestContext.Current.CancellationToken);

		string firstOutputPath = Path.Combine(this.StagingDirectory, "RepoA.dgml");
		this.Command = new()
		{
			InputPath = firstSolutionPath,
			OutputPath = firstOutputPath,
		};

		await this.ExecuteCommandAsync();
		Assert.Equal(0, this.Command.ExitCode);

		string secondOutputPath = Path.Combine(this.StagingDirectory, "RepoB.dgml");
		this.Command = new()
		{
			InputPath = secondSolutionPath,
			OutputPath = secondOutputPath,
		};

		await this.ExecuteCommandAsync();
		Assert.Equal(0, this.Command.ExitCode);

		Assert.Equal(
			await File.ReadAllTextAsync(firstOutputPath, TestContext.Current.CancellationToken),
			await File.ReadAllTextAsync(secondOutputPath, TestContext.Current.CancellationToken));
	}

	[Fact]
	public async Task WritesContainsLinksForSlnxInput()
	{
		(string projectPath, _) = await this.CreateProjectGraphAsync();
		string solutionPath = Path.Combine(this.StagingDirectory, "Repo.slnx");
		await CreateSlnxFileAsync(solutionPath, projectPath);
		string outputPath = Path.Combine(this.StagingDirectory, "solution.dgml");
		this.Command = new()
		{
			InputPath = solutionPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Equal(3, document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node").Count());
		XElement containerNode = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Id") == "solution:explicit-projects");
		Assert.Equal("Repo.slnx", (string?)containerNode.Attribute("Label"));
		Assert.Equal("Expanded", (string?)containerNode.Attribute("Group"));

		XElement containsLink = Assert.Single(document.Root.Element(ns + "Links")!.Elements(ns + "Link"), link => (string?)link.Attribute("Category") == "Contains");
		Assert.Equal("solution:explicit-projects", (string?)containsLink.Attribute("Source"));
		Assert.Equal("App.csproj", GetNodeLabelById(document, (string)containsLink.Attribute("Target")!));
		Assert.Single(document.Root.Element(ns + "Links")!.Elements(ns + "Link"), link => (string?)link.Attribute("Category") == "ProjectReference");
	}

	[Fact]
	public async Task RejectsUnsupportedInputExtension()
	{
		Directory.CreateDirectory(this.StagingDirectory);
		string inputPath = Path.Combine(this.StagingDirectory, "notes.txt");
		await File.WriteAllTextAsync(inputPath, "irrelevant", TestContext.Current.CancellationToken);
		this.Command = new()
		{
			InputPath = inputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(1, this.Command.ExitCode);
		Assert.Contains("Unsupported input type '.txt'", ((StringWriter)this.Command.Error).ToString());
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_OmitsExcludedProjectReferenceUsingWorkingDirectoryRelativePath()
	{
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string workingDirectory = Path.Combine(this.StagingDirectory, "cwd");
		Directory.CreateDirectory(workingDirectory);

		try
		{
			Environment.CurrentDirectory = workingDirectory;
			this.Command = new()
			{
				InputPath = projectPath,
				OutputPath = outputPath,
				ExcludedProjectPaths = [Path.GetRelativePath(workingDirectory, referencedProjectPath)],
			};

			await this.ExecuteCommandAsync();
		}
		finally
		{
			Environment.CurrentDirectory = originalCurrentDirectory;
		}

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Single(document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node"));
		Assert.Empty(document.Root.Element(ns + "Links")!.Elements(ns + "Link"));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Label") == "App.csproj");
		Assert.DoesNotContain(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Label") == "Lib.csproj");
		Assert.Contains("Wrote 1 node(s) and 0 edge(s)", ((StringWriter)this.Command.Out).ToString());
	}

	[Fact]
	public async Task WritesDgmlForSolutionInput_OmitsExcludedProjectsUsingAbsoluteAndWorkingDirectoryRelativePaths()
	{
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync();
		string solutionPath = Path.Combine(this.StagingDirectory, "Repo.sln");
		await File.WriteAllTextAsync(
			solutionPath,
			CreateSolutionFileContent(solutionPath, projectPath, referencedProjectPath),
			TestContext.Current.CancellationToken);
		string outputPath = Path.Combine(this.StagingDirectory, "solution.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string workingDirectory = Path.Combine(this.StagingDirectory, "cwd");
		Directory.CreateDirectory(workingDirectory);

		try
		{
			Environment.CurrentDirectory = workingDirectory;
			this.Command = new()
			{
				InputPath = solutionPath,
				OutputPath = outputPath,
				ExcludedProjectPaths = [projectPath, Path.GetRelativePath(workingDirectory, referencedProjectPath)],
			};

			await this.ExecuteCommandAsync();
		}
		finally
		{
			Environment.CurrentDirectory = originalCurrentDirectory;
		}

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Empty(document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node"));
		Assert.Empty(document.Root.Element(ns + "Links")!.Elements(ns + "Link"));
		Assert.Contains("Wrote 0 node(s) and 0 edge(s)", ((StringWriter)this.Command.Out).ToString());
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_OmitsProjectsMatchingExcludeGlobButNotSiblingPath()
	{
		(string projectPath, string excludedProjectPath, string includedProjectPath) = await this.CreateProjectGraphWithSimilarPrefixPathsAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph-prefix.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string workingDirectory = this.StagingDirectory;

		try
		{
			Environment.CurrentDirectory = workingDirectory;
			this.Command = new()
			{
				InputPath = projectPath,
				OutputPath = outputPath,
				ExcludedProjectPaths = [Path.Combine("subpath", "**", "*.csproj")],
			};

			await this.ExecuteCommandAsync();
		}
		finally
		{
			Environment.CurrentDirectory = originalCurrentDirectory;
		}

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Equal(2, document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node").Count());
		Assert.Single(document.Root.Element(ns + "Links")!.Elements(ns + "Link"));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.GetFullPath(projectPath));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.GetFullPath(includedProjectPath));
		Assert.DoesNotContain(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.GetFullPath(excludedProjectPath));
	}

	[Fact]
	public void CreateCommand_DefinesExcludeOptionAliasAndMultipleArguments()
	{
		MethodInfo createCommandMethod = typeof(GraphCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option excludeOption = Assert.Single(command.Options, option => option.Name == "--exclude");
		Assert.Contains("-e", excludeOption.Aliases);
		Assert.True(excludeOption.AllowMultipleArgumentsPerToken);
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_GroupsProjectsUsingAbsoluteAndWorkingDirectoryRelativePaths()
	{
		(string projectPath, string parentGroupPath, string childGroupPath, string siblingProjectPath, string nestedProjectPath, string ungroupedProjectPath) = await this.CreateProjectGraphWithGroupingPathsAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph-grouped.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string workingDirectory = Path.Combine(this.StagingDirectory, "cwd");
		Directory.CreateDirectory(workingDirectory);

		try
		{
			Environment.CurrentDirectory = workingDirectory;
			this.Command = new()
			{
				InputPath = projectPath,
				OutputPath = outputPath,
				GroupPaths = [parentGroupPath, Path.GetRelativePath(workingDirectory, childGroupPath)],
			};

			await this.ExecuteCommandAsync();
		}
		finally
		{
			Environment.CurrentDirectory = originalCurrentDirectory;
		}

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Equal(6, document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node").Count());
		XElement parentContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.GetFullPath(parentGroupPath));
		Assert.Equal("Copilot", (string?)parentContainer.Attribute("Label"));
		Assert.Equal("Expanded", (string?)parentContainer.Attribute("Group"));
		XElement childContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.GetFullPath(childGroupPath));
		Assert.Equal("tests", (string?)childContainer.Attribute("Label"));
		Assert.Equal("Expanded", (string?)childContainer.Attribute("Group"));

		IReadOnlyList<XElement> containsLinks = document.Root.Element(ns + "Links")!.Elements(ns + "Link")
			.Where(link => (string?)link.Attribute("Category") == "Contains")
			.ToList();
		Assert.Equal(4, containsLinks.Count);
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && (string?)link.Attribute("Target") == (string?)childContainer.Attribute("Id"));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == Path.GetFullPath(projectPath));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == Path.GetFullPath(siblingProjectPath));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)childContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == Path.GetFullPath(nestedProjectPath));
		Assert.DoesNotContain(containsLinks, link => GetNodePathById(document, (string)link.Attribute("Target")!) == Path.GetFullPath(ungroupedProjectPath));
	}

	[Fact]
	public async Task WritesGroupContainersInsteadOfSlnxExplicitProjectsContainerWhenGroupsSpecified()
	{
		(string projectPath, string parentGroupPath, _, _, _, string ungroupedProjectPath) = await this.CreateProjectGraphWithGroupingPathsAsync();
		string solutionPath = Path.Combine(this.StagingDirectory, "Repo.slnx");
		await CreateSlnxFileAsync(solutionPath, projectPath, ungroupedProjectPath);
		string outputPath = Path.Combine(this.StagingDirectory, "solution-grouped.dgml");
		this.Command = new()
		{
			InputPath = solutionPath,
			OutputPath = outputPath,
			GroupPaths = [parentGroupPath],
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.DoesNotContain(document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Id") == "solution:explicit-projects");
		XElement parentContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.GetFullPath(parentGroupPath));

		IReadOnlyList<XElement> containsLinks = document.Root.Element(ns + "Links")!.Elements(ns + "Link")
			.Where(link => (string?)link.Attribute("Category") == "Contains")
			.ToList();
		Assert.Equal(3, containsLinks.Count);
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == Path.GetFullPath(projectPath));
		Assert.DoesNotContain(containsLinks, link => GetNodePathById(document, (string)link.Attribute("Target")!) == Path.GetFullPath(ungroupedProjectPath));
	}

	[Fact]
	public void CreateCommand_DefinesGroupOptionAliasAndMultipleArguments()
	{
		MethodInfo createCommandMethod = typeof(GraphCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option groupOption = Assert.Single(command.Options, option => option.Name == "--group");
		Assert.Contains("-g", groupOption.Aliases);
		Assert.True(groupOption.AllowMultipleArgumentsPerToken);
	}

	[Fact]
	public void CreateCommand_DefinesHighlightProjectsOptionAliasAndMultipleArguments()
	{
		MethodInfo createCommandMethod = typeof(GraphCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option highlightProjectsOption = Assert.Single(command.Options, option => option.Name == "--highlight-projects");
		Assert.Contains("-s", highlightProjectsOption.Aliases);
		Assert.True(highlightProjectsOption.AllowMultipleArgumentsPerToken);
	}

	[Fact]
	public void AddEdge_DoesNotCreateSelfReferentialEdge()
	{
		MethodInfo addEdgeMethod = typeof(GraphCommand).GetMethod("AddEdge", BindingFlags.Static | BindingFlags.NonPublic)!;
		Type graphEdgeModelType = typeof(GraphCommand).GetNestedType("GraphEdgeModel", BindingFlags.NonPublic)!;
		IList edges = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(graphEdgeModelType))!;
		HashSet<(string SourceId, string TargetId, string Category)> edgeKeys = [];

		addEdgeMethod.Invoke(obj: null, ["node:1", "node:1", "Contains", edges, edgeKeys]);

		Assert.Empty(edges.Cast<object>());
		Assert.Empty(edgeKeys);
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_HighlightsProjectsUsingGlobPatternsAndStyles()
	{
		(string projectPath, _, string childGroupPath, string siblingProjectPath, string nestedProjectPath, string ungroupedProjectPath) = await this.CreateProjectGraphWithGroupingPathsAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph-highlighted.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string workingDirectory = Path.Combine(this.StagingDirectory, "cwd");
		Directory.CreateDirectory(workingDirectory);

		try
		{
			Environment.CurrentDirectory = workingDirectory;
			this.Command = new()
			{
				InputPath = projectPath,
				OutputPath = outputPath,
				HighlightProjectPatterns =
				[
					Path.Combine("..", "src", "Copilot", "App", "*.csproj"),
					Path.Combine(Path.GetRelativePath(workingDirectory, childGroupPath), "*.csproj"),
				],
			};

			await this.ExecuteCommandAsync();
		}
		finally
		{
			Environment.CurrentDirectory = originalCurrentDirectory;
		}

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		XElement appNode = GetNodeByPath(document, projectPath);
		XElement nestedNode = GetNodeByPath(document, nestedProjectPath);
		XElement siblingNode = GetNodeByPath(document, siblingProjectPath);
		XElement ungroupedNode = GetNodeByPath(document, ungroupedProjectPath);
		XAttribute appCategoryAttribute = Assert.Single(appNode.Attributes("Category"));
		XAttribute nestedCategoryAttribute = Assert.Single(nestedNode.Attributes("Category"));
		string appCategoryId = appCategoryAttribute.Value;
		string nestedCategoryId = nestedCategoryAttribute.Value;
		Assert.NotEqual(appCategoryId, nestedCategoryId);
		Assert.Null((string?)siblingNode.Attribute("Category"));
		Assert.Null((string?)ungroupedNode.Attribute("Category"));

		IReadOnlyList<XElement> categories = document.Root!.Element(ns + "Categories")!.Elements(ns + "Category").ToList();
		Assert.Contains(categories, category => (string?)category.Attribute("Id") == appCategoryId);
		Assert.Contains(categories, category => (string?)category.Attribute("Id") == nestedCategoryId);

		IReadOnlyList<XElement> styles = document.Root.Element(ns + "Styles")!.Elements(ns + "Style").ToList();
		Assert.Equal(2, styles.Count);
		AssertProjectHighlightStyle(styles, appCategoryId);
		AssertProjectHighlightStyle(styles, nestedCategoryId);
		Assert.NotEqual(
			GetStyleSetterValue(styles, appCategoryId, "Background"),
			GetStyleSetterValue(styles, nestedCategoryId, "Background"));
	}

	private static string CreateSolutionFileContent(string solutionPath, string projectPath, string referencedProjectPath, bool reverseProjectOrder = false)
	{
		string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
		string appPathFromSolution = Path.GetRelativePath(solutionDirectory, projectPath);
		string libPathFromSolution = Path.GetRelativePath(solutionDirectory, referencedProjectPath);
		string[] projectEntries = reverseProjectOrder
			? [
				$"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Lib\", \"{libPathFromSolution}\", \"{{22222222-2222-2222-2222-222222222222}}\"",
				"EndProject",
				$"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"App\", \"{appPathFromSolution}\", \"{{11111111-1111-1111-1111-111111111111}}\"",
				"EndProject",
			]
			: [
				$"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"App\", \"{appPathFromSolution}\", \"{{11111111-1111-1111-1111-111111111111}}\"",
				"EndProject",
				$"Project(\"{{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}}\") = \"Lib\", \"{libPathFromSolution}\", \"{{22222222-2222-2222-2222-222222222222}}\"",
				"EndProject",
			];
		return $$"""
			Microsoft Visual Studio Solution File, Format Version 12.00
			# Visual Studio Version 17
			VisualStudioVersion = 17.0.31903.59
			MinimumVisualStudioVersion = 10.0.40219.1
			{{string.Join(Environment.NewLine, projectEntries)}}
			Global
			EndGlobal
			""";
	}

	private static async Task CreateSlnxFileAsync(string solutionPath, params string[] projectPaths)
	{
		string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
		SolutionModel solutionModel = new();
		solutionModel.AddBuildType("Debug");
		solutionModel.AddPlatform("Any CPU");
		foreach (string projectPath in projectPaths)
		{
			string projectPathFromSolution = Path.GetRelativePath(solutionDirectory, projectPath);
			SolutionProjectModel project = solutionModel.AddProject(projectPathFromSolution, projectTypeName: null, folder: null);
			project.DisplayName = Path.GetFileNameWithoutExtension(projectPath);
		}

		await ((ISolutionSerializer)SolutionSerializers.SlnXml).SaveAsync(solutionPath, solutionModel, TestContext.Current.CancellationToken);
	}

	private static string? GetNodePathById(XDocument document, string nodeId)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		return document.Root!
			.Element(ns + "Nodes")!
			.Elements(ns + "Node")
			.Single(node => (string?)node.Attribute("Id") == nodeId)
			.Attribute("Path")?
			.Value;
	}

	private static string? GetNodeLabelById(XDocument document, string nodeId)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		return document.Root!
			.Element(ns + "Nodes")!
			.Elements(ns + "Node")
			.Single(node => (string?)node.Attribute("Id") == nodeId)
			.Attribute("Label")?
			.Value;
	}

	private static string CreateLibraryProjectContent() => """
		<Project Sdk="Microsoft.NET.Sdk">
		  <PropertyGroup>
			<TargetFramework>net8.0</TargetFramework>
		  </PropertyGroup>
		</Project>
		""";

	private static XElement GetNodeByPath(XDocument document, string nodePath)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		return document.Root!
			.Element(ns + "Nodes")!
			.Elements(ns + "Node")
			.Single(node => (string?)node.Attribute("Path") == Path.GetFullPath(nodePath));
	}

	private static void AssertProjectHighlightStyle(IReadOnlyList<XElement> styles, string categoryId)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		XElement style = Assert.Single(styles, candidate => (string?)candidate.Element(ns + "Condition")?.Attribute("Expression") == $"HasCategory('{categoryId}')");
		Assert.Equal("Node", (string?)style.Attribute("TargetType"));
		Assert.Equal("Project Highlights", (string?)style.Attribute("GroupLabel"));
		Assert.StartsWith("Project Highlight ", (string)style.Attribute("ValueLabel")!);
		Assert.StartsWith("#", GetStyleSetterValue(styles, categoryId, "Background"));
		Assert.StartsWith("#", GetStyleSetterValue(styles, categoryId, "Stroke"));
		Assert.StartsWith("#", GetStyleSetterValue(styles, categoryId, "Foreground"));
	}

	private static string GetStyleSetterValue(IReadOnlyList<XElement> styles, string categoryId, string propertyName)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		XElement style = Assert.Single(styles, candidate => (string?)candidate.Element(ns + "Condition")?.Attribute("Expression") == $"HasCategory('{categoryId}')");
		return Assert.Single(style.Elements(ns + "Setter"), setter => (string?)setter.Attribute("Property") == propertyName).Attribute("Value")!.Value;
	}

	private async Task<(string ProjectPath, string ReferencedProjectPath)> CreateProjectGraphAsync()
	{
		Directory.CreateDirectory(this.StagingDirectory);
		string appDirectory = Path.Combine(this.StagingDirectory, "App");
		string libDirectory = Path.Combine(this.StagingDirectory, "Lib");
		Directory.CreateDirectory(appDirectory);
		Directory.CreateDirectory(libDirectory);

		string referencedProjectPath = Path.Combine(libDirectory, "Lib.csproj");
		string referencedProjectContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
			  </PropertyGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(referencedProjectPath, referencedProjectContent, TestContext.Current.CancellationToken);

		string projectPath = Path.Combine(appDirectory, "App.csproj");
		string projectContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
				<ProjectReference Include="..\Lib\Lib.csproj" />
			  </ItemGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(projectPath, projectContent, TestContext.Current.CancellationToken);

		return (projectPath, referencedProjectPath);
	}

	private async Task<(string ProjectPath, string ExcludedProjectPath, string IncludedProjectPath)> CreateProjectGraphWithSimilarPrefixPathsAsync()
	{
		Directory.CreateDirectory(this.StagingDirectory);
		string appDirectory = Path.Combine(this.StagingDirectory, "App");
		string excludedDirectory = Path.Combine(this.StagingDirectory, "subpath", "deeper");
		string includedDirectory = Path.Combine(this.StagingDirectory, "subpather");
		Directory.CreateDirectory(appDirectory);
		Directory.CreateDirectory(excludedDirectory);
		Directory.CreateDirectory(includedDirectory);

		string excludedProjectPath = Path.Combine(excludedDirectory, "Excluded.csproj");
		await File.WriteAllTextAsync(excludedProjectPath, CreateLibraryProjectContent(), TestContext.Current.CancellationToken);

		string includedProjectPath = Path.Combine(includedDirectory, "Included.csproj");
		await File.WriteAllTextAsync(includedProjectPath, CreateLibraryProjectContent(), TestContext.Current.CancellationToken);

		string projectPath = Path.Combine(appDirectory, "App.csproj");
		string projectContent = $$"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
				<ProjectReference Include="{{Path.GetRelativePath(appDirectory, excludedProjectPath)}}" />
				<ProjectReference Include="{{Path.GetRelativePath(appDirectory, includedProjectPath)}}" />
			  </ItemGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(projectPath, projectContent, TestContext.Current.CancellationToken);

		return (projectPath, excludedProjectPath, includedProjectPath);
	}

	private async Task<(string ProjectPath, string ParentGroupPath, string ChildGroupPath, string SiblingProjectPath, string NestedProjectPath, string UngroupedProjectPath)> CreateProjectGraphWithGroupingPathsAsync()
	{
		Directory.CreateDirectory(this.StagingDirectory);
		string parentGroupPath = Path.Combine(this.StagingDirectory, "src", "Copilot");
		string appDirectory = Path.Combine(parentGroupPath, "App");
		string siblingDirectory = Path.Combine(parentGroupPath, "Shared");
		string childGroupPath = Path.Combine(parentGroupPath, "tests");
		string ungroupedDirectory = Path.Combine(this.StagingDirectory, "src", "Other");
		Directory.CreateDirectory(appDirectory);
		Directory.CreateDirectory(siblingDirectory);
		Directory.CreateDirectory(childGroupPath);
		Directory.CreateDirectory(ungroupedDirectory);

		string siblingProjectPath = Path.Combine(siblingDirectory, "Shared.csproj");
		await File.WriteAllTextAsync(siblingProjectPath, CreateLibraryProjectContent(), TestContext.Current.CancellationToken);

		string nestedProjectPath = Path.Combine(childGroupPath, "Tests.csproj");
		await File.WriteAllTextAsync(nestedProjectPath, CreateLibraryProjectContent(), TestContext.Current.CancellationToken);

		string ungroupedProjectPath = Path.Combine(ungroupedDirectory, "Other.csproj");
		await File.WriteAllTextAsync(ungroupedProjectPath, CreateLibraryProjectContent(), TestContext.Current.CancellationToken);

		string projectPath = Path.Combine(appDirectory, "App.csproj");
		string projectContent = $$"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
				<ProjectReference Include="{{Path.GetRelativePath(appDirectory, siblingProjectPath)}}" />
				<ProjectReference Include="{{Path.GetRelativePath(appDirectory, nestedProjectPath)}}" />
				<ProjectReference Include="{{Path.GetRelativePath(appDirectory, ungroupedProjectPath)}}" />
			  </ItemGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(projectPath, projectContent, TestContext.Current.CancellationToken);

		return (projectPath, parentGroupPath, childGroupPath, siblingProjectPath, nestedProjectPath, ungroupedProjectPath);
	}
}
