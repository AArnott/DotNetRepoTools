// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace DotNetRepoTools.NuGet;

internal class NuGetHelper
{
	internal NuGetHelper(ISettings settings)
	{
		this.NugetSettings = settings;
	}

	internal ISettings NugetSettings { get; }

	internal async Task<RestoreTargetGraph?> GetRestoreTargetGraphAsync(IReadOnlyCollection<PackageReference> packages, string projectPath, List<NuGetFramework> targetFrameworks, SourceCacheContext sourceCacheContext, CancellationToken cancellationToken)
	{
		// The package spec details what packages to restore
		PackageSpec packageSpec = new PackageSpec(targetFrameworks.Select(i => new TargetFrameworkInformation
		{
			FrameworkName = i,
		}).ToList())
		{
			Dependencies = packages.Select(i => new LibraryDependency
			{
				LibraryRange = new LibraryRange(i.PackageIdentity.Id, new VersionRange(i.PackageIdentity.Version), LibraryDependencyTarget.Package),
			}).ToList(),
			RestoreMetadata = new ProjectRestoreMetadata
			{
				ProjectPath = projectPath,
				ProjectName = Path.GetFileNameWithoutExtension(projectPath),
				ProjectStyle = ProjectStyle.PackageReference,
				ProjectUniqueName = projectPath,
				OutputPath = Path.GetTempPath(),
				OriginalTargetFrameworks = targetFrameworks.Select(i => i.ToString()).ToList(),
				ConfigFilePaths = this.NugetSettings.GetConfigFilePaths(),
				PackagesPath = SettingsUtility.GetGlobalPackagesFolder(this.NugetSettings),
				Sources = SettingsUtility.GetEnabledSources(this.NugetSettings).ToList(),
				FallbackFolders = SettingsUtility.GetFallbackPackageFolders(this.NugetSettings).ToList(),
			},
			FilePath = projectPath,
			Name = Path.GetFileNameWithoutExtension(projectPath),
		};

		DependencyGraphSpec dependencyGraphSpec = new DependencyGraphSpec();

		dependencyGraphSpec.AddProject(packageSpec);

		dependencyGraphSpec.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);

		IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), dependencyGraphSpec);

		RestoreArgs restoreArgs = new RestoreArgs
		{
			AllowNoOp = true,
			CacheContext = sourceCacheContext,
			CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(this.NugetSettings)),
			Log = NullLogger.Instance,
		};

		cancellationToken.ThrowIfCancellationRequested();

		// Create requests from the arguments
		IReadOnlyList<RestoreSummaryRequest> requests = await requestProvider.CreateRequests(restoreArgs);
		cancellationToken.ThrowIfCancellationRequested();

		// Restore the package without generating extra files
		RestoreResultPair? restoreResult = (await RestoreRunner.RunWithoutCommit(requests, restoreArgs)).FirstOrDefault();

		RestoreTargetGraph? restoreTargetGraph = restoreResult?.Result.RestoreGraphs.FirstOrDefault();
		return restoreTargetGraph;
	}
}
