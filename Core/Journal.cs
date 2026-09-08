using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aeropeek.Core;

public sealed class JournalSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset Started { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? Closed { get; set; }
    public string Label { get; set; } = "";
    public List<OpRecord> Records { get; set; } = new();
}

/// <summary>
/// Trace horodatée d'un changement, conservée même après annulation.
/// <para>
/// Les enregistrements d'opération disparaissent du journal quand on les annule —
/// c'est voulu, il ne reste alors plus rien à restaurer. Mais le fait QU'IL SE SOIT
/// PASSÉ QUELQUE CHOSE à cet instant, lui, ne doit pas disparaître : sans cela le
/// benchmark comparerait deux captures encadrant un aller-retour en croyant que
/// rien n'a bougé.
/// </para>
/// </summary>
public sealed class ChangeMark
{
    public DateTimeOffset When { get; set; } = DateTimeOffset.Now;
    public string TweakId { get; set; } = "";
    public string Label { get; set; } = "";
    public bool Reverted { get; set; }
}

public sealed class JournalFile
{
    public int Version { get; set; } = 1;
    public List<JournalSession> Sessions { get; set; } = new();

    /// <summary>Chronologie des changements, jamais élaguée.</summary>
    public List<ChangeMark> Changes { get; set; } = new();

    /// <summary>
    /// Début de la tenue de cette chronologie. Avant cette date, l'application
    /// ignore ce qui a changé et doit le dire au lieu de conclure.
    /// </summary>
    public DateTimeOffset? ChangesSince { get; set; }
}

/// <summary>
/// Journal persistant de tout ce que l'application a modifié.
/// Écrit sur disque avant chaque opération, pour qu'un plantage n'empêche
/// jamais la restauration.
/// </summary>
public sealed class Journal
{
    static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static string Directory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aeropeek");

    public static string FilePath => Path.Combine(Directory, "journal.json");

    JournalFile _data = new();
    JournalSession? _current;

    public IReadOnlyList<JournalSession> Sessions => _data.Sessions;

    public static Journal Load()
    {
        var j = new Journal();
        try
        {
            if (File.Exists(FilePath))
                j._data = JsonSerializer.Deserialize<JournalFile>(File.ReadAllText(FilePath), JsonOpts) ?? new JournalFile();
        }
        catch { j._data = new JournalFile(); }

        // Journal antérieur à cette chronologie : elle commence maintenant, et
        // tout ce qui précède reste explicitement inconnu.
        if (j._data.ChangesSince == null)
        {
            j._data.ChangesSince = DateTimeOffset.Now;
            j.Save();
        }
        return j;
    }

    /// <summary>Depuis quand l'application sait dater ses changements.</summary>
    public DateTimeOffset? ChangesSince => _data.ChangesSince;

    /// <summary>
    /// Changements survenus strictement entre deux instants, un par réglage.
    /// </summary>
    public List<ChangeMark> ChangesBetween(DateTimeOffset from, DateTimeOffset to) =>
        _data.Changes
             .Where(c => c.When > from && c.When < to)
             .GroupBy(c => c.TweakId.Length > 0 ? c.TweakId : c.Label)
             .Select(g => g.First())
             .OrderBy(c => c.When)
             .ToList();

    void Mark(OpRecord rec, bool reverted)
    {
        _data.Changes.Add(new ChangeMark
        {
            TweakId = rec.TweakId,
            Label = rec.Description,
            Reverted = reverted
        });
    }

    /// <summary>Sessions restées ouvertes : signe d'un arrêt brutal de l'application.</summary>
    public List<JournalSession> UnclosedSessions() => _data.Sessions.Where(s => s.Closed == null).ToList();

    public JournalSession Begin(string label)
    {
        _current = new JournalSession { Label = label };
        _data.Sessions.Add(_current);
        Save();
        return _current;
    }

    public void Record(OpRecord rec)
    {
        _current ??= Begin("Session");
        _current.Records.Add(rec);
        Mark(rec, reverted: false);
        Save();                      // avant tout, pas après
    }

    public void Close()
    {
        if (_current == null) return;
        _current.Closed = DateTimeOffset.Now;
        _current = null;
        Save();
    }

    /// <summary>Annule toutes les opérations d'une session, de la plus récente à la plus ancienne.</summary>
    public int RevertSession(JournalSession session, IProgress<string>? progress = null)
    {
        int done = 0;
        foreach (var rec in Enumerable.Reverse(session.Records.ToList()))
        {
            progress?.Report(rec.Description.Length > 0 ? rec.Description + "…" : "Restoring…");
            try { Ops.Revert(rec); done++; Mark(rec, reverted: true); } catch { }
        }
        session.Records.Clear();
        session.Closed ??= DateTimeOffset.Now;
        session.Label = session.Label + " (undone)";
        Save();
        return done;
    }

    /// <summary>
    /// Annule uniquement les écritures d'un réglage donné. Renvoie 0 si l'application
    /// n'a jamais touché à ce réglage — auquel cas il n'y a rien à restaurer.
    /// </summary>
    public int RevertTweak(string tweakId)
    {
        int done = 0;
        foreach (var s in _data.Sessions)
        {
            var mine = s.Records.Where(r => r.TweakId == tweakId).Reverse().ToList();
            foreach (var rec in mine)
            {
                try { Ops.Revert(rec); done++; s.Records.Remove(rec); Mark(rec, reverted: true); } catch { }
            }
        }
        if (done > 0) Save();
        return done;
    }

    /// <summary>
    /// Annule exactement les enregistrements demandés, du plus récent au plus
    /// ancien. Passer par l'identifiant du réglage ne conviendrait pas : le Mode
    /// Match range tous ses services sous le même, et annuler une ligne les
    /// annulerait tous.
    /// </summary>
    public int RevertRecords(IEnumerable<string> ids)
    {
        var wanted = new HashSet<string>(ids);
        int done = 0;

        foreach (var s in _data.Sessions)
        {
            var mine = s.Records.Where(r => wanted.Contains(r.Id)).Reverse().ToList();
            foreach (var rec in mine)
            {
                try { Ops.Revert(rec); done++; s.Records.Remove(rec); Mark(rec, reverted: true); } catch { }
            }
        }

        if (done > 0) Save();
        return done;
    }

    /// <summary>Annule tout ce que l'application a jamais modifié.</summary>
    public int RevertEverything()
    {
        int done = 0;
        foreach (var s in Enumerable.Reverse(_data.Sessions.ToList()))
            done += RevertSession(s);
        return done;
    }

    public void Save()
    {
        try
        {
            System.IO.Directory.CreateDirectory(Directory);
            var tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_data, JsonOpts));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch { /* on ne bloque jamais l'application sur un échec d'écriture du journal */ }
    }

    public int TotalRecords => _data.Sessions.Sum(s => s.Records.Count);
}
