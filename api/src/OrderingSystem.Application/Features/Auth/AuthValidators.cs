using FluentValidation;

namespace OrderingSystem.Application.Features.Auth;

/// <summary>
/// Called explicitly at the top of each use case rather than wired as an MVC filter, so validation
/// runs on the same path whether the caller is a controller, a background job or a test.
/// </summary>
public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(r => r.FullName).NotEmpty().MaximumLength(200);
        RuleFor(r => r.Phone).NotEmpty().MaximumLength(32);
        RuleFor(r => r.Password).ApplyPasswordRules();
    }
}

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        // Only presence is checked. Applying complexity rules to a login attempt would reject a
        // legitimate password set before the rules changed.
        RuleFor(r => r.Email).NotEmpty().MaximumLength(256);
        RuleFor(r => r.Password).NotEmpty();
    }
}

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator() =>
        RuleFor(r => r.Email).NotEmpty().EmailAddress().MaximumLength(256);
}

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(r => r.Token).NotEmpty();
        RuleFor(r => r.NewPassword).ApplyPasswordRules();
    }
}

public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(r => r.RefreshToken).NotEmpty();
}

internal static class PasswordRules
{
    /// <summary>
    /// Length first, because it buys more than symbol classes do. The upper bound exists because
    /// the hashing cost scales with input and an unbounded password is a cheap way to load the CPU.
    /// </summary>
    public static IRuleBuilderOptions<T, string> ApplyPasswordRules<T>(
        this IRuleBuilder<T, string> rule) =>
        rule.NotEmpty()
            .MinimumLength(10).WithMessage("Password must be at least 10 characters.")
            .MaximumLength(128)
            .Matches("[A-Za-z]").WithMessage("Password must contain a letter.")
            .Matches("[0-9]").WithMessage("Password must contain a digit.");
}
