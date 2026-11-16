using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
public class AccountController : BaseController
{
    private readonly IAccountService _accountService;
    private readonly ILoginMetadataService _loginMetadata;

    public AccountController(IAccountService accountService, ILoginMetadataService loginMetadata)
    {
        _accountService = accountService;
        _loginMetadata = loginMetadata;
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
        {
            return RedirectAfterSignIn(returnUrl);
        }

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _accountService.LoginAsync(model.Email, model.Password, model.RememberMe, model.Device);
        if (!result.Succeeded)
        {
            ModelState.AddModelError(string.Empty, result.Error ?? "Unable to log in.");
            return View(model);
        }

        if (result.RequiresPasswordChange)
        {
            return RedirectToAction(nameof(ChangeTemporaryPassword));
        }

        return RedirectAfterSignIn(model.ReturnUrl);
    }

    [Authorize(Policy = "PasswordSetup")]
    [HttpGet]
    public IActionResult ChangeTemporaryPassword()
    {
        var device = _loginMetadata.MergePendingDevice(User, null);
        return View(new ChangeTemporaryPasswordViewModel { Device = device });
    }

    [Authorize(Policy = "PasswordSetup")]
    [HttpPost]
    [EnableRateLimiting("password-help")]
    public async Task<IActionResult> ChangeTemporaryPassword(ChangeTemporaryPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _accountService.ChangeTemporaryPasswordAsync(model.Password, model.Device);
        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(model);
        }

        TempData["Success"] = "Your new password is saved. Welcome to School Management.";
        return RedirectToAction("Index", "Home");
    }

    [AllowAnonymous]
    [HttpPost]
    public async Task<IActionResult> CancelPasswordSetup()
    {
        await _accountService.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult Register()
    {
        return NotFound();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPassword()
    {
        return View(new ForgotPasswordViewModel());
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("password-help")]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        await _accountService.RequestPasswordResetAsync(model.Email);
        return RedirectToAction(nameof(ForgotPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPassword(ResetPasswordLinkViewModel link)
    {
        if (!ModelState.IsValid)
        {
            return BadRequest();
        }

        var model = new ResetPasswordViewModel
        {
            UserId = link.UserId,
            Code = link.Code
        };
        return View(model);
    }

    [AllowAnonymous]
    [HttpPost]
    [EnableRateLimiting("password-help")]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _accountService.ResetPasswordAsync(model.UserId, model.Code, model.Password);
        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(model);
        }

        return RedirectToAction(nameof(ResetPasswordConfirmation));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        await _accountService.SignOutAsync();
        return RedirectToAction(nameof(Login));
    }

    [AllowAnonymous]
    [HttpGet]
    public IActionResult AccessDenied()
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        return View();
    }

    private IActionResult RedirectAfterSignIn(string? returnUrl)
    {
        if (Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl!);
        }

        return RedirectToAction("Index", "Home");
    }
}
