using FluentValidation;

namespace OrderingSystem.Application.Features.Restaurants;

public sealed class InviteStaffRequestValidator : AbstractValidator<InviteStaffRequest>
{
    public InviteStaffRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);

        // Required even though an existing account keeps its own name, because at the moment of
        // typing nobody knows which case this is — the address may belong to no account at all,
        // and then this is the only name the new one will ever have.
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);

        RuleFor(r => r.Phone).MaximumLength(32);

        // A role outside the enum would otherwise be stored as its number and read back as
        // neither Staff nor Owner, which the screens would draw as a blank.
        RuleFor(r => r.StaffRole).IsInEnum();
    }
}

public sealed class SetStaffRoleRequestValidator : AbstractValidator<SetStaffRoleRequest>
{
    public SetStaffRoleRequestValidator() => RuleFor(r => r.StaffRole).IsInEnum();
}
