// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

public class ReconcileVersionsCommandTests : CommandTestBase<ReconcileVersionsCommand>
{
	private string packagesPropsPath = null!;

	public ReconcileVersionsCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();

		this.packagesPropsPath = await this.PlaceAssetAsync("Directory.Packages.props");
	}

	[Fact]
	public async Task NoChangesRequired()
	{
		DateTime originalTimestamp = File.GetLastWriteTimeUtc(this.packagesPropsPath);
		this.Command = new()
		{
			ProjectPath = this.packagesPropsPath,
			TargetFramework = "netstandard2.0",
		};
		await this.Command.ExecuteAsync();

		// Assert that the file was not re-saved when no changes were required.
		Assert.Equal(originalTimestamp, File.GetLastWriteTimeUtc(this.packagesPropsPath));
	}

	[Fact]
	public async Task FixesDowngradeIssues()
	{
		// Introduce a downgrade issue.
		Project project = this.MSBuild.EvaluateProjectFile(this.packagesPropsPath);
		project.GetItemsByEvaluatedInclude("Nerdbank.Streams").Single().SetMetadataValue("Version", "2.9.112");
		project.Save();

		this.Command = new()
		{
			ProjectPath = this.packagesPropsPath,
			TargetFramework = "netstandard2.0",
		};
		await this.Command.ExecuteAsync();

		this.MSBuild.CloseAll();
		project = this.MSBuild.EvaluateProjectFile(this.packagesPropsPath);

		AssertPackageVersion(project, "System.IO.Pipelines", "6.0.3");
	}
}
