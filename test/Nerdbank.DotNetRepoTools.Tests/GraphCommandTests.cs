// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
	public async Task WritesDgmlForProjectInput_OmitsProjectsUnderExcludedPathPrefixButNotSiblingPrefix()
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
				ExcludedProjectPaths = ["subpath"],
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

	private static async Task CreateSlnxFileAsync(string solutionPath, string projectPath)
	{
		string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
		string projectPathFromSolution = Path.GetRelativePath(solutionDirectory, projectPath);
		SolutionModel solutionModel = new();
		solutionModel.AddBuildType("Debug");
		solutionModel.AddPlatform("Any CPU");
		SolutionProjectModel project = solutionModel.AddProject(projectPathFromSolution, projectTypeName: null, folder: null);
		project.DisplayName = "App";

		await ((ISolutionSerializer)SolutionSerializers.SlnXml).SaveAsync(solutionPath, solutionModel, TestContext.Current.CancellationToken);
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
}
