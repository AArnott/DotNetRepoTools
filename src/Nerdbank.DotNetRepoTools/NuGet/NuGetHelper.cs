// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Text.RegularExpressions;
using Microsoft;
using Microsoft.Build.Construction;
using Microsoft.Build.Evaluation;
using NuGet.Commands;
using NuGet.Commands.Restore;
using NuGet.Commands.Restore.Utility;
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
	private static readonly Regex VersionPropertyReference = new(@"^\$\(([\w_]+)\)$");
	private readonly MSBuild msbuild;

	internal NuGetHelper(MSBuild msbuild, string projectPath)
		: this(msbuild, OpenOrCreateSandboxProject(msbuild, projectPath))
	{
	}

	internal NuGetHelper(MSBuild msbuild, Project project)
	{
		this.msbuild = msbuild;
		this.Project = project;
		this.NuGetSettings = Settings.LoadDefaultSettings(project.FullPath);
	}

	/// <summary>
	/// Gets the output writer for the console.
	/// </summary>
	internal TextWriter Out { get; init; } = Console.Out;

	/// <summary>
	/// Gets the error writer for the console.
	/// </summary>
	internal TextWriter Error { get; init; } = Console.Error;

	internal Project Project { get; }

	internal ISettings NuGetSettings { get; }

	internal SourceCacheContext SourceCacheContext { get; set; } = new()
	{
		IgnoreFailedSources = true,
	};

	internal bool IsCpvmActive => string.Equals(this.Project.GetPropertyValue("ManagePackageVersionsCentrally"), "true", StringComparison.OrdinalIgnoreCase);

	internal bool VerifyCpvmActive()
	{
		if (!this.IsCpvmActive)
		{
			this.Error.WriteLine("Central package management is not active for this project, but this command requires this.");
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
		PackageSpec packageSpec = GetPackageSpec(this.Project.FullPath, this.NuGetSettings, packages, targetFrameworks) ?? throw new InvalidOperationException("Unable to generate a package spec for this project.");

		packageSpec.RestoreMetadata.Sources = [.. SettingsUtility.GetEnabledSources(this.NuGetSettings)];
		packageSpec.RestoreMetadata.FallbackFolders = [.. SettingsUtility.GetFallbackPackageFolders(this.NuGetSettings)];
#if NET10_0_OR_GREATER
		packageSpec.RestoreMetadata.RestoreAuditProperties.EnableAudit = bool.FalseString;
#else
		packageSpec.RestoreMetadata.RestoreAuditProperties = new RestoreAuditProperties
		{
			EnableAudit = bool.FalseString,
		};
#endif
		DependencyGraphSpec dependencyGraphSpec = new DependencyGraphSpec();

		dependencyGraphSpec.AddProject(packageSpec);

		dependencyGraphSpec.AddRestore(packageSpec.RestoreMetadata.ProjectUniqueName);

		IPreLoadedRestoreRequestProvider requestProvider = new DependencyGraphSpecRequestProvider(new RestoreCommandProvidersCache(), dependencyGraphSpec);

		RestoreArgs restoreArgs = new RestoreArgs
		{
			AllowNoOp = true,
			CacheContext = this.SourceCacheContext,
			CachingSourceProvider = new CachingSourceProvider(new PackageSourceProvider(this.NuGetSettings)),
			Log = new Logger(),
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
			this.Error.WriteLine($"{message.Message}");
		}

		foreach (LibraryRange issue in restoreTargetGraph.Unresolved)
		{
			this.Error.WriteLine($"Unresolved package: {issue.Name} {issue.VersionRange}");
		}

		return restoreTargetGraph;
	}

	internal bool SetPackageVersion(string id, string version, bool addIfMissing = true, bool allowDowngrade = true, HashSet<string>? disregardVersionProperties = null)
	{
		ProjectRootElement? directoryPackagesPropsXml = this.Project.Imports.FirstOrDefault(i => string.Equals(Path.GetFileName(i.ImportedProject.FullPath), "Directory.Packages.props", StringComparison.OrdinalIgnoreCase)).ImportedProject;
		if (directoryPackagesPropsXml is null)
		{
			this.Error.WriteLine($"Unable to find an imported Directory.Packages.props in your project. Unable to set {id} to {version}.");
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
				changed = true;
			}
			else
			{
				return false;
			}
		}

		string? nameOfChangedProperty = null;
		string oldVersion = item.GetMetadataValue(VersionMetadata);
		VersionRange? oldVersionParsed = oldVersion.Length > 0 ? VersionRange.Parse(oldVersion) : null;
		VersionRange? newVersionParsed = VersionRange.Parse(version);
		if (allowDowngrade || oldVersionParsed is null ||
		   (oldVersionParsed.MinVersion is not null && newVersionParsed.MinVersion is not null && oldVersionParsed.MinVersion < newVersionParsed.MinVersion))
		{
			ProjectMetadataElement? versionMetadata = item.Xml.Metadata.SingleOrDefault(m => string.Equals(m.Name, VersionMetadata, StringComparison.OrdinalIgnoreCase));
			if (versionMetadata is null)
			{
				versionMetadata = item.Xml.AddMetadata(VersionMetadata, ProjectCollection.Escape(version), expressAsAttribute: true);
			}
			else
			{
				if (VersionPropertyReference.Match(versionMetadata.Value) is Match { Success: true } match)
				{
					// This version is defined by an MSBuild property. Find that property definition and update it instead.
					string propertyName = match.Groups[1].Value;
					if (disregardVersionProperties is null || !disregardVersionProperties.Contains(propertyName))
					{
						ProjectPropertyElement propertyElement = this.Project.GetProperty(propertyName).Xml;
						if (this.msbuild.CanChangeFile(propertyElement.ContainingProject.FullPath, this.Project.FullPath))
						{
							propertyElement.Value = ProjectCollection.Escape(version);
							nameOfChangedProperty = propertyName;
						}
					}
				}

				if (nameOfChangedProperty is null)
				{
					versionMetadata.Value = ProjectCollection.Escape(version);
				}
			}

			changed = true;
		}

		if (changed)
		{
			this.Out.Write(id);
			this.Out.Write(oldVersion.Length == 0 ? $" {version}" : $" {oldVersion} -> {version}");
			if (nameOfChangedProperty is not null)
			{
				this.Out.Write($" ({nameOfChangedProperty})");
			}

			this.Out.WriteLine(string.Empty);
		}

		return changed;
	}

	internal async Task<int> CorrectDowngradeIssuesAsync(NuGetFramework framework, PackageReference? hypotheticalPackageReference, HashSet<string>? disregardVersionProperties, CancellationToken cancellationToken)
	{
		int versionsUpdated = 0;
		bool fixesApplied = true;
		while (fixesApplied)
		{
			cancellationToken.ThrowIfCancellationRequested();

			this.Out.WriteLine("Looking for package downgrade issues...");

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
				if (conflict.DowngradedFrom.Key.VersionRange?.OriginalString is string originalVersion &&
					this.SetPackageVersion(conflict.DowngradedFrom.Key.Name, originalVersion, disregardVersionProperties: disregardVersionProperties))
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

	private static PackageSpec? GetPackageSpec(string projectFullPath, ISettings settings, IReadOnlyCollection<PackageReference> packages, List<NuGetFramework> targetFrameworks)
	{
#if NET10_0_OR_GREATER
		IProject project = new RestoreProjectAdapter(packages, targetFrameworks)
		{
			Directory = Path.GetDirectoryName(projectFullPath)!,
			FullPath = projectFullPath,
		};

		PackageSpec? packageSpec = PackageSpecFactory.GetPackageSpec(project, settings);

		return packageSpec;
#else
		PackageSpec packageSpec = new(targetFrameworks.Select(targetFramework => new TargetFrameworkInformation { FrameworkName = targetFramework }).ToList())
		{
			Dependencies = packages.Select(i => new LibraryDependency { LibraryRange = new LibraryRange(i.PackageIdentity.Id, i.AllowedVersions, LibraryDependencyTarget.Package) }).ToList(),
			RestoreMetadata = new ProjectRestoreMetadata
			{
				ProjectPath = projectFullPath,
				ProjectName = Path.GetFileNameWithoutExtension(projectFullPath),
				ProjectStyle = ProjectStyle.PackageReference,
				ProjectUniqueName = projectFullPath,
				OutputPath = Path.GetTempPath(),
				OriginalTargetFrameworks = targetFrameworks.Select(i => i.ToString()).ToList(),
				ConfigFilePaths = settings.GetConfigFilePaths(),
				PackagesPath = SettingsUtility.GetGlobalPackagesFolder(settings),
				Sources = SettingsUtility.GetEnabledSources(settings).ToList(),
				FallbackFolders = SettingsUtility.GetFallbackPackageFolders(settings).ToList(),
			},
			FilePath = projectFullPath,
			Name = Path.GetFileNameWithoutExtension(projectFullPath),
		};

		return packageSpec;
#endif
	}
}
