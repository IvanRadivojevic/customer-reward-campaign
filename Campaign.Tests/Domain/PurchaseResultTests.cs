namespace Campaign.Tests.Domain;

using Campaign.Core.Domain;

/// <summary>
/// The invariants here are the same ones the database will carry as CHECK constraints, so a row
/// that cannot exist in the model cannot exist in the table either.
/// </summary>
public class PurchaseResultTests
{
    [Fact]
    public void An_invalid_row_keeps_the_raw_line_and_the_error_but_no_parsed_field()
    {
        var row = PurchaseResult.Invalid(Guid.NewGuid(), Guid.NewGuid(), 7, "x;not-a-date;;", "PurchaseDate is not a date.");

        Assert.Equal(MatchStatus.Invalid, row.MatchStatus);
        Assert.Equal("x;not-a-date;;", row.RawLine);
        Assert.Null(row.CustomerExternalId);
        Assert.Null(row.PurchaseDate);
        Assert.Null(row.Amount);
        Assert.Null(row.Currency);
        Assert.Null(row.MatchedGrantId);
    }

    [Fact]
    public void An_amount_without_a_currency_is_rejected()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() => PurchaseResult.Unmatched(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "1,2026-09-14,149.90,",
            "1",
            new DateOnly(2026, 9, 14),
            amount: 149.90m,
            currency: null));

        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void A_currency_without_an_amount_is_rejected()
    {
        var error = Assert.Throws<DomainRuleViolationException>(() => PurchaseResult.Unmatched(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "1,2026-09-14,,EUR",
            "1",
            new DateOnly(2026, 9, 14),
            amount: null,
            currency: "EUR"));

        Assert.Equal(DomainErrorCodes.ValidationFailed, error.Code);
    }

    [Fact]
    public void A_row_without_an_amount_and_without_a_currency_is_accepted()
    {
        var row = PurchaseResult.Unmatched(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "9,2026-09-14",
            "9",
            new DateOnly(2026, 9, 14),
            amount: null,
            currency: null);

        Assert.Equal(MatchStatus.Unmatched, row.MatchStatus);
        Assert.Null(row.MatchedGrantId);
    }

    [Fact]
    public void A_matched_row_points_at_the_grant_it_converted()
    {
        var grantId = Guid.NewGuid();

        var row = PurchaseResult.Matched(
            Guid.NewGuid(),
            Guid.NewGuid(),
            1,
            "1,2026-09-14,149.90,EUR",
            "1",
            new DateOnly(2026, 9, 14),
            149.90m,
            "EUR",
            grantId);

        Assert.Equal(MatchStatus.Matched, row.MatchStatus);
        Assert.Equal(grantId, row.MatchedGrantId);
    }
}
