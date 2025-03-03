// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// The merge strategy to use when merging a pull request.
/// </summary>
internal enum GitPullRequestMergeStrategy
{
	/// <summary>
	/// A two-parent, no-fast-forward merge. The source branch is unchanged. This is the default behavior.
	/// </summary>
	NoFastForward,

	/// <summary>
	/// Rebase the source branch on top of the target branch HEAD commit, and fast-forward the target branch. The source branch is updated during the rebase operation.
	/// </summary>
	Rebase,

	/// <summary>
	/// Rebase the source branch on top of the target branch HEAD commit, and create a two-parent, no-fast-forward merge. The source branch is updated during the rebase operation.
	/// </summary>
	RebaseMerge,

	/// <summary>
	/// Put all changes from the pull request into a single-parent commit.
	/// </summary>
	Squash,
}
