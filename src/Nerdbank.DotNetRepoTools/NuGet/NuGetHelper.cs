// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using Microsoft.Build.Evaluation;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace Nerdbank.DotNetRepoTools.NuGet;

internal class NuGetHelper
{
	internal const string PackageVersionItemType = "PackageVersion";
	internal const string VersionMetadata = "Version";

	internal NuGetHelper(IConsole console, Project project)
	{
		this.Console = console;
		this.Project = project;
		this.NuGetSettings = Settings.LoadDefaultSettings(project.FullPath);
	}

	internal Project Project { get; }

	internal ISettings NuGetSettings { get; }

	internal IConsole Console { get; }

	internal SourceCacheContext SourceCacheContext { get; set; } = new()
	{
		IgnoreFailedSources = true,
	};

	internal PackageReference CreatePackageReference(string id, string version, NuGetFramework nugetFramework)
	{
		return new PackageReference(new PackageIdentity(id, null), nugetFramework, userInstalled: true, developmentDependency: false, requireReinstallation: false, VersionRange.Parse(version));
	}

	internal async Task<RestoreTargetGraph> GetRestoreTargetGraphAsync(IReadOnlyCollection<PackageReference> packages, List<NuGetFramework> targetFrameworks, CancellationToken cancellationToken)
	{
		// The package spec details what packages to restore
		PackageSpec packageSpec = new(targetFrameworks.Select(targetFramework => new TargetFrameworkInformation { FrameworkName = targetFramework }).ToList())
		{
			Dependencies = packages.Select(i => new LibraryDependency { LibraryRange = new LibraryRange(i.PackageIdentity.Id, i.AllowedVersions, LibraryDependencyTarget.Package) }).ToList(),
			RestoreMetadata = new ProjectRestoreMetadata
			{
				ProjectPath = this.Project.FullPath,
				ProjectName = Path.GetFileNameWithoutExtension(this.Project.FullPath),
				ProjectStyle = ProjectStyle.PackageReference,
				ProjectUniqueName = this.Project.FullPath,
				OutputPath = Path.GetTempPath(),
				OriginalTargetFrameworks = targetFrameworks.Select(i => i.ToString()).ToList(),
				ConfigFilePaths = this.NuGetSettings.GetConfigFilePaths(),
				PackagesPath = SettingsUtility.GetGlobalPackagesFolder(this.NuGetSettings),
				Sources = SettingsUtility.GetEnabledSources(this.NuGetSettings).ToList(),
				FallbackFolders = SettingsUtility.GetFallbackPackageFolders(this.NuGetSettings).ToList(),
			},
			FilePath = this.Project.FullPath,
			Name = Path.GetFileNameWithoutExtension(this.Project.FullPath),
		};

		DependencyGraphSpec dependencyGraphSpec = new DependencyGraphSpec();

		dependencyGraphSpec.AddProject(packageSpec);

		dependencyGraphSpec.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);

		IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), dependencyGraphSpec);

		RestoreArgs restoreArgs = new RestoreArgs
		{
			AllowNoOp = true,
			CacheContext = this.SourceCacheContext,
			CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(this.NuGetSettings)),
			Log = NullLogger.Instance,
		};

		cancellationToken.ThrowIfCancellationRequested();

		// Create requests from the arguments
		IReadOnlyList<RestoreSummaryRequest> requests = await requestProvider.CreateRequests(restoreArgs);
		cancellationToken.ThrowIfCancellationRequested();

		// Restore the package without generating extra files
		IReadOnlyList<RestoreResultPair> restoreResult = await RestoreRunner.RunWithoutCommit(requests, restoreArgs);

		RestoreTargetGraph? restoreTargetGraph = restoreResult[0].Result.RestoreGraphs.First();
		return restoreTargetGraph;
	}

	internal bool SetPackageVersion(string id, string version, bool addIfMissing = true, bool allowDowngrade = true)
	{
		bool changed = false;
		ProjectItem? item = MSBuild.FindItem(this.Project, PackageVersionItemType, id);
		if (item is null)
		{
			if (addIfMissing)
			{
				item = this.Project.AddItem(PackageVersionItemType, id).First();
			}
			else
			{
				return changed;
			}
		}

		string oldVersion = item.GetMetadataValue(VersionMetadata);
		VersionRange? oldVersionParsed = oldVersion.Length > 0 ? VersionRange.Parse(oldVersion) : null;
		VersionRange? newVersionParsed = VersionRange.Parse(version);
		if (allowDowngrade || oldVersionParsed is null || oldVersionParsed.MinVersion < newVersionParsed.MinVersion)
		{
			item.SetMetadataValue(VersionMetadata, version);
			changed = true;
		}

		this.Console.WriteLine(oldVersion.Length == 0 ? $"{id} {version}" : $"{id} {oldVersion} -> {version}");
		return changed;
	}

	internal async Task<int> CorrectDowngradeIssuesAsync(NuGetFramework framework, PackageReference? hypotheticalPackageReference, CancellationToken cancellationToken)
	{
		int versionsUpdated = 0;
		bool fixesApplied = true;
		while (fixesApplied)
		{
			cancellationToken.ThrowIfCancellationRequested();

			this.Console.WriteLine("Looking for package downgrade issues...");

			List<PackageReference> packageReferences = this.Project.GetItems(PackageVersionItemType)
				.Select(pv => this.CreatePackageReference(pv.EvaluatedInclude, pv.GetMetadataValue(VersionMetadata), framework)).ToList();

			if (hypotheticalPackageReference is not null)
			{
				packageReferences.Add(hypotheticalPackageReference);
			}

			RestoreTargetGraph restoreGraph = await this.GetRestoreTargetGraphAsync(packageReferences, new() { framework }, cancellationToken);

			fixesApplied = false;
			foreach (DowngradeResult<RemoteResolveResult> conflict in restoreGraph.AnalyzeResult.Downgrades)
			{
				if (this.SetPackageVersion(conflict.DowngradedFrom.Key.Name, conflict.DowngradedFrom.Key.VersionRange.OriginalString))
				{
					fixesApplied = true;
					versionsUpdated++;
				}
			}
		}

		return versionsUpdated;
	}
}
