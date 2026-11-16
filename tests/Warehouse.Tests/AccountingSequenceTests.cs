namespace Warehouse.Tests;

public sealed class AccountingSequenceTests
{
    [Theory]
    [InlineData(17)]
    [InlineData(71)]
    [InlineData(1701)]
    public void Mixed_sequences_match_an_independent_quantity_ledger_and_conserve_value(int seed)
    {
        var w = new World();
        var random = new Random(seed);
        var sites = new[] { w.A.Id, w.B.Id };
        var products = new[] { w.P.Id, w.Q.Id };
        // The oracle counts quarter-units as integers; all input prices are exactly 8.
        var expected = new int[2, 2];
        long nextNumber = 1;
        for (var step = 0; step < 150; step++)
        {
            var site = random.Next(2);
            var product = random.Next(2);
            var quarters = random.Next(1, 10);
            var quantity = quarters / 4m;
            var operation = random.Next(3);
            var kind = operation switch { 0 => DocumentKind.Receipt, 1 => DocumentKind.Issue, _ => DocumentKind.Transfer };
            var draft = w.Documents.Create(kind, sites[site], [new(products[product], quantity, operation == 0 ? 8 : null)],
                operation == 2 ? sites[1 - site] : null);
            if (operation != 0 && expected[site, product] < quarters)
            {
                Check.Error(ErrorCode.InsufficientStock, () => w.Documents.Post(draft.Id));
            }
            else
            {
                var posted = w.Documents.Post(draft.Id);
                Assert.Equal(nextNumber++, posted.Number);
                var before = (int[,])expected.Clone();
                expected[site, product] += operation == 0 ? quarters : -quarters;
                if (operation == 2) expected[1 - site, product] += quarters;
                if (random.Next(4) == 0)
                {
                    w.Documents.Reverse(posted.Id);
                    expected = before;
                }
            }
            for (var warehouse = 0; warehouse < 2; warehouse++)
                for (var item = 0; item < 2; item++)
                {
                    var stock = w.Stock(sites[warehouse], products[item]);
                    Assert.Equal(expected[warehouse, item] / 4m, stock.Quantity);
                    Assert.Equal(expected[warehouse, item] * 2m, stock.Value);
                    var history = w.Read(s => s.Movements.All()).Where(x => x.Key == new StockKey(sites[warehouse], products[item])).ToArray();
                    Assert.Equal(expected[warehouse, item] / 4m, history.Sum(x => x.Quantity));
                    Assert.Equal(expected[warehouse, item] * 2m, history.Sum(x => x.Value));
                }
        }
    }

    [Fact]
    public void Reversal_of_issue_requires_reactivating_archived_references()
    {
        var w = new World();
        w.Receipt(1, 2);
        var issue = w.Issue(1);
        w.Catalog.SetProductActive(w.P.Id, false);
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Reverse(issue.Id));
        w.Catalog.SetProductActive(w.P.Id, true);
        w.Catalog.UpdateWarehouse(w.A.Id, "A", false);
        Check.Error(ErrorCode.InvalidState, () => w.Documents.Reverse(issue.Id));
        w.Catalog.UpdateWarehouse(w.A.Id, "A", true);
        w.Documents.Reverse(issue.Id);
        Assert.Equal((1m, 2m), (w.Stock().Quantity, w.Stock().Value));
    }

    [Fact]
    public void Role_changes_take_effect_for_existing_identity_without_reauthentication()
    {
        var w = new World();
        w.Accounts.UpdateUser(w.Keeper, Role.Auditor, true);
        w.Identity.UserId = w.Keeper;
        Check.Error(ErrorCode.Forbidden, () => w.Receipt(1, 1));
        Assert.Single(w.Inventories.Start(w.A.Id, [w.P.Id]).Lines);
    }
}
