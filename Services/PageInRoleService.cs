using SchoolManagement.Models;
using SchoolManagement.Repositories;

namespace SchoolManagement.Services;

public sealed class PageInRoleService(PageInRoleRepository repository)
{
    public Task<IReadOnlyList<PageRightsTarget>> ListTargetsAsync(
        PageRightsType type, CancellationToken cancellationToken)
    {
        return repository.ListTargetsAsync(type, cancellationToken);
    }

    public async Task<PageInRoleViewModel> SearchAsync(
        PageRightsType? type, int? targetId, CancellationToken cancellationToken)
    {
        if (type is null)
        {
            return new PageInRoleViewModel(null, targetId, [], [], false);
        }

        var targets = await repository.ListTargetsAsync(type.Value, cancellationToken);
        if (targetId is null || !targets.Any(target => target.Id == targetId))
        {
            return new PageInRoleViewModel(type, targetId, targets, [], false);
        }

        var pages = await repository.ListPagesAsync(type.Value, targetId.Value, cancellationToken);
        var pageIds = pages.Select(page => page.PathId).ToHashSet();
        foreach (var page in pages)
        {
            if (page.HierarchyId == page.PathId || !pageIds.Contains(page.HierarchyId))
            {
                page.HierarchyId = 0;
            }
        }

        return new PageInRoleViewModel(type, targetId, targets, pages, true);
    }

    public Task<string> SaveAsync(
        PageRightsUpdate update, string actorId, Guid? sessionId, CancellationToken cancellationToken)
    {
        return repository.SaveAsync(update, actorId, sessionId, cancellationToken);
    }
}
