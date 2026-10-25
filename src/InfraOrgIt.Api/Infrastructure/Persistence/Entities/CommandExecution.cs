namespace InfraOrgIt.Api.Infrastructure.Persistence.Entities;

// Append-only audit row. ServerId has no FK: the history survives server deletion.
public sealed class CommandExecution
{
    public int Id { get; set; }
    public string Username { get; set; } = "";
    public int ServerId { get; set; }
    public string ServerName { get; set; } = "";
    public string Kind { get; set; } = "Action";
    public string Label { get; set; } = "";
    public string Script { get; set; } = "";
    public int? ExitCode { get; set; }
    public string Output { get; set; } = "";
    public DateTime StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public string? IdempotencyKey { get; set; }
}
