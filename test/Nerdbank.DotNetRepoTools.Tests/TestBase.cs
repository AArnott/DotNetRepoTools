// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Reflection;
using Microsoft.Build.Evaluation;

public abstract class TestBase : IAsyncLifetime
{
	private const string AssetsPrefix = "Assets/";

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

	public virtual ValueTask InitializeAsync()
	{
		return ValueTask.CompletedTask;
	}

	public virtual ValueTask DisposeAsync()
	{
		this.MSBuild.Dispose();
		this.TempFiles.Dispose();
		return ValueTask.CompletedTask;
	}

	internal static Stream GetAsset(string assetName)
	{
		return Assembly.GetExecutingAssembly().GetManifestResourceStream(TransformAssetNameToStreamName(assetName))
			?? throw new ArgumentException($"No resource named {assetName} found under the Assets directory of the test project.");
	}

	protected static void AssertPackageVersion(Project project, string id, string? version, bool compareUnevaluatedValue = false)
	{
		project.ReevaluateIfNecessary();
		try
		{
			ProjectMetadata? metadatum = MSBuild.FindItem(project, "PackageVersion", id)?.GetMetadata("Version");
			string? actual = compareUnevaluatedValue ? metadatum?.UnevaluatedValue : metadatum?.EvaluatedValue;
			Assert.Equal(version, actual);
		}
		catch (Exception ex)
		{
			throw new Exception($"Failure while asserting version for package '{id}'.", ex);
		}
	}

	protected void DumpConsole(StringWriter commandOutput, StringWriter commandError)
	{
		if (commandOutput.ToString() is { Length: > 0 } output)
		{
			this.Logger.WriteLine("Command STDOUT:");
			this.Logger.WriteLine(output);
		}

		if (commandError.ToString() is { Length: > 0 } error)
		{
			this.Logger.WriteLine("Command STDERR:");
			this.Logger.WriteLine(error);
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

	/// <summary>
	/// Writes one asset to disk for a test.
	/// </summary>
	/// <param name="assetName">The path to the asset, relative to the test project's Assets directory. If <see langword="null" />, <see cref="StagingDirectory"/> will be used.</param>
	/// <param name="baseDirectory">The path to the directory to save the asset to. If the asset is in a subdirectory under Assets, that relative path will be reconstructed onto this path.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The absolute path to the created file.</returns>
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

	/// <summary>
	/// Writes all assets to disk that are defined under a given directory.
	/// </summary>
	/// <param name="assetSubDirectory">The path to the subdirectory under Assets, relative to the test project's Assets directory.</param>
	/// <param name="baseDirectory">The path to the directory to save the files to. Any relative paths from the Assets directory will be reconstructed onto this path. If <see langword="null" />, <see cref="StagingDirectory"/> will be used.</param>
	/// <param name="cancellationToken">A cancellation token.</param>
	/// <returns>The absolute path to the base directory under which the assets were created.</returns>
	protected async Task<string> PlaceAssetsAsync(string assetSubDirectory, string? baseDirectory = null, CancellationToken cancellationToken = default)
	{
		baseDirectory ??= this.StagingDirectory;

		string streamNamePrefix = TransformAssetNameToStreamName(assetSubDirectory);
		foreach (string embeddedStreamName in Assembly.GetExecutingAssembly().GetManifestResourceNames())
		{
			if (!embeddedStreamName.StartsWith(streamNamePrefix, StringComparison.Ordinal))
			{
				continue;
			}

			string targetFilePath = Path.Combine(baseDirectory, embeddedStreamName.Substring(AssetsPrefix.Length));
			string? targetDirectory = Path.GetDirectoryName(targetFilePath);
			if (targetDirectory is not null)
			{
				Directory.CreateDirectory(targetDirectory);
			}

			using Stream assetStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(embeddedStreamName)!;
			using FileStream targetFile = File.Create(targetFilePath);
			this.TempFiles.Add(targetFilePath);
			await assetStream.CopyToAsync(targetFile, cancellationToken);
		}

		return baseDirectory;
	}

	protected void LogFileContent(string path)
	{
		this.Logger.WriteLine($"Content of file: {path}");
		this.Logger.WriteLine("-----------------");
		this.Logger.WriteLine(File.ReadAllText(path));
		this.Logger.WriteLine("-----------------");
		this.Logger.WriteLine(string.Empty);
	}

	private static string TransformAssetNameToStreamName(string assetName) => $"{AssetsPrefix}{assetName.Replace('\\', '/')}";
}
