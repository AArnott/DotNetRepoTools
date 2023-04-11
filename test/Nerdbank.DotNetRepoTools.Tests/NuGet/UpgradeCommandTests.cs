// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft;
using Microsoft.Build.Evaluation;
using Nerdbank.DotNetRepoTools.NuGet;

namespace NuGet;

public class UpgradeCommandTests : CommandTestBase<UpgradeCommand>
{
	private Project consumingProj = null!;
	private Project packagesProps = null!;

	public UpgradeCommandTests(ITestOutputHelper logger)
		: base(logger)
	{
	}

	public override async Task InitializeAsync()
	{
		await base.InitializeAsync();

		await this.SynthesizeAllMSBuildAssetsAsync();
		this.consumingProj = this.MSBuild.SynthesizeVolatileProject(Path.Combine(this.StagingDirectory, "repotools.csproj"));
		this.packagesProps = this.MSBuild.GetProject(Path.Combine(this.StagingDirectory, DirectoryPackagesPropsFileName));
	}

	[Fact]
	public async Task IncludesTransitiveDependencies()
	{
		this.Command = new()
		{
			PackageId = "Nerdbank.Streams",
			PackageVersion = "2.9.112",
			TargetFramework = "netstandard2.0",
			Path = this.StagingDirectory,
		};

		await this.ExecuteCommandAsync();
		this.AssertPackageVersion("System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task OnlyTransitiveDependencies()
	{
		// As a test sanity check, ensure the package we're updating doesn't even appear, since we're testing that we can update transitive dependencies
		// via a meta-package that isn't referenced.
		Assert.Null(MSBuild.FindItem(this.consumingProj, "PackageVersion", "StreamJsonRpc"));

		this.Command = new()
		{
			PackageId = "StreamJsonRpc",
			PackageVersion = "2.13.33",
			TargetFramework = "netstandard2.0",
			Path = this.StagingDirectory,
		};

		await this.ExecuteCommandAsync();
		this.AssertPackageVersion("Nerdbank.Streams", "2.9.109");
		this.AssertPackageVersion("System.IO.Pipelines", "6.0.3");
	}

	[Fact]
	public async Task ExplodeTransitiveDependencies()
	{
		this.Command = new()
		{
			PackageId = "StreamJsonRpc",
			PackageVersion = "2.13.33",
			TargetFramework = "netstandard2.0",
			Path = this.StagingDirectory,
			Explode = true,
		};

		await this.ExecuteCommandAsync();
		this.AssertPackageVersion("StreamJsonRpc", "2.13.33");
		this.AssertPackageVersion("Microsoft.VisualStudio.Threading", "17.1.46");
		this.AssertPackageVersion("Microsoft.VisualStudio.Validation", "17.0.53");
	}

	[Fact]
	public async Task ExplodeTransitiveDependencies_DoesNotDowngradeAnything()
	{
		// Introduce a downgrade issue.
		this.packagesProps.AddItem("PackageVersion", "Newtonsoft.Json").Single().SetMetadataValue("Version", "13.0.2");

		this.Command = new()
		{
			PackageId = "StreamJsonRpc",
			PackageVersion = "2.13.33",
			TargetFramework = "netstandard2.0",
			Path = this.StagingDirectory,
			Explode = true,
		};

		await this.ExecuteCommandAsync();
		this.AssertPackageVersion("StreamJsonRpc", "2.13.33");
		this.AssertPackageVersion("Newtonsoft.Json", "13.0.2");
	}

	[Theory, PairwiseData]
	public async Task PreserveMSBuildVersionProperties(bool preserveProperties)
	{
		HashSet<string> disregardVersionProperties = new(StringComparer.OrdinalIgnoreCase);
		disregardVersionProperties.Add(preserveProperties ? "ABC" : "NerdbankStreamsVersion");
		this.Command = new()
		{
			PackageId = "Nerdbank.Streams",
			PackageVersion = "2.9.112",
			TargetFramework = "netstandard2.0",
			Path = this.StagingDirectory,
			DisregardVersionProperties = disregardVersionProperties,
		};

		await this.ExecuteCommandAsync();

		// Assert that the version was effectively changed.
		this.AssertPackageVersion("Nerdbank.Streams", "2.9.112");

		// Assert that it was done without removing the property reference.
		this.AssertPackageVersion("Nerdbank.Streams", preserveProperties ? "$(NerdbankStreamsVersion)" : "2.9.112", compareUnevaluatedValue: true);
	}

	protected override async Task ExecuteCommandAsync()
	{
		Verify.Operation(this.Command is not null, $"Set {nameof(this.Command)} first.");
		bool topLevelItemDefined = MSBuild.FindItem(this.consumingProj, "PackageVersion", this.Command.PackageId) is not null;

		await base.ExecuteCommandAsync();

		this.packagesProps.ReevaluateIfNecessary();
		this.consumingProj.ReevaluateIfNecessary();

		// Assert that the package version itself was updated, if it existed previously.
		// If it did not exist previously, we should *not* have added it.
		this.AssertPackageVersion(this.Command.PackageId, topLevelItemDefined || this.Command.Explode ? this.Command.PackageVersion : null);
	}

	protected void AssertPackageVersion(string id, string? version, bool compareUnevaluatedValue = false) => AssertPackageVersion(this.consumingProj, id, version, compareUnevaluatedValue);
}
