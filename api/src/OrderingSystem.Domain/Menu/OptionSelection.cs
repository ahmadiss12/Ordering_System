namespace OrderingSystem.Domain.Menu;

/// <summary>
/// What one group demands of one item, after any per-item override has been resolved.
/// </summary>
/// <param name="OptionGroupId">The group these bounds belong to.</param>
/// <param name="Name">Shown to the customer, so it appears in the error they read.</param>
/// <param name="MinSelect">How many must be chosen. Zero means the group is optional.</param>
/// <param name="MaxSelect">How many may be chosen, or null for no limit.</param>
public readonly record struct GroupBounds(Guid OptionGroupId, string Name, int MinSelect, int? MaxSelect);

/// <summary>One option a customer picked, alongside what the menu currently says about it.</summary>
public readonly record struct PickedOption(
    Guid OptionId,
    Guid OptionGroupId,
    string Name,
    int Quantity,
    int MaxQuantity,
    bool IsAvailable);

/// <summary>A single thing wrong with a selection, named so a form can point at it.</summary>
/// <param name="Field">The group's name, or "options" when it is not about one group.</param>
public sealed record SelectionError(string Field, string Message);

/// <summary>
/// Whether a set of chosen options is a thing the customer is actually allowed to order.
///
/// <para>
/// Phase 2 built the screen where a restaurant declares these rules. This is where they are
/// enforced, and it has to be here rather than in the browser: the storefront can be reasoned
/// with, and a request built by hand cannot.
/// </para>
/// <para>
/// Pure, like the order state machine, and for the same reason — the rules are the interesting
/// part and they should be testable without a database in the way.
/// </para>
/// </summary>
public static class OptionSelection
{
    /// <summary>
    /// Every problem with the selection, or an empty list when there is none.
    ///
    /// <para>
    /// All of them, not the first: a customer who picked a sold-out sauce and forgot to choose a
    /// size should be told both at once rather than discovering the second after fixing the first.
    /// </para>
    /// </summary>
    public static IReadOnlyList<SelectionError> Validate(
        IReadOnlyCollection<GroupBounds> groups,
        IReadOnlyCollection<PickedOption> picked)
    {
        ArgumentNullException.ThrowIfNull(groups);
        ArgumentNullException.ThrowIfNull(picked);

        var errors = new List<SelectionError>();
        var known = groups.ToDictionary(g => g.OptionGroupId);

        foreach (var option in picked)
        {
            // An option from a group this item does not offer. Either the menu changed under the
            // customer, or the request was not built by our storefront.
            if (!known.TryGetValue(option.OptionGroupId, out var group))
            {
                errors.Add(new SelectionError("options",
                    $"{option.Name} is not one of the choices for this item."));
                continue;
            }

            if (!option.IsAvailable)
            {
                errors.Add(new SelectionError(group.Name,
                    $"{option.Name} is not available right now."));
            }

            if (option.Quantity < 1)
            {
                errors.Add(new SelectionError(group.Name,
                    $"Choose at least one {option.Name}, or leave it out."));
            }
            else if (option.Quantity > option.MaxQuantity)
            {
                errors.Add(new SelectionError(group.Name,
                    $"You can add at most {option.MaxQuantity} × {option.Name}."));
            }
        }

        // The same option twice on one line. The database's composite key would refuse it, but a
        // constraint violation is not a sentence anybody can act on.
        foreach (var duplicate in picked.GroupBy(p => p.OptionId).Where(g => g.Count() > 1))
        {
            errors.Add(new SelectionError("options",
                $"{duplicate.First().Name} was chosen more than once. Use the quantity instead."));
        }

        foreach (var group in groups)
        {
            // Distinct options, not the sum of their quantities: "choose 2 sauces" means two
            // different sauces, while quantity is what expresses double cheese.
            var chosen = picked
                .Where(p => p.OptionGroupId == group.OptionGroupId)
                .Select(p => p.OptionId)
                .Distinct()
                .Count();

            if (chosen < group.MinSelect)
            {
                errors.Add(new SelectionError(group.Name, TooFew(group)));
            }
            else if (group.MaxSelect is { } max && chosen > max)
            {
                errors.Add(new SelectionError(group.Name,
                    max == 1
                        ? $"Choose only one from {group.Name}."
                        : $"Choose at most {max} from {group.Name}."));
            }
        }

        return errors;
    }

    private static string TooFew(GroupBounds group) =>
        group.MinSelect == group.MaxSelect
            ? group.MinSelect == 1
                ? $"Choose one from {group.Name}."
                : $"Choose {group.MinSelect} from {group.Name}."
            : $"Choose at least {group.MinSelect} from {group.Name}.";
}
