// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

/// <summary>
/// The various states of a pull request thread.
/// </summary>
public enum PullRequestCommentState
{
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#pragma warning disable SA1602 // Enumeration items should be documented
	Unknown = 0,
	Active = 1,
	Resolved = 2,
	WontFix = 3,
	Closed = 4,
	Pending = 6,
}
