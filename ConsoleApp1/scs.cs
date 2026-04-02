public class SetConsentsService : ISetConsentsService
{
    private readonly IConsentsRepository _repository;
    private readonly ITimeService _timeService;

    public SetConsentsService(IConsentsRepository repository, ITimeService timeService)
    {
        _repository = repository;
        _timeService = timeService;
    }

    public async Task<ICollection<ConsentEntry>> SetConsentsAsync(
        ICollection<ConsentEntry> consentsToSet,
        ConsentHeaderRequest consentHeaders,
        ConsentKey consentKey,
        DateTime readTimestamp,
        ConsentIdentityEntry? existingIdentity,
        bool saveImmediately)
    {
        if (consentsToSet.Count == 0)
            return consentsToSet;

        var now = _timeService.UtcNow();

        Dictionary<string, ConsentEntry>? existingDict = null;

        if (existingIdentity != null && existingIdentity.Consents != null)
        {
            existingDict = existingIdentity.Consents
                .ToDictionary(x => x.ConsentType);
        }

        if (existingIdentity is null)
        {
            existingIdentity = new ConsentIdentityEntry
            {
                ConsentObjectType = consentKey.ConsentObjectType.GetValueOrDefault(),
                ConsentObject = consentKey.ConsentObjectId,
                ConsentSubjectType = consentKey.ConsentSubjectType.GetValueOrDefault(),
                ConsentSubject = consentKey.ConsentSubjectId,
                Created = now
            };

            foreach (var consent in consentsToSet)
            {
                consent.ConsentIdentity = existingIdentity;
                consent.Created = consent.LastModified = now;
                consent.User = consentHeaders.User;
                consent.System = consentHeaders.System;
                consent.Channel = consentHeaders.Channel;
            }

            await _repository.AddConsentsAsync(consentsToSet.ToArray());
            await _repository.AddHistoryAsync(consentsToSet.ToArray());
        }
        else
        {
            if (existingIdentity.Consents?.Count > 0
                && existingIdentity.Consents.Max(x => x.LastModified) > readTimestamp)
                throw new ArgumentException("Another channel save consent after readTimestamp");

            foreach (var consentEntry in consentsToSet)
            {
                existingDict?.TryGetValue(consentEntry.ConsentType, out var matchedExistingConsent);

                if (matchedExistingConsent is null)
                {
                    consentEntry.ConsentIdentity = existingIdentity;
                    consentEntry.User = consentHeaders.User;
                    consentEntry.System = consentHeaders.System;
                    consentEntry.Channel = consentHeaders.Channel;
                    consentEntry.Created = consentEntry.LastModified = now;

                    await _repository.AddConsentsAsync(consentEntry);
                    await _repository.AddHistoryAsync(consentEntry);
                }
                else if (matchedExistingConsent.ConsentAnswer != consentEntry.ConsentAnswer)
                {
                    matchedExistingConsent.ConsentAnswer = consentEntry.ConsentAnswer;
                    matchedExistingConsent.LastModified = now;
                    matchedExistingConsent.User = consentHeaders.User;
                    matchedExistingConsent.System = consentHeaders.System;
                    matchedExistingConsent.Channel = consentHeaders.Channel;

                    await _repository.AddHistoryAsync(matchedExistingConsent);
                }
            }
        }

        if (saveImmediately)
            await _repository.SaveChangesAsync();

        return existingIdentity.Consents;
    }
}