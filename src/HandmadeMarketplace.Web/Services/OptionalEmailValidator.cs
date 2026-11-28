using HandmadeMarketplace.Infrastructure;
using Microsoft.AspNetCore.Identity;

namespace HandmadeMarketplace.Web.Services;

public sealed class OptionalEmailValidator : IUserValidator<AppUser>
{
    public async Task<IdentityResult> ValidateAsync(UserManager<AppUser> manager, AppUser user)
    {
        var email = await manager.GetEmailAsync(user);
        if (email is null && user.PhoneNumberConfirmed && user.VerifiedMobile is not null)
            return IdentityResult.Success;

        if (string.IsNullOrWhiteSpace(email) || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(email))
            return IdentityResult.Failed(manager.ErrorDescriber.InvalidEmail(email));

        var owner = await manager.FindByEmailAsync(email);
        return owner is null || await manager.GetUserIdAsync(owner) == await manager.GetUserIdAsync(user)
            ? IdentityResult.Success : IdentityResult.Failed(manager.ErrorDescriber.DuplicateEmail(email));
    }
}
