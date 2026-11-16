using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Authorization;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Services;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize]

public class SubjectController : BaseController
{
    private readonly ISubjectService _subjectService;

    public SubjectController(ISubjectService subjectService)
    {
        _subjectService = subjectService;
    }

    [HttpGet]
    [Authorize(Policy = PageAccessRequirement.ReadPolicy)]
    public async Task<IActionResult> Index([FromServices] PageAccessService pageAccess, CancellationToken cancellationToken)
    {
        ViewData["CanAdd"] = await pageAccess.CanAccessAsync(User, ControllerContext.ActionDescriptor.ControllerName,
            requireAdd: true, cancellationToken);
        var subjects = await _subjectService.GetSubjectsAsync(cancellationToken);
        return View(subjects);
    }

    [HttpGet]
    [Authorize(Policy = PageAccessRequirement.AddPolicy)]
    public IActionResult Create()
    {
        return View(new Subjectmodel());
    }

    [HttpPost]
    [Authorize(Policy = PageAccessRequirement.AddPolicy)]
    public async Task<IActionResult> Create(Subjectmodel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var result = await _subjectService.CreateSubject(model, CurrentUserId, cancellationToken);

        if (!result.Succeeded)
        {
            AddModelErrors(result);
            return View(model);
        }

        ShowResultMessage(result, "Subject created.");
        return RedirectToAction(nameof(Index));
    }
}
