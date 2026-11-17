using Microsoft.EntityFrameworkCore;

namespace TalkToRag.Infrastructure.Audit;

/// <summary>
/// Gives the talk_to_rag_auditor role the password from Audit:AuditorPassword. The migrations create the
/// role without one, so it cannot log in until a password is configured.
/// </summary>
public static class AuditorLogin
{
    public static void SetPassword(DbContext context, string? password)
    {
        if (string.IsNullOrEmpty(password))
            return;
        context.Database.OpenConnection();
        try
        {
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = AlterRole(password);
            command.ExecuteNonQuery();
        }
        finally
        {
            context.Database.CloseConnection();
        }
    }

    public static async Task SetPasswordAsync(DbContext context, string? password, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(password))
            return;
        await context.Database.OpenConnectionAsync(cancellationToken);
        try
        {
            await using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = AlterRole(password);
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        finally
        {
            await context.Database.CloseConnectionAsync();
        }
    }

    // ALTER ROLE takes no parameters, so the password goes in as a quoted literal.
    private static string AlterRole(string password) =>
        $"ALTER ROLE talk_to_rag_auditor PASSWORD '{password.Replace("'", "''")}'";
}
