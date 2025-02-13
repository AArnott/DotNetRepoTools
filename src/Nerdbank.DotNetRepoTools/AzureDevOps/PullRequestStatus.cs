// Copyright (c) Andrew Arnott. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace Nerdbank.DotNetRepoTools.AzureDevOps;

internal enum PullRequestStatus
{
#pragma warning disable SA1602 // Enumeration items should be documented
	Abandoned,
	Active,
	Completed,
#pragma warning restore SA1602 // Enumeration items should be documented
}
