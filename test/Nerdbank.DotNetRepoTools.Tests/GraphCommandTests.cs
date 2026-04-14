// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.CommandLine;
using System.Reflection;
using System.Xml.Linq;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;

[Collection(nameof(CurrentDirectorySensitiveTestCollection))]
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
	public async Task WritesMermaidForProjectInput()
	{
		(string projectPath, _) = await this.CreateProjectGraphAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph.mmd");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
			OutputFormat = GraphCommand.GraphOutputFormat.Mermaid,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string mermaid = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.StartsWith("flowchart ", mermaid, StringComparison.Ordinal);
		Assert.Contains("[\"App.csproj\"]", mermaid);
		Assert.Contains("[\"Lib.csproj\"]", mermaid);
		Assert.Contains(" --> ", mermaid);
		Assert.Contains("Wrote 2 node(s) and 1 edge(s)", ((StringWriter)this.Command.Out).ToString());
	}

	[Fact]
	public async Task InfersMermaidFormatFromOutputExtensionWhenFormatOptionOmitted()
	{
		(string projectPath, _) = await this.CreateProjectGraphAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph.mmd");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string mermaid = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.StartsWith("flowchart ", mermaid, StringComparison.Ordinal);
		Assert.Contains("[\"App.csproj\"]", mermaid);
	}

	[Fact]
	public async Task ExplicitFormatOverridesOutputExtension()
	{
		(string projectPath, _) = await this.CreateProjectGraphAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph.dgml");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
			OutputFormat = GraphCommand.GraphOutputFormat.Mermaid,
			IsOutputFormatSpecified = true,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string content = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.StartsWith("flowchart ", content, StringComparison.Ordinal);
		Assert.DoesNotContain("<DirectedGraph", content);
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
		Assert.Equal(GetEmittedPath(solutionPath, projectPath), GetNodePathById(document, GetNodeIdByPath(document, projectPath, solutionPath)));
		Assert.Equal(GetEmittedPath(solutionPath, referencedProjectPath), GetNodePathById(document, GetNodeIdByPath(document, referencedProjectPath, solutionPath)));
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
	public async Task WritesDgmlWithStableNodeIdsAcrossDifferentAbsoluteRoots()
	{
		string firstRoot = Path.Combine(this.StagingDirectory, "first-root");
		string secondRoot = Path.Combine(this.StagingDirectory, "second-root");
		(string firstProjectPath, string firstReferencedProjectPath) = await this.CreateProjectGraphAsync(firstRoot);
		(string secondProjectPath, string secondReferencedProjectPath) = await this.CreateProjectGraphAsync(secondRoot);
		string firstOutputPath = Path.Combine(firstRoot, "graph.dgml");
		string secondOutputPath = Path.Combine(secondRoot, "graph.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;

		try
		{
			Environment.CurrentDirectory = firstRoot;
			this.Command = new()
			{
				InputPath = firstProjectPath,
				OutputPath = firstOutputPath,
			};

			await this.ExecuteCommandAsync();
			Assert.Equal(0, this.Command.ExitCode);

			Environment.CurrentDirectory = secondRoot;
			this.Command = new()
			{
				InputPath = secondProjectPath,
				OutputPath = secondOutputPath,
			};

			await this.ExecuteCommandAsync();
			Assert.Equal(0, this.Command.ExitCode);
		}
		finally
		{
			Environment.CurrentDirectory = originalCurrentDirectory;
		}

		XDocument firstDocument = XDocument.Load(firstOutputPath);
		XDocument secondDocument = XDocument.Load(secondOutputPath);
		Assert.Equal(GetNodeIdByPath(firstDocument, firstProjectPath, firstProjectPath), GetNodeIdByPath(secondDocument, secondProjectPath, secondProjectPath));
		Assert.Equal(GetNodeIdByPath(firstDocument, firstReferencedProjectPath, firstProjectPath), GetNodeIdByPath(secondDocument, secondReferencedProjectPath, secondProjectPath));
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
		string emittedPathBaseDirectory = GetGraphOutputPathBaseDirectory(projectPath);

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
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, projectPath));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, includedProjectPath));
		Assert.DoesNotContain(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, excludedProjectPath));
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_OmitsProjectsMatchingAbsoluteRootedExcludeGlob()
	{
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph-rooted-pattern.dgml");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
			ExcludedProjectPaths = [Path.Combine(Path.GetPathRoot(referencedProjectPath)!, "**", Path.GetFileName(referencedProjectPath))],
		};

		await this.ExecuteCommandAsync();

		string emittedPathBaseDirectory = GetGraphOutputPathBaseDirectory(projectPath);
		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Single(document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node"));
		Assert.Empty(document.Root.Element(ns + "Links")!.Elements(ns + "Link"));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, projectPath));
		Assert.DoesNotContain(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, referencedProjectPath));
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_OmitsProjectsMatchingPrefixAgnosticExcludeGlob()
	{
		string projectRoot = Path.Combine(this.StagingDirectory, "project-root");
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync(projectRoot);
		string outputPath = Path.Combine(this.StagingDirectory, "graph-prefix-agnostic-pattern.dgml");
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
				ExcludedProjectPaths = [Path.Combine("**", Path.GetFileName(referencedProjectPath))],
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
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(projectPath, projectPath));
		Assert.DoesNotContain(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(projectPath, referencedProjectPath));
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_GroupsProjectsUsingAbsoluteAndWorkingDirectoryRelativePaths()
	{
		(string projectPath, string parentGroupPath, string childGroupPath, string siblingProjectPath, string nestedProjectPath, string ungroupedProjectPath) = await this.CreateProjectGraphWithGroupingPathsAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph-grouped.dgml");
		string originalCurrentDirectory = Environment.CurrentDirectory;
		string workingDirectory = Path.Combine(this.StagingDirectory, "cwd");
		string emittedPathBaseDirectory = GetGraphOutputPathBaseDirectory(projectPath);
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
		XElement parentContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, parentGroupPath));
		Assert.Equal("Copilot", (string?)parentContainer.Attribute("Label"));
		Assert.Equal("Expanded", (string?)parentContainer.Attribute("Group"));
		XElement childContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, childGroupPath));
		Assert.Equal("tests", (string?)childContainer.Attribute("Label"));
		Assert.Equal("Expanded", (string?)childContainer.Attribute("Group"));

		IReadOnlyList<XElement> containsLinks = document.Root.Element(ns + "Links")!.Elements(ns + "Link")
			.Where(link => (string?)link.Attribute("Category") == "Contains")
			.ToList();
		Assert.Equal(4, containsLinks.Count);
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && (string?)link.Attribute("Target") == (string?)childContainer.Attribute("Id"));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, projectPath));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, siblingProjectPath));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)childContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, nestedProjectPath));
		Assert.DoesNotContain(containsLinks, link => GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, ungroupedProjectPath));
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

		string emittedPathBaseDirectory = GetGraphOutputPathBaseDirectory(solutionPath);
		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.DoesNotContain(document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Id") == "solution:explicit-projects");
		XElement parentContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, parentGroupPath));

		IReadOnlyList<XElement> containsLinks = document.Root.Element(ns + "Links")!.Elements(ns + "Link")
			.Where(link => (string?)link.Attribute("Category") == "Contains")
			.ToList();
		Assert.Equal(3, containsLinks.Count);
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, projectPath));
		Assert.DoesNotContain(containsLinks, link => GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, ungroupedProjectPath));
	}

	[Fact]
	public async Task WritesDgmlForProjectInput_DoesNotCreateEmptyGroupContainers()
	{
		(string projectPath, string parentGroupPath, _, string siblingProjectPath, string nestedProjectPath, string ungroupedProjectPath) = await this.CreateProjectGraphWithGroupingPathsAsync();
		string emptyChildGroupPath = Path.Combine(parentGroupPath, "empty");
		Directory.CreateDirectory(emptyChildGroupPath);
		string outputPath = Path.Combine(this.StagingDirectory, "graph-grouped-no-empty-containers.dgml");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
			GroupPaths = [parentGroupPath, emptyChildGroupPath],
		};

		await this.ExecuteCommandAsync();

		string emittedPathBaseDirectory = GetGraphOutputPathBaseDirectory(projectPath);
		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Equal(5, document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node").Count());
		XElement parentContainer = Assert.Single(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, parentGroupPath));
		Assert.DoesNotContain(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == GetEmittedPath(emittedPathBaseDirectory, emptyChildGroupPath));

		IReadOnlyList<XElement> containsLinks = document.Root.Element(ns + "Links")!.Elements(ns + "Link")
			.Where(link => (string?)link.Attribute("Category") == "Contains")
			.ToList();
		Assert.Equal(3, containsLinks.Count);
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, projectPath));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, siblingProjectPath));
		Assert.Contains(containsLinks, link => (string?)link.Attribute("Source") == (string?)parentContainer.Attribute("Id") && GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, nestedProjectPath));
		Assert.DoesNotContain(containsLinks, link => GetNodePathById(document, (string)link.Attribute("Target")!) == GetEmittedPath(emittedPathBaseDirectory, ungroupedProjectPath));
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
	public void CreateCommand_DefinesFormatOptionAlias()
	{
		MethodInfo createCommandMethod = typeof(GraphCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option formatOption = Assert.Single(command.Options, option => option.Name == "--format");
		Assert.Contains("-f", formatOption.Aliases);
	}

	[Fact]
	public async Task WritesMermaidForProjectInput_GroupsProjectsAndHighlights()
	{
		(string projectPath, string parentGroupPath, string childGroupPath, string siblingProjectPath, string nestedProjectPath, string ungroupedProjectPath) = await this.CreateProjectGraphWithGroupingPathsAsync();
		string outputPath = Path.Combine(this.StagingDirectory, "graph-highlighted.mmd");
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
				OutputFormat = GraphCommand.GraphOutputFormat.Mermaid,
				GroupPaths = [parentGroupPath, Path.GetRelativePath(workingDirectory, childGroupPath)],
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
		string mermaid = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.StartsWith("flowchart ", mermaid, StringComparison.Ordinal);
		Assert.Contains("subgraph", mermaid);
		Assert.Contains("[\"Copilot\"]", mermaid);
		Assert.Contains("[\"tests\"]", mermaid);
		Assert.Contains("[\"Shared.csproj\"]", mermaid);
		Assert.Contains("[\"Tests.csproj\"]", mermaid);
		Assert.Contains("[\"Other.csproj\"]", mermaid);
		Assert.Contains("classDef highlight1 fill:#DBEAFE,stroke:#2563EB,color:#172554;", mermaid);
		Assert.Contains("classDef highlight2 fill:#DCFCE7,stroke:#16A34A,color:#052E16;", mermaid);
		Assert.Contains("class ", mermaid);
		Assert.Contains(" --> ", mermaid);
		Assert.DoesNotContain("Other.csproj\"] highlight", mermaid);
		Assert.DoesNotContain("Shared.csproj\"] highlight", mermaid);
	}

	[Fact]
	public async Task WritesMermaidUsingRepoRelativePathsWhenGitRepoContainsProject()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
		(string projectPath, _) = await this.CreateProjectGraphAsync(repoRoot);
		string outputPath = Path.Combine(repoRoot, "graph.mmd");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
			OutputFormat = GraphCommand.GraphOutputFormat.Mermaid,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string mermaid = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.DoesNotContain(repoRoot, mermaid, StringComparison.OrdinalIgnoreCase);
		Assert.Contains("[\"App.csproj\"]", mermaid);
		Assert.Contains("[\"Lib.csproj\"]", mermaid);
	}

	[Fact]
	public async Task WritesDgmlUsingRepoRelativePathsWhenGitRepoContainsProject()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		Directory.CreateDirectory(Path.Combine(repoRoot, ".git"));
		(string projectPath, string referencedProjectPath) = await this.CreateProjectGraphAsync(repoRoot);
		string outputPath = Path.Combine(repoRoot, "graph.dgml");
		this.Command = new()
		{
			InputPath = projectPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		XDocument document = XDocument.Load(outputPath);
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		Assert.Contains(document.Root!.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.Combine("App", "App.csproj"));
		Assert.Contains(document.Root.Element(ns + "Nodes")!.Elements(ns + "Node"), node => (string?)node.Attribute("Path") == Path.Combine("Lib", "Lib.csproj"));
		Assert.DoesNotContain(document.ToString(), repoRoot, StringComparison.OrdinalIgnoreCase);
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

	private static string GetNodeIdByPath(XDocument document, string nodePath, string inputPath)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		return document.Root!
			.Element(ns + "Nodes")!
			.Elements(ns + "Node")
			.Single(node => (string?)node.Attribute("Path") == GetEmittedPath(inputPath, nodePath))
			.Attribute("Id")!
			.Value;
	}

	private static string CreateLibraryProjectContent() => """
		<Project Sdk="Microsoft.NET.Sdk">
		  <PropertyGroup>
			<TargetFramework>net8.0</TargetFramework>
		  </PropertyGroup>
		</Project>
		""";

	private static XElement GetNodeByPath(XDocument document, string nodePath, string inputPath)
	{
		XNamespace ns = "http://schemas.microsoft.com/vs/2009/dgml";
		return document.Root!
			.Element(ns + "Nodes")!
			.Elements(ns + "Node")
			.Single(node => (string?)node.Attribute("Path") == GetEmittedPath(inputPath, nodePath));
	}

	private static string GetGraphOutputPathBaseDirectory(string inputPath)
	{
		string inputDirectory = Path.GetDirectoryName(Path.GetFullPath(inputPath))!;
		for (string? subpath = inputDirectory; subpath is not null; subpath = Path.GetDirectoryName(subpath))
		{
			string gitLocation = Path.Combine(subpath, ".git");
			if (File.Exists(gitLocation) || Directory.Exists(gitLocation))
			{
				return subpath;
			}
		}

		return inputDirectory;
	}

	private static string GetEmittedPath(string inputPathOrBaseDirectory, string path)
	{
		string fullInputPathOrBaseDirectory = Path.GetFullPath(inputPathOrBaseDirectory);
		string pathBaseDirectory = File.Exists(fullInputPathOrBaseDirectory)
			? GetGraphOutputPathBaseDirectory(fullInputPathOrBaseDirectory)
			: fullInputPathOrBaseDirectory;
		return Path.TrimEndingDirectorySeparator(Path.GetRelativePath(pathBaseDirectory, Path.GetFullPath(path)));
	}

	private async Task<(string ProjectPath, string ReferencedProjectPath)> CreateProjectGraphAsync()
		=> await this.CreateProjectGraphAsync(this.StagingDirectory);

	private async Task<(string ProjectPath, string ReferencedProjectPath)> CreateProjectGraphAsync(string rootDirectory)
	{
		Directory.CreateDirectory(rootDirectory);
		string appDirectory = Path.Combine(rootDirectory, "App");
		string libDirectory = Path.Combine(rootDirectory, "Lib");
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
