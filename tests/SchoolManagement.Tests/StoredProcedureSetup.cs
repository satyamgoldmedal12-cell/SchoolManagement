namespace SchoolManagement.Tests;

internal static class StoredProcedureSetup
{
    public static async Task ApplyAsync(TestDatabase database)
    {
        foreach (var file in new[] { "20261103_LoginArea.sql", "20261103_LoginGeoLocation.sql", "20261103_StoredProcedures.sql", "20261103_DapperIdentity.sql" })
            await database.ExecuteScriptAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, file)));
    }
}
