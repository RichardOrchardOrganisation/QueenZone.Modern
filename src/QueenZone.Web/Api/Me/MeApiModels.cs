using QueenZone.Data;

namespace QueenZone.Web;

public sealed record MemberProfileLimitsDto(
    int MinDisplayNameLength,
    int MaxDisplayNameLength,
    long MaxAvatarBytes,
    IReadOnlyList<string> AllowedAvatarContentTypes,
    int DeletionRetentionDays);

public sealed record LegacyMatchDto(int UserId, string Username);

public sealed record LegacyLinkDto(
    LegacyAccountLinkKind Kind,
    LegacyMatchDto? Match,
    IReadOnlyList<LegacyMatchDto> ClaimableMatches,
    IReadOnlyList<LegacyMatchDto> UnavailableMatches);

public sealed record AccountDeletionInfoDto(
    string ConfirmationPhrase,
    string ConfirmationHint,
    string RequestedTitle,
    string RequestedMessage,
    IReadOnlyList<string> WhatHappens);

public sealed record MemberProfileDto(
    Guid MemberId,
    string Email,
    string DisplayName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    bool HasAvatar,
    string? AvatarPath,
    string? AvatarThumbPath,
    MemberMessagePrivacy MessagePrivacy,
    IReadOnlyList<string> LinkedProviders,
    LegacyLinkDto LegacyLink,
    DateTimeOffset? ScheduledDeletionAt,
    MemberProfileLimitsDto Limits,
    AccountDeletionInfoDto Deletion);

public sealed record MemberProfilePatchRequest(
    string? DisplayName,
    MemberMessagePrivacy? MessagePrivacy);

public sealed record ClaimLegacyRequest(
    int? LegacyUserId,
    bool AdoptDisplayName = true);

public sealed record DeletionRequestBody(string? Confirmation, bool Immediate = false);

public sealed record DeletionRequestedResponse(
    bool Requested,
    DateTimeOffset? ScheduledDeletionAt,
    string Title,
    string Message,
    string? StatusReceipt = null);

public sealed record DeletionProgressResponse(string Status);
