using Microsoft.AspNetCore.Identity;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public class AdministrationBootstrapper
{
    private readonly IAccountRepository _accounts;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IUserService _userService;
    private readonly ITemporaryPasswordGenerator _passwordGenerator;

    public AdministrationBootstrapper(
        IAccountRepository accounts,
        UserManager<ApplicationUser> userManager,
        IUserService userService,
        ITemporaryPasswordGenerator passwordGenerator)
    {
        _accounts = accounts;
        _userManager = userManager;
        _userService = userService;
        _passwordGenerator = passwordGenerator;
    }

    public async Task RunAsync(string email, bool createAdministrator)
    {
        if (createAdministrator)
        {
            var created = await _accounts.CreateAdministratorAsync(email, _passwordGenerator.Generate());
            if (!created.Succeeded)
            {
                throw new InvalidOperationException(string.Join(" ", created.Errors));
            }
            Console.WriteLine("Administrator created. Sending the temporary login email.");
        }

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
        {
            throw new InvalidOperationException("No user has this email address.");
        }

        if (!user.IsActive || user.DeletedAt is not null)
        {
            throw new InvalidOperationException("Cannot send an invitation to an inactive user.");
        }

        var result = await _userService.SendAccessEmailAsync(user.Id, user.Id);
        if (!result.Succeeded || result.Warning is not null)
        {
            var message = result.Warning ?? string.Join(" ", result.Errors);
            throw new InvalidOperationException(message);
        }

        Console.WriteLine("Login email sent. A temporary password must be changed on first login.");
    }
}
