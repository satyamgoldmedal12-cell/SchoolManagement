using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Services;
using SchoolManagement.Data;
using SchoolManagement.Repositories;
using SchoolManagement.Models;

namespace SchoolManagement.Configuration;

public static class ServiceRegistration
{
    public static IServiceCollection AddSchoolManagement(this IServiceCollection services,
        IConfiguration configuration, string contentRoot, bool development)
    {
        var dataPath = Path.Combine(contentRoot, "App_Data");
        Directory.CreateDirectory(dataPath);
        var protection = services.AddDataProtection().SetApplicationName("SchoolManagement")
            .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
        if (OperatingSystem.IsWindows()) protection.ProtectKeysWithDpapi();

        services.AddSingleton<SqlConnectionFactory>();
        services.AddScoped<SqlSession>();
        services.AddScoped<DatabaseSetup>();
        services.AddIdentity<ApplicationUser, ApplicationRole>(options =>
        {
            options.User.RequireUniqueEmail = true;
            options.Password.RequiredLength = 8;
            options.Lockout.MaxFailedAccessAttempts = 5;
            options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
        }).AddUserStore<DapperUserStore>().AddRoleStore<DapperRoleStore>()
            .AddSignInManager<ActiveUserSignInManager>().AddDefaultTokenProviders();
        services.AddAuthentication().AddCookie(PasswordSetupAuthentication.Scheme, options =>
        {
            options.Cookie.Name = "SchoolManagement.PasswordSetup";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(10);
            options.SlidingExpiration = false;
            options.LoginPath = "/Account/Login";
        });
        services.Configure<DataProtectionTokenProviderOptions>(options => options.TokenLifespan = TimeSpan.FromHours(1));
        services.Configure<SecurityStampValidatorOptions>(options => options.ValidationInterval = TimeSpan.Zero);
        services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Account/Login";
            options.AccessDeniedPath = "/Account/AccessDenied";
            options.Cookie.Name = "SchoolManagement.Auth";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromMinutes(30);
            options.SlidingExpiration = true;
            options.Events.OnValidatePrincipal = async context =>
            {
                var history = context.HttpContext.RequestServices.GetRequiredService<ILoginHistoryService>();
                var sessionId = context.Principal?.FindFirstValue(LoginHistoryConstants.SessionClaim);
                var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);

                await SecurityStampValidator.ValidatePrincipalAsync(context);
                if (context.Principal?.Identity?.IsAuthenticated != true)
                {
                    await history.EndSessionAsync(sessionId, userId, LoginHistoryConstants.Revoked);
                    return;
                }

                var users = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
                var signIn = context.HttpContext.RequestServices.GetRequiredService<SignInManager<ApplicationUser>>();
                var user = await users.GetUserAsync(context.Principal);
                if (user is null || user.MustChangePassword || !await signIn.CanSignInAsync(user))
                {
                    await history.EndSessionAsync(sessionId, userId, LoginHistoryConstants.Revoked);
                    context.RejectPrincipal();
                    await signIn.SignOutAsync();
                    return;
                }

                await history.ValidateSessionAsync(context, sessionId);
            };
        });
        services.AddSingleton(TimeProvider.System);
        services.Configure<SmtpSettings>(configuration.GetSection("Smtp"));
        services.Configure<ApplicationSettings>(configuration.GetSection("Application"));
        services.Configure<GeocodingSettings>(configuration.GetSection("Geocoding"));
        services.AddHttpClient<IGeoLocationService, GeoLocationService>(client => client.Timeout = TimeSpan.FromSeconds(3))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false })
            .RemoveAllLoggers();
        services.AddScoped<IEmailSender, SmtpEmailSender>();
        services.AddScoped<PasswordLinkService>();
        services.AddScoped<AdministrationBootstrapper>();
        services.AddScoped<ISchoolRepository, SchoolRepository>();
        services.AddScoped<UserRepository>();
        services.AddScoped<IUserRepository>(provider => provider.GetRequiredService<UserRepository>());
        services.AddScoped<IAccountRepository, AccountRepository>();
        services.AddScoped<ILoginHistoryRepository, LoginHistoryRepository>();
        services.AddScoped<ILoginHistoryService, LoginHistoryService>();
        services.AddScoped<ILoginMetadataService, LoginMetadataService>();
        services.AddSingleton<ILoginAreaService, LoginAreaService>();
        services.AddHostedService<LoginHistoryCleanupService>();
        services.AddScoped<INotificationService, NotificationService>();
        services.AddSingleton<ITemporaryPasswordGenerator, TemporaryPasswordGenerator>();
        services.AddScoped<IUserService, UserService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<ICatalogService, CatalogService>();
        services.AddScoped<ISubjectRepositories, SubjectRepositories>();
        services.AddScoped<ISubjectService, SubjectService>();
        services.AddScoped<IClassesRepository, ClassesRepository>();
        services.AddScoped<IClassesService, ClassesService>();
        services.AddScoped<IPageRepository, PageRepository>();
        services.AddScoped<IPageService, PageService>();
        return services;
    }
}
