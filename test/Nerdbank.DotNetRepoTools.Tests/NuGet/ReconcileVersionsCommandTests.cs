// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

public class ReconcileVersionsCommandTests : CommandTestBase<ReconcileVersionsCommand>
{
	private Project packagesProps = null!;

	public ReconcileVersionsCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public override async ValueTask InitializeAsync()
	{
		await base.InitializeAsync();

		await this.SynthesizeAllMSBuildAssetsAsync();
		await File.WriteAllTextAsync(
			Path.Combine(this.StagingDirectory, "Project.csproj"),
			"<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.0</TargetFramework></PropertyGroup><ItemGroup><PackageReference Include=\"Nerdbank.Streams\" /></ItemGroup></Project>",
			TestContext.Current.CancellationToken);
		this.packagesProps = this.MSBuild.GetProject(Path.Combine(this.StagingDirectory, DirectoryPackagesPropsFileName));
	}

	[Fact]
	public async Task FixesDowngradeIssues()
	{
		// Introduce a downgrade issue.
		this.packagesProps.GetItemsByEvaluatedInclude("Nerdbank.Streams").Single().SetMetadataValue("Version", "2.9.112");

		this.Command = new()
		{
			ProjectPath = this.StagingDirectory,
			TargetFramework = "netstandard2.0",
		};
		await this.ExecuteCommandAsync();

		AssertPackageVersion(this.packagesProps, "System.IO.Pipelines", "6.0.3");
		Assert.False(Directory.Exists(Path.Combine(this.StagingDirectory, "nuget.frameworks")));
	}

	[Fact]
	public async Task DiscoversTargetFrameworks()
	{
		string projectsDirectory = Path.Combine(this.StagingDirectory, "Projects");
		Directory.CreateDirectory(projectsDirectory);
		string singleTargetProject = string.Join(
			Environment.NewLine,
			[
				"<Project Sdk=\"Microsoft.NET.Sdk\">",
				"  <PropertyGroup>",
				"    <TargetFramework>netstandard2.0</TargetFramework>",
				"  </PropertyGroup>",
				"</Project>",
			]);
		await File.WriteAllTextAsync(Path.Combine(projectsDirectory, "SingleTarget.csproj"), singleTargetProject, TestContext.Current.CancellationToken);

		string multiTargetProject = string.Join(
			Environment.NewLine,
			[
				"<Project Sdk=\"Microsoft.NET.Sdk\">",
				"  <PropertyGroup>",
				"    <TargetFrameworkList>net8.0;netstandard2.0</TargetFrameworkList>",
				"    <TargetFrameworks>$(TargetFrameworkList)</TargetFrameworks>",
				"  </PropertyGroup>",
				"</Project>",
			]);
		await File.WriteAllTextAsync(Path.Combine(projectsDirectory, "MultiTarget.csproj"), multiTargetProject, TestContext.Current.CancellationToken);

		// Introduce a downgrade issue that must be corrected for every discovered framework.
		this.packagesProps.GetItemsByEvaluatedInclude("Nerdbank.Streams").Single().SetMetadataValue("Version", "2.9.112");
		this.Command = new()
		{
			ProjectPath = projectsDirectory,
		};

		await this.ExecuteCommandAsync();

		AssertPackageVersion(this.packagesProps, "System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task UsesExplicitTargetFrameworks()
	{
		// Introduce a downgrade issue.
		this.packagesProps.GetItemsByEvaluatedInclude("Nerdbank.Streams").Single().SetMetadataValue("Version", "2.9.112");
		this.Command = new()
		{
			ProjectPath = this.StagingDirectory,
			TargetFrameworks = ["netstandard2.0", "net8.0", "net8.0"],
		};

		await this.ExecuteCommandAsync();

		AssertPackageVersion(this.packagesProps, "System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task DoesNotReportSyntheticCompatibilityErrors()
	{
		this.packagesProps.AddItem("PackageVersion", "Cake.Core").Single().SetMetadataValue("Version", "6.2.0");
		this.Command = new()
		{
			ProjectPath = this.StagingDirectory,
			TargetFrameworks = ["net472"],
		};

		await this.ExecuteCommandAsync();

		Assert.DoesNotContain("Cake.Core", ((StringWriter)this.Command.Error).ToString());
	}
}
