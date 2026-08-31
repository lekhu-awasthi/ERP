namespace ErpApp.Application.Common.Exceptions;

/// <summary>
/// The tenant never opted into the feature this request needs (FR-2.6, enforced since Phase 20f
/// by FeatureGateBehavior). Maps to HTTP 403, alongside <see cref="ForbiddenException"/>: the
/// request is understood and well-formed, and authorization is refused -- it just fails on the
/// Organization's entitlements rather than the acting user's role. The two are distinguishable
/// by message: this one always names the specific feature. See phase-20f-status.md for the
/// 403-vs-409-vs-422 decision.
/// </summary>
public sealed class FeatureNotEnabledException(string message) : Exception(message);
