using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Controllers;

[Authorize(Roles = AppRoles.Administrator)]

public class SubjectController : BaseController
{
    private readonly ISubjectService _subjectService;

    public SubjectController(ISubjectService subjectService)
    {
        _subjectService = subjectService;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var subjects = await _subjectService.GetSubjectsAsync(cancellationToken);
        return View(subjects);
    }

    [HttpGet]
    public IActionResult Create()
    {
        return View(new Subjectmodel());
    }

    [HttpPost]
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
