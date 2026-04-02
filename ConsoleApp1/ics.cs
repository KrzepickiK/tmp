public class ImportConsentsService : IMarketingConsentsService
{
    private readonly IImportConsentsRepository _importRepository;
    private readonly ISetConsentsService _setConsentsService;
    private readonly ConsentToImportEntryBuilder _entriesBuilder;
    private readonly IImportConsentConfigurationManager _importConsentsConfigurationManager;

    public ImportConsentsService(
        IImportConsentsRepository importRepository,
        ISetConsentsService setConsentsService,
        ConsentToImportEntryBuilder entriesBuilder,
        IImportConsentConfigurationManager importConsentsConfigurationManager)
    {
        _importRepository = importRepository;
        _setConsentsService = setConsentsService;
        _entriesBuilder = entriesBuilder;
        _importConsentsConfigurationManager = importConsentsConfigurationManager;
    }

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

            var identities = await _importRepository.GetAsync(toSelect);

            var identityDict = identities.ToDictionary(
                x => (x.ConsentSubject, x.ConsentSubjectType, x.ConsentObject, x.ConsentObjectType)
            );

            var identityIds = identities.Select(x => x.Id).ToList();

            var consentsFromDb = await _importRepository.GetConsentsByIdentityIdsAsync(identityIds);

            foreach (var identity in identities)
            {
                identity.Consents = consentsFromDb
                    .Where(x => x.ConsentIdentityId == identity.Id)
                    .ToList();
            }

            foreach (var entry in chunk)
            {
                var key = (
                    entry.ConsentKey.ConsentSubjectId,
                    entry.ConsentKey.ConsentSubjectType.GetValueOrDefault(),
                    entry.ConsentKey.ConsentObjectId,
                    entry.ConsentKey.ConsentObjectType.GetValueOrDefault()
                );

                identityDict.TryGetValue(key, out var identity);

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

            // 🔥 KLUCZOWE
            await _importRepository.SaveChangesAsync();
            _importRepository.Clear();
        }

        _importRepository.EnableAutoDetectChanges();
    }
}