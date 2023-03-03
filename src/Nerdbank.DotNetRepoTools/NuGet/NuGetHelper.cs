// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.CommandLine.IO;
using Microsoft;
using Microsoft.Build.Construction;
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

	internal NuGetHelper(MSBuild msbuild, IConsole console, string projectPath)
		: this(console, OpenOrCreateSandboxProject(msbuild, projectPath))
	{
	}

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

	internal bool IsCpvmActive => string.Equals(this.Project.GetPropertyValue("ManagePackageVersionsCentrally"), "true", StringComparison.OrdinalIgnoreCase);

	internal bool VerifyCpvmActive()
	{
		if (!this.IsCpvmActive)
		{
			this.Console.Error.WriteLine("Central package management is not active for this project, but this command requires this.");
			return false;
		}

		return true;
	}

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

		RestoreResult restoreResultResult = restoreResult[0].Result;
		RestoreTargetGraph restoreTargetGraph = restoreResultResult.RestoreGraphs.First();

		foreach (IAssetsLogMessage message in restoreResultResult.LogMessages)
		{
			this.Console.Error.WriteLine($"{message.Message}");
		}

		foreach (LibraryRange issue in restoreTargetGraph.Unresolved)
		{
			this.Console.Error.WriteLine($"Unresolved package: {issue.Name} {issue.VersionRange}");
		}

		return restoreTargetGraph;
	}

	internal bool SetPackageVersion(string id, string version, bool addIfMissing = true, bool allowDowngrade = true)
	{
		ProjectRootElement? directoryPackagesPropsXml = this.Project.Imports.FirstOrDefault(i => string.Equals(Path.GetFileName(i.ImportedProject.FullPath), "Directory.Packages.props", StringComparison.OrdinalIgnoreCase)).ImportedProject;
		if (directoryPackagesPropsXml is null)
		{
			this.Console.Error.WriteLine($"Unable to find an imported Directory.Packages.props in your project. Unable to set {id} to {version}.");
			return false;
		}

		bool changed = false;
		ProjectItem? item = MSBuild.FindItem(this.Project, PackageVersionItemType, id);
		if (item is null)
		{
			if (addIfMissing)
			{
				directoryPackagesPropsXml.AddItem(PackageVersionItemType, ProjectCollection.Escape(id));
				this.Project.ReevaluateIfNecessary();
				item = MSBuild.FindItem(this.Project, PackageVersionItemType, id);
				Assumes.NotNull(item);
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
			if (item.Xml.ContainingProject == directoryPackagesPropsXml)
			{
				ProjectMetadataElement? versionMetadata = item.Xml.Metadata.SingleOrDefault(m => string.Equals(m.Name, VersionMetadata, StringComparison.OrdinalIgnoreCase));
				if (versionMetadata is null)
				{
					versionMetadata = item.Xml.AddMetadata(VersionMetadata, ProjectCollection.Escape(version), expressAsAttribute: true);
				}
				else
				{
					versionMetadata.Value = ProjectCollection.Escape(version);
				}

				changed = true;
			}
			else
			{
				this.Console.Error.WriteLine($"PackageVersion for {id} was defined in unsupported file \"{item.Xml.ContainingProject.FullPath}\".");
			}
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

			this.Project.ReevaluateIfNecessary();
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

	private static Project OpenOrCreateSandboxProject(MSBuild msbuild, string path)
	{
		Project project;
		if (File.Exists(path))
		{
			project = msbuild.GetProject(path);
		}
		else
		{
			project = msbuild.CreateSandboxProject(path);
			msbuild.FillWithPackageReferences(project);
		}

		return project;
	}
}
