using System.ComponentModel.DataAnnotations;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SchoolManagement.Configuration;
using SchoolManagement.Data;
using SchoolManagement.Services;

var builder = WebApplication.CreateBuilder(args);

// 1. Register MVC, application services and access policies.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute());
});

builder.Services.AddSchoolManagement(
    builder.Configuration,
    builder.Environment.ContentRootPath,
    builder.Environment.IsDevelopment());

builder.Services.AddAuthorization(options =>
{
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build();

    options.AddPolicy("PasswordSetup", policy =>
    {
        policy.AddAuthenticationSchemes(PasswordSetupAuthentication.Scheme);
        policy.RequireAuthenticatedUser();
    });
});

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("password-help", context =>
    {
        var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";

        return RateLimitPartition.GetFixedWindowLimiter(ipAddress, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(15),
            QueueLimit = 0
        });
    });
});

var app = builder.Build();

// 2. Handle database commands and exit without starting the website.
bool printSchema = args.Contains("--print-schema");
bool initializeDatabase = args.Contains("--initialize-database");

if (printSchema || initializeDatabase)
{
    using var scope = app.Services.CreateScope();
    var database = scope.ServiceProvider.GetRequiredService<DatabaseSetup>();

    if (printSchema)
    {
        var schema = database.ReadSchema();
        Console.WriteLine(schema);
    }
    else
    {
        bool databaseCreated = await database.InitializeAsync();
        if (databaseCreated)
        {
            Console.WriteLine("SchoolManagement database initialized.");
        }
        else
        {
            Console.WriteLine("Database already has tables. Apply the SQL scripts in Database/ to upgrade it.");
        }
    }

    return;
}

// 3. Handle administrator creation or invitation commands.
int bootstrapIndex = Array.IndexOf(args, "--bootstrap-admin");
int resendIndex = Array.IndexOf(args, "--resend-invitation");

if (bootstrapIndex >= 0 || resendIndex >= 0)
{
    bool createAdministrator = bootstrapIndex >= 0;

    int commandIndex;
    if (createAdministrator)
    {
        commandIndex = bootstrapIndex;
    }
    else
    {
        commandIndex = resendIndex;
    }

    if (args.Length <= commandIndex + 1)
    {
        throw new ArgumentException("Provide an email address after the administration command.");
    }

    var email = args[commandIndex + 1];
    var emailValidator = new EmailAddressAttribute();
    if (!emailValidator.IsValid(email))
    {
        throw new ArgumentException("Provide an email address after the administration command.");
    }

    using var scope = app.Services.CreateScope();
    var bootstrapper = scope.ServiceProvider.GetRequiredService<AdministrationBootstrapper>();
    await bootstrapper.RunAsync(email, createAdministrator);

    return;
}

// 4. Configure the HTTP request pipeline. The order of these calls matters.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.Use(async (context, next) =>
{
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await next();
});

app.UseHttpsRedirection();
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// 5. Map the routes and start the website.
app.MapStaticAssets().AllowAnonymous();
app.MapControllers();
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();

// Allows the integration tests to start the application.
public partial class Program
{
}
