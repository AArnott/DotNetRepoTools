// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.CommandLine;
using System.Reflection;
using Microsoft.Build.Evaluation;

public abstract class TestBase : IAsyncLifetime
{
	private const string AssetsPrefix = "Assets.";

	static TestBase()
	{
		MSBuild.MSBuildLocator.EnsureLoaded();
	}

	public TestBase(ITestOutputHelper logger)
	{
		this.Logger = logger;
		this.StagingDirectory = Path.Combine(Path.GetTempPath(), "test_" + Path.GetRandomFileName());
		this.TempFiles.Add(this.StagingDirectory);
	}

	public string StagingDirectory { get; }

	public ITestOutputHelper Logger { get; }

	internal MSBuild MSBuild { get; } = new();

	internal TempFileCollection TempFiles { get; } = new();

	public virtual Task InitializeAsync()
	{
		return Task.CompletedTask;
	}

	public virtual Task DisposeAsync()
	{
		this.MSBuild.Dispose();
		this.TempFiles.Dispose();
		return Task.CompletedTask;
	}

	internal static Stream GetAsset(string assetName)
	{
		return Assembly.GetExecutingAssembly().GetManifestResourceStream($"{AssetsPrefix}{assetName.Replace('/', '.')}")
			?? throw new ArgumentException($"No resource named {assetName} found under the Assets directory of the test project.");
	}

	protected static void AssertPackageVersion(Project project, string id, string? version)
	{
		project.ReevaluateIfNecessary();
		try
		{
			Assert.Equal(version, MSBuild.FindItem(project, "PackageVersion", id)?.GetMetadataValue("Version"));
		}
		catch (Exception ex)
		{
			throw new Exception($"Failure while asserting version for package '{id}'.", ex);
		}
	}

	protected void DumpConsole(IConsole console)
	{
		if (console.Out.ToString() is { Length: > 0 } stdout)
		{
			this.Logger.WriteLine("Command STDOUT:");
			this.Logger.WriteLine(stdout);
		}

		if (console.Error.ToString() is { Length: > 0 } stderr)
		{
			this.Logger.WriteLine("Command STDERR:");
			this.Logger.WriteLine(stderr);
		}
	}

	protected Project SynthesizeMSBuildAsset(string assetName)
	{
		return this.MSBuild.SynthesizeVolatileProject(Path.Combine(this.StagingDirectory, assetName), GetAsset(assetName));
	}

	protected async Task SynthesizeAllMSBuildAssetsAsync()
	{
		foreach (string streamName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
		{
			if (streamName.StartsWith(AssetsPrefix, StringComparison.Ordinal))
			{
				string assetName = streamName.Substring(AssetsPrefix.Length);

				// Synthesize msbuild in memory isn't enough to satisfy recursive parent directory imports.
				////this.SynthesizeMSBuildAsset(assetName);
				await this.PlaceAssetAsync(assetName);
			}
		}
	}

	protected async Task<string> PlaceAssetAsync(string assetName, string? baseDirectory = null, CancellationToken cancellationToken = default)
	{
		baseDirectory ??= this.StagingDirectory;
		string targetFilePath = Path.Combine(baseDirectory, assetName);
		string? targetDirectory = Path.GetDirectoryName(targetFilePath);
		if (targetDirectory is not null)
		{
			Directory.CreateDirectory(targetDirectory);
		}

		using Stream assetStream = GetAsset(assetName);
		using FileStream targetFile = File.Create(targetFilePath);
		this.TempFiles.Add(targetFilePath);
		await assetStream.CopyToAsync(targetFile, cancellationToken);

		return targetFilePath;
	}
}
