namespace Net.Code.Kbo.Build;

/// <summary>Publishes a validated database and its bound source snapshot before switching the pointer.</summary>
static class IncrementalPublisher
{
    public static void Publish(
        string share, string database, string source, SourceBinding binding,
        Action<string> log, CancellationToken cancellationToken)
    {
        var previous = Releaser.CurrentExtract(share);
        if (binding.PreviousExtract != previous)
            throw new InvalidDataException("The source binding does not identify the previous release.");
        var sourceTarget = SourceBinding.PathFor(share, binding.Extract);
        var databaseTarget = Path.Combine(share, Releaser.FileName(binding.Extract));
        Directory.CreateDirectory(Path.GetDirectoryName(sourceTarget)!);

        if (File.Exists(sourceTarget))
        {
            if (SourceBinding.HashFile(sourceTarget, cancellationToken) != binding.Hash)
                throw new IOException("An incompatible source snapshot already exists for this extract.");
        }
        else
        {
            CopyAtomically(source, sourceTarget, cancellationToken);
            if (SourceBinding.HashFile(sourceTarget, cancellationToken) != binding.Hash)
            {
                File.Delete(sourceTarget);
                throw new IOException("The published source snapshot failed its integrity check.");
            }
        }

        if (File.Exists(databaseTarget))
        {
            var existing = SourceBinding.Open(share, binding.Extract, log, cancellationToken);
            if (existing != binding)
                throw new IOException("An incompatible immutable release already exists for this extract.");
        }
        else
            CopyAtomically(database, databaseTarget, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        Releaser.WriteAtomically(Path.Combine(share, Releaser.CurrentFile), Releaser.FileName(binding.Extract));
        log($"published {Releaser.FileName(binding.Extract)}");
        Releaser.PruneReleases(share, binding.Extract, previous, log);
    }

    private static void CopyAtomically(string source, string destination, CancellationToken cancellationToken)
    {
        var temporary = destination + ".tmp";
        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Copy(source, temporary, overwrite: true);
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
