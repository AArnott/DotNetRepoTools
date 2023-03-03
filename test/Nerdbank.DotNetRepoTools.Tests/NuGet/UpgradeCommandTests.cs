// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

public class UpgradeCommandTests : TestBase
{
	public UpgradeCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	[Fact]
	public async Task IncludesTransitiveDependencies()
	{
		string packagesPropsPath = await this.PlaceAssetAsync("Directory.Packages.props");
		UpgradeCommand command = new()
		{
			PackageId = "Nerdbank.Streams",
			PackageVersion = "2.9.112",
			TargetFramework = "netstandard2.0",
			DirectoryPackagesPropsPath = packagesPropsPath,
		};

		Project newPackagesProps = await this.ExecuteAsync(command);
		AssertPackageVersion(newPackagesProps, "System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task OnlyTransitiveDependencies()
	{
		string packagesPropsPath = await this.PlaceAssetAsync("Directory.Packages.props");
		UpgradeCommand command = new()
		{
			PackageId = "StreamJsonRpc",
			PackageVersion = "2.13.33",
			TargetFramework = "netstandard2.0",
			DirectoryPackagesPropsPath = packagesPropsPath,
		};

		Project newPackagesProps = await this.ExecuteAsync(command);
		AssertPackageVersion(newPackagesProps, "Nerdbank.Streams", "2.9.109");
		AssertPackageVersion(newPackagesProps, "System.IO.Pipelines", "6.0.3");
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
		AssertPackageVersion(newPackagesProps, command.PackageId, topLevelItemDefined ? command.PackageVersion : null);

		return newPackagesProps;
	}
}
