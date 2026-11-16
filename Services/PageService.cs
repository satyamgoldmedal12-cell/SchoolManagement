using SchoolManagement.Interfaces.RepositoryInterfaces;
using SchoolManagement.Interfaces.ServiceInterfaces;
using SchoolManagement.Models;

namespace SchoolManagement.Services;

public sealed class PageService : IPageService
{
    private readonly IPageRepository _pages;

    public PageService(IPageRepository pages)
    {
        _pages = pages;
    }

    public Task<IReadOnlyList<PageRegistrationModel>> ListAsync(CancellationToken cancellationToken)
    {
        return _pages.ListAsync(cancellationToken);
    }

    public async Task<OperationResult> CreateAsync(PageRegistrationModel page, int creatorSerial, CancellationToken cancellationToken)
    {
        if (!ModulePathIcons.Contains(page.PageSystemName))
        {
            var existing = page.PathId > 0
                ? (await _pages.ListAsync(cancellationToken)).FirstOrDefault(entry => entry.PathId == page.PathId)
                : null;

            if (existing?.PageSystemName != page.PageSystemName)
            {
                return OperationResult.Failure("Choose a menu icon from the list.");
            }
        }

        page.PageName = page.PageName.Trim();
        page.PathName = page.IsLeafNode ? page.PathName?.TrimEnd('/') : "#";
        return await _pages.CreateAsync(page, creatorSerial, cancellationToken);
    }

    public Task<OperationResult> DeleteAsync(int pathId, string? rowVersion, int creatorSerial, CancellationToken cancellationToken)
    {
        return _pages.DeleteAsync(pathId, rowVersion, creatorSerial, cancellationToken);
    }
}
