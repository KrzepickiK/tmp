public async Task ImportMarketingConsentsAsync(IEnumerable<ConsentToImport> consents)
{
    const int batchSize = 100;

    _importRepository.DisableAutoDetectChanges();

    foreach (var chunk in consents
                 .Distinct(new ConsentToImportComparer())
                 .Chunk(batchSize))
    {
        var toSelect = chunk.Select(x =>
            (x.ConsentKey.ConsentSubjectType.GetValueOrDefault(),
             x.ConsentKey.ConsentSubjectId,
             x.ConsentKey.ConsentObjectType.GetValueOrDefault(),
             x.ConsentKey.ConsentObjectId)).ToList();

        // 🔥 identities bez Include
        var identities = await _importRepository.GetAsync(toSelect);

        var identityDict = identities.ToDictionary(
            x => (x.ConsentSubject, x.ConsentSubjectType, x.ConsentObject, x.ConsentObjectType)
        );

        // 🔥 dociągamy consent entries osobno
        var identityIds = identities.Select(x => x.Id).ToList();

        var consentsFromDb = await _importRepository.GetConsentsByIdentityIdsAsync(identityIds);

        var consentDict = consentsFromDb.ToDictionary(
            x => (x.ConsentIdentityId, x.ConsentType)
        );

        foreach (var entry in chunk)
        {
            var key = (
                entry.ConsentKey.ConsentSubjectId,
                entry.ConsentKey.ConsentSubjectType.GetValueOrDefault(),
                entry.ConsentKey.ConsentObjectId,
                entry.ConsentKey.ConsentObjectType.GetValueOrDefault()
            );

            identityDict.TryGetValue(key, out var identity);

            // 🔥 podpinamy lightweight consents (bez Include)
            if (identity != null)
            {
                identity.Consents = consentsFromDb
                    .Where(x => x.ConsentIdentityId == identity.Id)
                    .ToList();
            }

            ICollection<ConsentEntry> newConsents;

            if (entry.CommercialInformationObjection == ConsentAnswersDictionary.Y)
            {
                newConsents = await _importConsentsConfigurationManager
                    .ConfigureConsentsWithCommercialObjection(identity);
            }
            else
            {
                var consentsToAdd = _entriesBuilder.BuildImportEntries(entry);

                newConsents = await _importConsentsConfigurationManager
                    .ConfigureConsentsWithoutCommercialObjection(identity, consentsToAdd);
            }

            await _setConsentsService.SetConsentsAsync(
                newConsents,
                entry.ConsentHeader,
                entry.ConsentKey,
                DateTime.UtcNow,
                identity,
                saveImmediately: false
            );
        }

        // 🔥 batch save + czyszczenie pamięci
        await _importRepository.SaveChangesAsync();
        _importRepository.Clear(); // 👈 MUSI być
    }
}