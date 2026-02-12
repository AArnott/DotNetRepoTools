// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics;
using System.Text.RegularExpressions;

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// Extracts Azure DevOps account, project, and repository information from a git remote URL.
/// </summary>
internal partial class AzDoRemoteInfo
{
	private AzDoRemoteInfo(string account, string project, string repo)
	{
		this.Account = account;
		this.Project = project;
		this.Repo = repo;
	}

	/// <summary>
	/// Gets the Azure DevOps account (organization) name.
	/// </summary>
	public string Account { get; }

	/// <summary>
	/// Gets the Azure DevOps project name.
	/// </summary>
	public string Project { get; }

	/// <summary>
	/// Gets the Azure DevOps repository name.
	/// </summary>
	public string Repo { get; }

	/// <summary>
	/// Gets the collection URI (e.g. <c>https://dev.azure.com/fabrikamfiber/</c>).
	/// </summary>
	public string CollectionUri => $"https://dev.azure.com/{this.Account}/";

	/// <summary>
	/// Attempts to parse an Azure DevOps remote URL and extract account, project, and repository information.
	/// </summary>
	/// <param name="remoteUrl">The git remote URL to parse.</param>
	/// <returns>An <see cref="AzDoRemoteInfo"/> instance if the URL is a recognized Azure DevOps URL; otherwise, <see langword="null"/>.</returns>
	public static AzDoRemoteInfo? TryParse(string? remoteUrl)
	{
		if (string.IsNullOrEmpty(remoteUrl))
		{
			return null;
		}

		// HTTPS: https://dev.azure.com/{org}/{project}/_git/{repo}
		Match match = DevAzureComHttpsPattern().Match(remoteUrl);
		if (match.Success)
		{
			return new AzDoRemoteInfo(match.Groups["org"].Value, match.Groups["project"].Value, match.Groups["repo"].Value);
		}

		// Legacy HTTPS: https://{org}.visualstudio.com/{project}/_git/{repo}
		// Also: https://{org}.visualstudio.com/DefaultCollection/{project}/_git/{repo}
		match = VisualStudioComHttpsPattern().Match(remoteUrl);
		if (match.Success)
		{
			return new AzDoRemoteInfo(match.Groups["org"].Value, match.Groups["project"].Value, match.Groups["repo"].Value);
		}

		// SSH: git@ssh.dev.azure.com:v3/{org}/{project}/{repo}
		match = SshPattern().Match(remoteUrl);
		if (match.Success)
		{
			return new AzDoRemoteInfo(match.Groups["org"].Value, match.Groups["project"].Value, match.Groups["repo"].Value);
		}

		return null;
	}

	/// <summary>
	/// Attempts to infer Azure DevOps information from the <c>origin</c> remote of the git repository
	/// that contains the current directory.
	/// </summary>
	/// <returns>An <see cref="AzDoRemoteInfo"/> instance if inference succeeded; otherwise, <see langword="null"/>.</returns>
	public static AzDoRemoteInfo? TryInferFromGitRemote()
	{
		try
		{
			string? repoRoot = CommandBase.FindGitRepoRoot();
			if (repoRoot is null)
			{
				return null;
			}

			ProcessStartInfo psi = new("git", "remote get-url origin")
			{
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				WorkingDirectory = repoRoot,
			};

			using Process? process = Process.Start(psi);
			if (process is null)
			{
				return null;
			}

			string url = process.StandardOutput.ReadToEnd().Trim();
			process.WaitForExit(5000);
			if (process.ExitCode != 0 || string.IsNullOrEmpty(url))
			{
				return null;
			}

			return TryParse(url);
		}
		catch
		{
			return null;
		}
	}

	[GeneratedRegex(@"^https?://(?:[^@]+@)?dev\.azure\.com/(?<org>[^/]+)/(?<project>[^/]+)/_git/(?:_optimized/)?(?<repo>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
	private static partial Regex DevAzureComHttpsPattern();

	[GeneratedRegex(@"^https?://(?:[^@]+@)?(?<org>[^.]+)\.visualstudio\.com/(?:DefaultCollection/)?(?<project>[^/]+)/_git/(?:_optimized/)?(?<repo>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
	private static partial Regex VisualStudioComHttpsPattern();

	[GeneratedRegex(@"^git@ssh\.dev\.azure\.com:v3/(?<org>[^/]+)/(?<project>[^/]+)/(?<repo>[^/]+?)(?:\.git)?/?$", RegexOptions.IgnoreCase)]
	private static partial Regex SshPattern();
}
