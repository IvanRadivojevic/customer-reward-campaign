namespace Campaign.Tests.Domain;

using Campaign.Core.Domain;

public class ImportBatchTests
{
    private static readonly string Sha256 = new('a', ImportBatch.Sha256HexLength);

    [Fact]
    public void A_batch_without_invalid_rows_is_completed()
    {
        var batch = Create(rowsTotal: 10, rowsMatched: 7, rowsUnmatched: 3, rowsInvalid: 0);

        Assert.Equal(ImportBatchStatus.Completed, batch.Status);
    }

    [Fact]
    public void Unmatched_rows_alone_do_not_make_a_batch_faulty()
    {
        var batch = Create(rowsTotal: 5, rowsMatched: 0, rowsUnmatched: 5, rowsInvalid: 0);

        Assert.Equal(ImportBatchStatus.Completed, batch.Status);
    }

    [Fact]
    public void A_batch_with_a_row_that_could_not_be_read_is_completed_with_errors()
    {
        var batch = Create(rowsTotal: 10, rowsMatched: 6, rowsUnmatched: 3, rowsInvalid: 1);

        Assert.Equal(ImportBatchStatus.CompletedWithErrors, batch.Status);
    }

    [Fact]
    public void Every_row_has_to_end_up_in_exactly_one_outcome()
    {
        var error = Assert.Throws<DomainRuleViolationException>(
            () => Create(rowsTotal: 10, rowsMatched: 6, rowsUnmatched: 3, rowsInvalid: 0));

        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }

    private static ImportBatch Create(int rowsTotal, int rowsMatched, int rowsUnmatched, int rowsInvalid) =>
        ImportBatch.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "purchases.csv",
            Sha256,
            new DateTimeOffset(2026, 9, 15, 8, 0, 0, TimeSpan.Zero),
            "integration",
            rowsTotal,
            rowsMatched,
            rowsUnmatched,
            rowsInvalid);
}
