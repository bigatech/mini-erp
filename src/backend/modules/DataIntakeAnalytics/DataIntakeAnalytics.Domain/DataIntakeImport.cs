namespace DataIntakeAnalytics.Domain;

public sealed record DataIntakeImport(Guid Id, string FileName, string MediaType, long FileSize, int RowCount, DateTimeOffset UploadedAt);
