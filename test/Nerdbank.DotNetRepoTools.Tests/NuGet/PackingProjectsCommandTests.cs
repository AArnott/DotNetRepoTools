// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Reflection;
using System.Text.Json;
using Microsoft.VisualStudio.SolutionPersistence;
using Microsoft.VisualStudio.SolutionPersistence.Model;
using Microsoft.VisualStudio.SolutionPersistence.Serializer;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

[Collection(nameof(CurrentDirectorySensitiveTestCollection))]
public class PackingProjectsCommandTests : CommandTestBase<PackingProjectsCommand>
{
	public PackingProjectsCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public void CreateCommand_DefinesFormatOptionAlias()
	{
		MethodInfo createCommandMethod = typeof(PackingProjectsCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option formatOption = Assert.Single(command.Options, option => option.Name == "--format");
		Assert.Contains("-f", formatOption.Aliases);
	}

	[Fact]
	public void CreateCommand_DefinesFindConsumersOptionAlias()
	{
		MethodInfo createCommandMethod = typeof(PackingProjectsCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option findConsumersOption = Assert.Single(command.Options, option => option.Name == "--find-consumers");
		Assert.Contains("-c", findConsumersOption.Aliases);
	}

	[Fact]
	public void CreateCommand_DefinesFindTransitiveConsumersOption()
	{
		MethodInfo createCommandMethod = typeof(PackingProjectsCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Assert.Single(command.Options, option => option.Name == "--find-transitive-consumers");
	}

	[Fact]
	public void Constructor_EnablesFindConsumersWhenFindTransitiveConsumersSpecified()
	{
		MethodInfo createCommandMethod = typeof(PackingProjectsCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		ParseResult parseResult = command.Parse(["--find-transitive-consumers", "repo.csproj"]);

		using PackingProjectsCommand packingProjectsCommand = new(parseResult, CancellationToken.None);

		Assert.True(packingProjectsCommand.FindTransitiveConsumers);
		Assert.True(packingProjectsCommand.FindConsumers);
	}

	[Fact]
	public void CreateCommand_DefinesOutputPathOptionAlias()
	{
		MethodInfo createCommandMethod = typeof(PackingProjectsCommand).GetMethod("CreateCommand", BindingFlags.Static | BindingFlags.NonPublic)!;
		Command command = Assert.IsType<Command>(createCommandMethod.Invoke(obj: null, parameters: null));
		Option outputPathOption = Assert.Single(command.Options, option => option.Name == "--output-path");
		Assert.Contains("-o", outputPathOption.Aliases);
	}

	[Fact]
	public async Task ListsPackingProjectsForProjectInput()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		this.Command = new()
		{
			InputPath = rootProjectPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string[] lines = ((StringWriter)this.Command.Out).ToString()
			.Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
		Assert.Equal(
			[
				$"App: {Path.Combine("src", "App", "App.csproj")}",
				$"Contoso.Legacy: {Path.Combine("packaging", "Legacy", "Legacy.nuproj")}",
				$"Contoso.Multi: {Path.Combine("src", "Multi", "Multi.csproj")}",
				$"Contoso.Packed: {Path.Combine("src", "Packed", "Packed.csproj")}",
			],
			lines);
	}

	[Fact]
	public async Task WritesJsonForProjectInput()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		this.Command = new()
		{
			InputPath = rootProjectPath,
			Format = PackingProjectsOutputFormat.Json,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		using JsonDocument json = JsonDocument.Parse(((StringWriter)this.Command.Out).ToString());
		JsonElement packingProjects = json.RootElement.GetProperty("packingProjects");
		JsonElement failedProjects = json.RootElement.GetProperty("failedProjects");
		Assert.Equal(4, packingProjects.GetArrayLength());
		Assert.Equal(0, failedProjects.GetArrayLength());
		Assert.Equal("App", packingProjects[0].GetProperty("packageId").GetString());
		Assert.Equal(Path.Combine("src", "App", "App.csproj"), packingProjects[0].GetProperty("projectPath").GetString());
		Assert.Equal("Contoso.Legacy", packingProjects[1].GetProperty("packageId").GetString());
		Assert.Equal(Path.Combine("packaging", "Legacy", "Legacy.nuproj"), packingProjects[1].GetProperty("projectPath").GetString());
		Assert.Equal("Contoso.Multi", packingProjects[2].GetProperty("packageId").GetString());
		Assert.Equal(Path.Combine("src", "Multi", "Multi.csproj"), packingProjects[2].GetProperty("projectPath").GetString());
		Assert.Equal("Contoso.Packed", packingProjects[3].GetProperty("packageId").GetString());
		Assert.Equal(Path.Combine("src", "Packed", "Packed.csproj"), packingProjects[3].GetProperty("projectPath").GetString());
		Assert.Equal(0, json.RootElement.GetProperty("builtPackageConsumers").GetArrayLength());
	}

	[Fact]
	public async Task WritesTextToOutputFileWhenOutputPathSpecified()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string outputPath = Path.Combine(this.StagingDirectory, "packing-projects.txt");
		this.Command = new()
		{
			InputPath = rootProjectPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		Assert.Equal(string.Empty, ((StringWriter)this.Command.Out).ToString());
		string fileOutput = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.Contains($"App: {Path.Combine("src", "App", "App.csproj")}", fileOutput);
		Assert.Contains($"Contoso.Packed: {Path.Combine("src", "Packed", "Packed.csproj")}", fileOutput);
	}

	[Fact]
	public async Task InfersJsonFormatFromOutputPathWhenFormatOmitted()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string outputPath = Path.Combine(this.StagingDirectory, "packing-projects.json");
		this.Command = new()
		{
			InputPath = rootProjectPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		Assert.Equal(string.Empty, ((StringWriter)this.Command.Out).ToString());
		using JsonDocument json = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken));
		Assert.Equal(4, json.RootElement.GetProperty("packingProjects").GetArrayLength());
	}

	[Fact]
	public async Task UsesTextFormatForNonJsonOutputPathWhenFormatOmitted()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string outputPath = Path.Combine(this.StagingDirectory, "packing-projects.out");
		this.Command = new()
		{
			InputPath = rootProjectPath,
			OutputPath = outputPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string output = await File.ReadAllTextAsync(outputPath, TestContext.Current.CancellationToken);
		Assert.Contains($"App: {Path.Combine("src", "App", "App.csproj")}", output);
		Assert.DoesNotContain("\"packingProjects\"", output);
	}

	[Fact]
	public async Task LeavesStdOutUsableWhenOutputPathIsNotSpecified()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		this.Command = new()
		{
			InputPath = rootProjectPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		StringWriter stdOutWriter = (StringWriter)this.Command.Out;
		stdOutWriter.Write("tail");
		Assert.EndsWith("tail", stdOutWriter.ToString(), StringComparison.Ordinal);
	}

	[Fact]
	public async Task ListsConsumersOfBuiltPackagesWhenRequested()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string rootProjectDirectory = Path.GetDirectoryName(rootProjectPath)!;
		string importedItemsPath = Path.Combine(rootProjectDirectory, "ImportedPackages.props");
		string importedItemsContent = string.Join(
			Environment.NewLine,
			[
				"<Project>",
				"  <ItemGroup>",
				"    <PackageReference Include=\"Contoso.Packed\" Version=\"1.0.0\" />",
				"    <PackageVersion Include=\"Contoso.Multi\" Version=\"1.0.0\" />",
				"  </ItemGroup>",
				"</Project>",
			]);
		await File.WriteAllTextAsync(
			importedItemsPath,
			importedItemsContent,
			TestContext.Current.CancellationToken);
		string rootProjectContent = string.Join(
			Environment.NewLine,
			[
				"<Project Sdk=\"Microsoft.NET.Sdk\">",
				"  <PropertyGroup>",
				"    <TargetFramework>net8.0</TargetFramework>",
				"  </PropertyGroup>",
				$"  <Import Project=\"{Path.GetFileName(importedItemsPath)}\" />",
				"  <ItemGroup>",
				$"    <ProjectReference Include=\"{Path.Combine("..", "Packed", "Packed.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "Multi", "Multi.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "..", "test", "DisabledPack", "DisabledPack.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "..", "packaging", "Legacy", "Legacy.nuproj")}\" />",
				"  </ItemGroup>",
				"</Project>",
			]);

		await File.WriteAllTextAsync(
			rootProjectPath,
			rootProjectContent,
			TestContext.Current.CancellationToken);
		await WriteProjectAssetsFileAsync(
			rootProjectPath,
			directPackageIds: ["Contoso.Packed"],
			resolvedPackageIds: ["Contoso.Packed", "Contoso.Legacy"]);

		this.Command = new()
		{
			InputPath = rootProjectPath,
			FindConsumers = true,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string output = ((StringWriter)this.Command.Out).ToString();
		Assert.Contains("Consumers of built package IDs:", output);
		Assert.Contains("Contoso.Multi:", output);
		Assert.Contains("Contoso.Packed:", output);
		Assert.Contains($"  {Path.Combine("src", "App", "ImportedPackages.props")} (direct)", output);
		Assert.DoesNotContain($"  {Path.Combine("src", "App", "App.csproj")} (transitive)", output);
	}

	[Fact]
	public async Task WritesJsonConsumersOfBuiltPackagesWhenRequested()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string rootProjectDirectory = Path.GetDirectoryName(rootProjectPath)!;
		string importedItemsPath = Path.Combine(rootProjectDirectory, "ImportedPackages.props");
		string importedItemsContent = string.Join(
			Environment.NewLine,
			[
				"<Project>",
				"  <ItemGroup>",
				"    <PackageVersion Include=\"Contoso.Legacy\" Version=\"1.0.0\" />",
				"  </ItemGroup>",
				"</Project>",
			]);
		await File.WriteAllTextAsync(
			importedItemsPath,
			importedItemsContent,
			TestContext.Current.CancellationToken);
		string rootProjectContent = string.Join(
			Environment.NewLine,
			[
				"<Project Sdk=\"Microsoft.NET.Sdk\">",
				"  <PropertyGroup>",
				"    <TargetFramework>net8.0</TargetFramework>",
				"  </PropertyGroup>",
				$"  <Import Project=\"{Path.GetFileName(importedItemsPath)}\" />",
				"  <ItemGroup>",
				$"    <ProjectReference Include=\"{Path.Combine("..", "Packed", "Packed.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "Multi", "Multi.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "..", "test", "DisabledPack", "DisabledPack.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "..", "packaging", "Legacy", "Legacy.nuproj")}\" />",
				"  </ItemGroup>",
				"</Project>",
			]);

		await File.WriteAllTextAsync(
			rootProjectPath,
			rootProjectContent,
			TestContext.Current.CancellationToken);
		await WriteProjectAssetsFileAsync(
			rootProjectPath,
			directPackageIds: [],
			resolvedPackageIds: ["Contoso.Legacy"]);

		this.Command = new()
		{
			InputPath = rootProjectPath,
			Format = PackingProjectsOutputFormat.Json,
			FindConsumers = true,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		using JsonDocument json = JsonDocument.Parse(((StringWriter)this.Command.Out).ToString());
		JsonElement builtPackageConsumers = json.RootElement.GetProperty("builtPackageConsumers");
		JsonElement legacyConsumer = Assert.Single(
			builtPackageConsumers.EnumerateArray(),
			element => element.GetProperty("packageId").GetString() == "Contoso.Legacy");
		JsonElement legacyConsumerEntry = Assert.Single(legacyConsumer.GetProperty("consumers").EnumerateArray());
		Assert.Equal(Path.Combine("src", "App", "ImportedPackages.props"), legacyConsumerEntry.GetProperty("consumerProjectPath").GetString());
		Assert.Equal("direct", legacyConsumerEntry.GetProperty("dependencyKind").GetString());
	}

	[Fact]
	public async Task FindsTransitiveConsumersWhenRequested()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string rootProjectDirectory = Path.GetDirectoryName(rootProjectPath)!;
		string importedItemsPath = Path.Combine(rootProjectDirectory, "ImportedPackages.props");
		string importedItemsContent = string.Join(
			Environment.NewLine,
			[
				"<Project>",
				"  <ItemGroup>",
				"    <PackageVersion Include=\"Contoso.Legacy\" Version=\"1.0.0\" />",
				"  </ItemGroup>",
				"</Project>",
			]);
		await File.WriteAllTextAsync(
			importedItemsPath,
			importedItemsContent,
			TestContext.Current.CancellationToken);
		string rootProjectContent = string.Join(
			Environment.NewLine,
			[
				"<Project Sdk=\"Microsoft.NET.Sdk\">",
				"  <PropertyGroup>",
				"    <TargetFramework>net8.0</TargetFramework>",
				"  </PropertyGroup>",
				$"  <Import Project=\"{Path.GetFileName(importedItemsPath)}\" />",
				"  <ItemGroup>",
				$"    <ProjectReference Include=\"{Path.Combine("..", "Packed", "Packed.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "Multi", "Multi.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "..", "test", "DisabledPack", "DisabledPack.csproj")}\" />",
				$"    <ProjectReference Include=\"{Path.Combine("..", "..", "packaging", "Legacy", "Legacy.nuproj")}\" />",
				"  </ItemGroup>",
				"</Project>",
			]);

		await File.WriteAllTextAsync(
			rootProjectPath,
			rootProjectContent,
			TestContext.Current.CancellationToken);
		await WriteProjectAssetsFileAsync(
			rootProjectPath,
			directPackageIds: [],
			resolvedPackageIds: ["Contoso.Legacy"]);

		this.Command = new()
		{
			InputPath = rootProjectPath,
			Format = PackingProjectsOutputFormat.Json,
			FindTransitiveConsumers = true,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		using JsonDocument json = JsonDocument.Parse(((StringWriter)this.Command.Out).ToString());
		JsonElement builtPackageConsumers = json.RootElement.GetProperty("builtPackageConsumers");
		JsonElement legacyConsumerWithTransitive = Assert.Single(
			builtPackageConsumers.EnumerateArray(),
			element => element.GetProperty("packageId").GetString() == "Contoso.Legacy");
		JsonElement legacyConsumerEntry = Assert.Single(
			legacyConsumerWithTransitive.GetProperty("consumers").EnumerateArray(),
			element => element.GetProperty("dependencyKind").GetString() == "transitive");
		Assert.Equal(Path.Combine("src", "App", "App.csproj"), legacyConsumerEntry.GetProperty("consumerProjectPath").GetString());
		Assert.Equal("transitive", legacyConsumerEntry.GetProperty("dependencyKind").GetString());
	}

	[Fact]
	public async Task ListsPackingProjectsForSolutionInput()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string solutionPath = Path.Combine(repoRoot, "Repo.sln");
		await File.WriteAllTextAsync(
			solutionPath,
			CreateSolutionFileContent(solutionPath, rootProjectPath),
			TestContext.Current.CancellationToken);
		this.Command = new()
		{
			InputPath = solutionPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string output = ((StringWriter)this.Command.Out).ToString();
		Assert.Contains($"App: {Path.Combine("src", "App", "App.csproj")}", output);
		Assert.Contains($"Contoso.Packed: {Path.Combine("src", "Packed", "Packed.csproj")}", output);
		Assert.Contains($"Contoso.Multi: {Path.Combine("src", "Multi", "Multi.csproj")}", output);
		Assert.Contains($"Contoso.Legacy: {Path.Combine("packaging", "Legacy", "Legacy.nuproj")}", output);
	}

	[Fact]
	public async Task ListsPackingProjectsForSlnxInput()
	{
		string repoRoot = Path.Combine(this.StagingDirectory, "repo");
		(string rootProjectPath, _, _, _, _) = await this.CreatePackingProjectGraphAsync(repoRoot);
		string solutionPath = Path.Combine(repoRoot, "Repo.slnx");
		await CreateSlnxFileAsync(solutionPath, rootProjectPath);
		this.Command = new()
		{
			InputPath = solutionPath,
		};

		await this.ExecuteCommandAsync();

		Assert.Equal(0, this.Command.ExitCode);
		string output = ((StringWriter)this.Command.Out).ToString();
		Assert.Contains($"App: {Path.Combine("src", "App", "App.csproj")}", output);
		Assert.Contains($"Contoso.Packed: {Path.Combine("src", "Packed", "Packed.csproj")}", output);
		Assert.Contains($"Contoso.Multi: {Path.Combine("src", "Multi", "Multi.csproj")}", output);
		Assert.Contains($"Contoso.Legacy: {Path.Combine("packaging", "Legacy", "Legacy.nuproj")}", output);
	}

	private static string CreateSolutionFileContent(string solutionPath, string projectPath)
	{
		string solutionDirectory = Path.GetDirectoryName(solutionPath)!;
		string projectPathFromSolution = Path.GetRelativePath(solutionDirectory, projectPath);
		return $$"""
			Microsoft Visual Studio Solution File, Format Version 12.00
			# Visual Studio Version 17
			VisualStudioVersion = 17.0.31903.59
			MinimumVisualStudioVersion = 10.0.40219.1
			Project("{FAE04EC0-301F-11D3-BF4B-00C04F79EFBC}") = "App", "{{projectPathFromSolution}}", "{11111111-1111-1111-1111-111111111111}"
			EndProject
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

	private static async Task WriteProjectAssetsFileAsync(string projectPath, string[] directPackageIds, string[] resolvedPackageIds)
	{
		string assetsFilePath = Path.Combine(Path.GetDirectoryName(projectPath)!, "obj", "project.assets.json");
		Directory.CreateDirectory(Path.GetDirectoryName(assetsFilePath)!);

		string assetsFileContent = JsonSerializer.Serialize(new
		{
			version = 3,
			project = new
			{
				frameworks = new Dictionary<string, object?>
				{
					["net8.0"] = new
					{
						dependencies = directPackageIds.ToDictionary(
							static packageId => packageId,
							static _ => (object?)new { target = "Package", version = "[1.0.0, )" },
							StringComparer.OrdinalIgnoreCase),
					},
				},
			},
			libraries = resolvedPackageIds.ToDictionary(
				static packageId => $"{packageId}/1.0.0",
				static packageId => (object?)new
				{
					type = "package",
					path = $"{packageId.ToLowerInvariant()}/1.0.0",
					files = Array.Empty<string>(),
				},
				StringComparer.OrdinalIgnoreCase),
		});

		await File.WriteAllTextAsync(assetsFilePath, assetsFileContent, TestContext.Current.CancellationToken);
	}

	private async Task<(string RootProjectPath, string PackedProjectPath, string MultiTargetProjectPath, string NuProjPath, string DisabledPackProjectPath)> CreatePackingProjectGraphAsync(string rootDirectory)
	{
		Directory.CreateDirectory(rootDirectory);
		Directory.CreateDirectory(Path.Combine(rootDirectory, ".git"));

		string rootProjectDirectory = Path.Combine(rootDirectory, "src", "App");
		string packedProjectDirectory = Path.Combine(rootDirectory, "src", "Packed");
		string multiTargetProjectDirectory = Path.Combine(rootDirectory, "src", "Multi");
		string disabledPackProjectDirectory = Path.Combine(rootDirectory, "test", "DisabledPack");
		string nuProjDirectory = Path.Combine(rootDirectory, "packaging", "Legacy");
		Directory.CreateDirectory(rootProjectDirectory);
		Directory.CreateDirectory(packedProjectDirectory);
		Directory.CreateDirectory(multiTargetProjectDirectory);
		Directory.CreateDirectory(disabledPackProjectDirectory);
		Directory.CreateDirectory(nuProjDirectory);

		string packedProjectPath = Path.Combine(packedProjectDirectory, "Packed.csproj");
		string packedProjectContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
				<IsPackable>true</IsPackable>
				<PackageId>Contoso.Packed</PackageId>
			  </PropertyGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(
			packedProjectPath,
			packedProjectContent,
			TestContext.Current.CancellationToken);

		string multiTargetProjectPath = Path.Combine(multiTargetProjectDirectory, "Multi.csproj");
		string multiTargetProjectContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFrameworks>net8.0;net9.0</TargetFrameworks>
				<IsPackable>true</IsPackable>
				<PackageId>Contoso.Multi</PackageId>
			  </PropertyGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(
			multiTargetProjectPath,
			multiTargetProjectContent,
			TestContext.Current.CancellationToken);

		string disabledPackProjectPath = Path.Combine(disabledPackProjectDirectory, "DisabledPack.csproj");
		string disabledPackProjectContent = """
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
				<IsPackable>false</IsPackable>
				<PackageId>Contoso.Disabled</PackageId>
			  </PropertyGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(
			disabledPackProjectPath,
			disabledPackProjectContent,
			TestContext.Current.CancellationToken);

		string nuProjPath = Path.Combine(nuProjDirectory, "Legacy.nuproj");
		string nuProjContent = """
			<Project>
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
				<PackageName>Contoso.Legacy</PackageName>
			  </PropertyGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(
			nuProjPath,
			nuProjContent,
			TestContext.Current.CancellationToken);

		string rootProjectPath = Path.Combine(rootProjectDirectory, "App.csproj");
		string rootProjectContent = $$"""
			<Project Sdk="Microsoft.NET.Sdk">
			  <PropertyGroup>
				<TargetFramework>net8.0</TargetFramework>
			  </PropertyGroup>
			  <ItemGroup>
				<ProjectReference Include="{{Path.GetRelativePath(rootProjectDirectory, packedProjectPath)}}" />
				<ProjectReference Include="{{Path.GetRelativePath(rootProjectDirectory, multiTargetProjectPath)}}" />
				<ProjectReference Include="{{Path.GetRelativePath(rootProjectDirectory, disabledPackProjectPath)}}" />
				<ProjectReference Include="{{Path.GetRelativePath(rootProjectDirectory, nuProjPath)}}" />
			  </ItemGroup>
			</Project>
			""";
		await File.WriteAllTextAsync(
			rootProjectPath,
			rootProjectContent,
			TestContext.Current.CancellationToken);

		return (rootProjectPath, packedProjectPath, multiTargetProjectPath, nuProjPath, disabledPackProjectPath);
	}
}
