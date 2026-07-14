using System.Collections.Concurrent;

public sealed record Client(string Id, string Name);

public sealed record ClientAccount(string Id, string AccountNumber);

public sealed record ClientAccountRow(
    Client Client,
    ClientAccount ClientAccount);

public sealed class ClientAccountConflictCache
{
    private sealed class AccountEntry
    {
        public AccountEntry(ClientAccount account)
        {
            Account = account;
        }

        public ClientAccount Account { get; }

        public ConcurrentDictionary<string, Client> Clients { get; } = new();

        public bool IsConflict => Clients.Count > 1;
    }

    private readonly ConcurrentDictionary<string, AccountEntry> _accounts = new();

    public void AddChunk(
        IEnumerable<ClientAccountRow> chunk,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(chunk);

        Parallel.ForEach(
            chunk,
            new ParallelOptions
            {
                CancellationToken = cancellationToken,
                MaxDegreeOfParallelism = Environment.ProcessorCount
            },
            row =>
            {
                var entry = _accounts.GetOrAdd(
                    row.ClientAccount.Id,
                    _ => new AccountEntry(row.ClientAccount));

                entry.Clients.TryAdd(row.Client.Id, row.Client);
            });
    }

    public bool IsConflict(string clientAccountId)
    {
        return _accounts.TryGetValue(clientAccountId, out var entry)
               && entry.IsConflict;
    }

    public IReadOnlyCollection<string> GetClientIds(string clientAccountId)
    {
        return _accounts.TryGetValue(clientAccountId, out var entry)
            ? entry.Clients.Keys.ToArray()
            : Array.Empty<string>();
    }

    public ConflictSnapshot CreateSnapshot()
    {
        var conflictingAccountIds = new HashSet<string>();
        var conflictingClientIds = new HashSet<string>();

        foreach (var pair in _accounts)
        {
            var clients = pair.Value.Clients.Keys.ToArray();

            if (clients.Length <= 1)
                continue;

            conflictingAccountIds.Add(pair.Key);

            foreach (var clientId in clients)
                conflictingClientIds.Add(clientId);
        }

        return new ConflictSnapshot(
            conflictingAccountIds,
            conflictingClientIds);
    }
}

public sealed class ConflictSnapshot
{
    private readonly HashSet<string> _accountIds;
    private readonly HashSet<string> _clientIds;

    internal ConflictSnapshot(
        HashSet<string> accountIds,
        HashSet<string> clientIds)
    {
        _accountIds = accountIds;
        _clientIds = clientIds;
    }

    public bool ContainsAccount(string clientAccountId) =>
        _accountIds.Contains(clientAccountId);

    public bool ContainsClient(string clientId) =>
        _clientIds.Contains(clientId);

    public int ConflictingAccountCount => _accountIds.Count;
}
