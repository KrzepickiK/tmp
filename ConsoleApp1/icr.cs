public class ImportConsentsRepository : IImportConsentsRepository
{
    private readonly ConsentsContext _context;

    public ImportConsentsRepository(ConsentsContext context)
    {
        _context = context;
    }

    public async Task<List<ConsentIdentityEntry>> GetAsync(
        IEnumerable<(ConsentSubjectTypesDictionary subjectType, string subjectId,
                     ConsentObjectTypesDictionary objectType, string objectId)> data)
    {
        var tableParameter = new DataTable();
        tableParameter.Columns.Add("ConsentSubjectType", typeof(string));
        tableParameter.Columns.Add("ConsentSubject", typeof(string));
        tableParameter.Columns.Add("ConsentObjectType", typeof(string));
        tableParameter.Columns.Add("ConsentObject", typeof(string));

        foreach (var d in data)
            tableParameter.Rows.Add(d.subjectType.ToString(), d.subjectId, d.objectType.ToString(), d.objectId);

        var param = new SqlParameter("@keys", tableParameter)
        {
            SqlDbType = SqlDbType.Structured,
            TypeName = "dbo.ConsentKeyType"
        };

        var sql = @"SELECT ci.*
                    FROM dbo.ConsentIdentity ci
                    INNER JOIN @keys k
                      ON ci.ConsentSubjectType = k.ConsentSubjectType
                     AND ci.ConsentSubject = k.ConsentSubject
                     AND ci.ConsentObjectType = k.ConsentObjectType
                     AND ci.ConsentObject = k.ConsentObject";

        return await _context.ConsentIdentity
            .FromSqlRaw(sql, param)
            .AsNoTracking() // 🔥 KLUCZOWE
            .ToListAsync();
    }

    public async Task<List<ConsentEntry>> GetConsentsByIdentityIdsAsync(List<Guid> ids)
    {
        if (!ids.Any())
            return new List<ConsentEntry>();

        return await _context.ConsentEntries
            .Where(x => ids.Contains(x.ConsentIdentityId))
            .AsNoTracking()
            .ToListAsync();
    }

    public async Task AddConsentsAsync(params ConsentEntry[] consents)
    {
        await _context.ConsentEntries.AddRangeAsync(consents);
    }

    public async Task AddHistoryAsync(params ConsentEntry[] consents)
    {
        var history = consents.Select(c => new ConsentHistory
        {
            Consent = c,
            Created = DateTime.UtcNow
        });

        await _context.ConsentHistory.AddRangeAsync(history);
    }

    public async Task AddHistoryAsync(ConsentEntry consent)
    {
        var history = new ConsentHistory
        {
            Consent = consent,
            Created = DateTime.UtcNow
        };

        await _context.ConsentHistory.AddAsync(history);
    }

    public async Task SaveChangesAsync()
    {
        await _context.SaveChangesAsync();
    }

    public void DisableAutoDetectChanges()
    {
        _context.ChangeTracker.AutoDetectChangesEnabled = false;
    }

    public void EnableAutoDetectChanges()
    {
        _context.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    public void Clear()
    {
        _context.ChangeTracker.Clear(); // 🔥 KLUCZOWE
    }
}