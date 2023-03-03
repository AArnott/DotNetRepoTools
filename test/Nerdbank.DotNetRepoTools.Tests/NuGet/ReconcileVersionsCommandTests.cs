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

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();

		this.packagesProps = this.SynthesizeMSBuildAsset("Directory.Packages.props");
	}

	[Fact]
	public async Task FixesDowngradeIssues()
	{
		// Introduce a downgrade issue.
		this.packagesProps.GetItemsByEvaluatedInclude("Nerdbank.Streams").Single().SetMetadataValue("Version", "2.9.112");

		this.Command = new()
		{
			ProjectPath = this.packagesProps.FullPath,
			TargetFramework = "netstandard2.0",
		};
		await this.Command.ExecuteAsync();

		AssertPackageVersion(this.packagesProps, "System.IO.Pipelines", "6.0.3");
	}
}
