using System.IO;
using AiLight.Core;
namespace AiLight.App;
public sealed class Diagnostics(bool persist = true) : DiagnosticJournal(persist,
    () => new FileInfo(Path.Combine(Storage.Root, FileName)) is { Exists: true, Length: < 2_000_000 }
        ? Storage.Read<List<DiagnosticEntry>>(FileName) ?? [] : [],
    entries => Storage.Save(FileName, entries));
