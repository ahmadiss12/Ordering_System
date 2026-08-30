namespace OrderingSystem.Application.Features.Menu;

public sealed record CreateCategoryRequest(string Name, int SortOrder);
public sealed record UpdateCategoryRequest(string Name, int SortOrder, bool IsActive);
public sealed record CategoryResponse(Guid Id, string Name, int SortOrder, bool IsActive);

public sealed record CreateMenuItemRequest(
    Guid CategoryId, string Name, string? Description, decimal BasePriceUsd, int SortOrder);

public sealed record UpdateMenuItemRequest(
    Guid CategoryId, string Name, string? Description, decimal BasePriceUsd, int SortOrder);

public sealed record SetAvailabilityRequest(bool IsAvailable);

public sealed record MenuItemResponse(
    Guid Id, Guid CategoryId, string Name, string? Description,
    decimal BasePriceUsd, string? ImageUrl, bool IsAvailable, int SortOrder);

public sealed record CreateOptionGroupRequest(string Name, int MinSelect, int? MaxSelect, int SortOrder);
public sealed record UpdateOptionGroupRequest(string Name, int MinSelect, int? MaxSelect, int SortOrder);

public sealed record OptionGroupResponse(
    Guid Id, string Name, int MinSelect, int? MaxSelect, int SortOrder,
    IReadOnlyList<OptionResponse> Options);

public sealed record CreateOptionRequest(string Name, decimal PriceDeltaUsd, int MaxQuantity, int SortOrder);
public sealed record UpdateOptionRequest(string Name, decimal PriceDeltaUsd, int MaxQuantity, bool IsAvailable, int SortOrder);

public sealed record OptionResponse(
    Guid Id, string Name, decimal PriceDeltaUsd, int MaxQuantity, bool IsAvailable, int SortOrder);

/// <summary>
/// One group as it applies to one item, for the editor.
/// <para>
/// Unlike the customer-facing projection, the overrides arrive unresolved as well as resolved.
/// The editor has to show whether this item inherits the group's own bounds or overrides them,
/// and a single resolved number cannot say which — an item showing "pick 1" would give no way to
/// tell a deliberate override from the group's default, and saving would silently freeze the
/// inherited value into an override.
/// </para>
/// </summary>
public sealed record AttachedOptionGroupResponse(
    Guid OptionGroupId, string Name, int SortOrder,
    int? MinSelectOverride, int? MaxSelectOverride,
    int EffectiveMinSelect, int? EffectiveMaxSelect,
    IReadOnlyList<OptionResponse> Options);

/// <summary>
/// Attaches a shared group to one item. The two overrides are why a group can be shared at all —
/// null inherits the group's own bound, a number applies to this item alone.
/// </summary>
public sealed record AttachOptionGroupRequest(
    Guid OptionGroupId, int SortOrder, int? MinSelectOverride, int? MaxSelectOverride);
