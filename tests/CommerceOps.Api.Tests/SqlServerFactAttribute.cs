using Xunit;

namespace CommerceOps.Api.Tests;

internal sealed class SqlServerFactAttribute : FactAttribute
{
    public SqlServerFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("COMMERCEOPS_TEST_SQLSERVER")))
        {
            Skip = "Requires SQL Server and separate database connections.";
        }
    }
}
