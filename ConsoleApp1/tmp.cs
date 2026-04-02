public async Task ImportMarketingConsentsAsync(IEnumerable<ConsentToImport> consents)
{
    const int batchSize = 100;

    foreach (var batch in consents
                 .Distinct(new ConsentToImportComparer())
                 .Chunk(batchSize))
    {
        using var context = _contextFactory.CreateDbContext();

        context.ChangeTracker.AutoDetectChangesEnabled = false;

        // 1. Przygotuj klucze
        var keys = batch.Select(x => new
        {
            SubjectType = x.ConsentKey.ConsentSubjectType.GetValueOrDefault(),
            Subject = x.ConsentKey.ConsentSubjectId,
            ObjectType = x.ConsentKey.ConsentObjectType.GetValueOrDefault(),
            Object = x.ConsentKey.ConsentObjectId
        }).ToList();

        // 2. Pobierz identity (BEZ Include!)
        var identities = await context.ConsentIdentity
            .Where(ci => keys.Any(k =>
                k.Subject == ci.ConsentSubject &&
                k.SubjectType == ci.ConsentSubjectType &&
                k.Object == ci.ConsentObject &&
                k.ObjectType == ci.ConsentObjectType))
            .AsNoTracking()
            .ToListAsync();

        // 3. Dictionary (O(1))
        var identityDict = identities.ToDictionary(
            x => (x.ConsentSubject, x.ConsentSubjectType, x.ConsentObject, x.ConsentObjectType)
        );

        // 4. Pobierz consent entries osobno
        var identityIds = identities.Select(x => x.Id).ToList();

        var existingConsents = await context.ConsentEntries
            .Where(x => identityIds.Contains(x.ConsentIdentityId))
            .AsNoTracking()
            .ToListAsync();

        var consentDict = existingConsents.ToDictionary(
            x => (x.ConsentIdentityId, x.ConsentType)
        );

        var toInsert = new List<ConsentEntry>();
        var toUpdate = new List<ConsentEntry>();
        var history = new List<ConsentHistory>();

        var now = DateTime.UtcNow;

        foreach (var item in batch)
        {
            var key = (
                item.ConsentKey.ConsentSubjectId,
                item.ConsentKey.ConsentSubjectType.GetValueOrDefault(),
                item.ConsentKey.ConsentObjectId,
                item.ConsentKey.ConsentObjectType.GetValueOrDefault()
            );

            if (!identityDict.TryGetValue(key, out var identity))
            {
                identity = new ConsentIdentityEntry
                {
                    ConsentSubject = key.Item1,
                    ConsentSubjectType = key.Item2,
                    ConsentObject = key.Item3,
                    ConsentObjectType = key.Item4,
                    Created = now
                };

                context.ConsentIdentity.Add(identity);
                await context.SaveChangesAsync(); // potrzebne do ID

                identityDict[key] = identity;
            }

            var consentKey = (identity.Id, item.ConsentType);

            if (!consentDict.TryGetValue(consentKey, out var existing))
            {
                var newConsent = new ConsentEntry
                {
                    ConsentIdentityId = identity.Id,
                    ConsentType = item.ConsentType,
                    ConsentAnswer = item.ConsentAnswer,
                    Created = now,
                    LastModified = now,
                    User = item.User,
                    System = item.System,
                    Channel = item.Channel
                };

                toInsert.Add(newConsent);

                history.Add(new ConsentHistory
                {
                    Consent = newConsent,
                    Created = now
                });
            }
            else if (existing.ConsentAnswer != item.ConsentAnswer)
            {
                existing.ConsentAnswer = item.ConsentAnswer;
                existing.LastModified = now;

                toUpdate.Add(existing);

                history.Add(new ConsentHistory
                {
                    ConsentId = existing.Id,
                    Created = now
                });
            }
        }

        // 5. Batch save
        if (toInsert.Any())
            context.ConsentEntries.AddRange(toInsert);

        if (toUpdate.Any())
            context.ConsentEntries.UpdateRange(toUpdate);

        if (history.Any())
            context.ConsentHistory.AddRange(history);

        await context.SaveChangesAsync();

        // 🔥 KLUCZOWE
        context.ChangeTracker.Clear();
    }
}