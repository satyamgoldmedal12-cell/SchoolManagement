using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public class ActiveUserSignInManager : SignInManager<ApplicationUser>
{
    private readonly ISchoolRepository _schoolRepository;

    public ActiveUserSignInManager(
        UserManager<ApplicationUser> userManager,
        IHttpContextAccessor contextAccessor,
        IUserClaimsPrincipalFactory<ApplicationUser> claimsFactory,
        IOptions<IdentityOptions> optionsAccessor,
        ILogger<SignInManager<ApplicationUser>> logger,
        IAuthenticationSchemeProvider schemes,
        IUserConfirmation<ApplicationUser> confirmation,
        ISchoolRepository schoolRepository)
        : base(userManager, contextAccessor, claimsFactory, optionsAccessor, logger, schemes, confirmation)
    {
        _schoolRepository = schoolRepository;
    }

    public override async Task<bool> CanSignInAsync(ApplicationUser user)
    {
        if (!user.IsActive || user.DeletedAt is not null || !user.DesignationId.HasValue)
        {
            return false;
        }

        var designation = await _schoolRepository.FindDesignationAsync(user.DesignationId.Value);
        if (designation is null || !designation.IsActive)
        {
            return false;
        }

        return await base.CanSignInAsync(user);
    }
}
