// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

public class UpgradeCommandTests : TestBase
{
	private string packagesPropsPath = null!;

	public UpgradeCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();

		this.packagesPropsPath = await this.PlaceAssetAsync("Directory.Packages.props");
	}

	[Fact]
	public async Task IncludesTransitiveDependencies()
	{
		UpgradeCommand command = new()
		{
			PackageId = "Nerdbank.Streams",
			PackageVersion = "2.9.112",
			TargetFramework = "netstandard2.0",
			DirectoryPackagesPropsPath = this.packagesPropsPath,
		};

		Project newPackagesProps = await this.ExecuteAsync(command);
		AssertPackageVersion(newPackagesProps, "System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task OnlyTransitiveDependencies()
	{
		// As a test sanity check, ensure the package we're updating doesn't even appear, since we're testing that we can update transitive dependencies
		// via a meta-package that isn't referenced.
		Assert.Null(MSBuild.FindItem(this.MSBuild.EvaluateProjectFile(this.packagesPropsPath), "PackageVersion", "StreamJsonRpc"));

		UpgradeCommand command = new()
		{
			PackageId = "StreamJsonRpc",
			PackageVersion = "2.13.33",
			TargetFramework = "netstandard2.0",
			DirectoryPackagesPropsPath = this.packagesPropsPath,
		};

		Project newPackagesProps = await this.ExecuteAsync(command);
		AssertPackageVersion(newPackagesProps, "Nerdbank.Streams", "2.9.109");
		AssertPackageVersion(newPackagesProps, "System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task ExplodeTransitiveDependencies()
	{
		UpgradeCommand command = new()
		{
			PackageId = "StreamJsonRpc",
			PackageVersion = "2.13.33",
			TargetFramework = "netstandard2.0",
			DirectoryPackagesPropsPath = this.packagesPropsPath,
			Explode = true,
		};

		Project newPackagesProps = await this.ExecuteAsync(command);
		AssertPackageVersion(newPackagesProps, "StreamJsonRpc", "2.13.33");
		AssertPackageVersion(newPackagesProps, "Microsoft.VisualStudio.Threading", "17.1.46");
		AssertPackageVersion(newPackagesProps, "Microsoft.VisualStudio.Validation", "17.0.53");
	}

	private static void AssertPackageVersion(Project project, string id, string? version)
	{
		try
		{
			Assert.Equal(version, MSBuild.FindItem(project, "PackageVersion", id)?.GetMetadataValue("Version"));
		}
		catch (Exception ex)
		{
			throw new Exception($"Failure while asserting version for package '{id}'.", ex);
		}
	}

	private async Task<Project> ExecuteAsync(UpgradeCommand command)
	{
		Project newPackagesProps = this.MSBuild.EvaluateProjectFile(command.DirectoryPackagesPropsPath);
		bool topLevelItemDefined = MSBuild.FindItem(newPackagesProps, "PackageVersion", command.PackageId) is not null;

		await command.ExecuteAsync();
		this.DumpConsole(command.Console);

		this.MSBuild.CloseAll();
		newPackagesProps = this.MSBuild.EvaluateProjectFile(command.DirectoryPackagesPropsPath);

		// Assert that the package version itself was updated, if it existed previously.
		// If it did not exist previously, we should *not* have added it.
		AssertPackageVersion(newPackagesProps, command.PackageId, topLevelItemDefined || command.Explode ? command.PackageVersion : null);

		return newPackagesProps;
	}
}
