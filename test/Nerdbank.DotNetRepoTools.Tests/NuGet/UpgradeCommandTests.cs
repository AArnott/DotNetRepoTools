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

	private static void AssertPackageVersion(Project project, string id, string version)
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
		await command.ExecuteAsync();
		this.DumpConsole(command.Console);

		Project newPackagesProps = this.MSBuild.EvaluateProjectFile(command.DirectoryPackagesPropsPath);
		AssertPackageVersion(newPackagesProps, command.PackageId, command.PackageVersion);
		return newPackagesProps;
	}
}
