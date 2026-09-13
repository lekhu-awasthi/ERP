namespace ErpApp.Application.Imports;

/// <summary>
/// Puts a hierarchical import file's rows in an order where every parent is created before its
/// children, and refuses the file outright when no such order exists.
///
/// <para><b>One implementation, two importers, and a deliberate refusal to write a third.</b>
/// Product Category and Account Group pose the identical problem in different words; the reference
/// product's own templates state it as a rule the user must obey ("Parent category should already
/// exist or should be in the upcoming rows" / "Make sure there are no cyclic dependencies"), which
/// is a fair thing to ask of a person and a terrible thing to depend on. Sorting the file is
/// cheaper than explaining the ordering, and detecting a cycle is the only way to keep the promise
/// that a bad file writes nothing.</para>
///
/// <para><b>Only edges inside the file are edges.</b> A parent name that no row in the file claims
/// is treated as external -- it is either already in the tenant's database, in which case the
/// importer resolves it by name at plan time, or it is nowhere, in which case that <i>one</i> row is
/// rejected with a message naming the missing parent. Guessing here would turn a single bad cell
/// into a whole-file failure.</para>
///
/// <para><b>The order is deterministic</b>, because a resumed job re-sorts the same file and must
/// walk it the same way: ties inside one depth are broken by spreadsheet row number, so the output
/// is a function of the input alone.</para>
/// </summary>
public static class ImportRowSequencer
{
    /// <summary>
    /// Returns <paramref name="rows"/> reordered parents-first.
    /// </summary>
    /// <exception cref="ImportFileException">The file names one key twice (so a child's parent is
    /// ambiguous), or its rows form a cycle. Both are whole-file conditions by construction: there
    /// is no row to blame for a cycle, only a set of them, and the caller marks the job Failed with
    /// the rows listed rather than importing the part that happens to sort.</exception>
    public static IReadOnlyList<ImportSheetRow> Order(
        IReadOnlyList<ImportSheetRow> rows,
        IReadOnlyDictionary<string, int> columnIndexes,
        string keyColumn,
        string parentColumn)
    {
        var nodes = new List<Node>(rows.Count);
        var byKey = new Dictionary<string, Node>(StringComparer.OrdinalIgnoreCase);
        var duplicates = new SortedDictionary<string, List<int>>(StringComparer.OrdinalIgnoreCase);

        foreach (var row in rows)
        {
            var reader = new ImportRowReader(columnIndexes, row);
            var node = new Node(row, reader.GetOptionalString(keyColumn), reader.GetOptionalString(parentColumn));
            nodes.Add(node);

            if (node.Key is null)
            {
                // A row with no name of its own cannot be anybody's parent, so it has no place in
                // the graph -- but it is still a row, and rejecting the whole file for it would hide
                // the real message ("'Category Name' is required") behind a file-level error. It
                // sorts last and PlanAsync rejects it on its own terms.
                continue;
            }

            if (!byKey.TryAdd(node.Key, node))
            {
                if (!duplicates.TryGetValue(node.Key, out var rowNumbers))
                {
                    rowNumbers = [byKey[node.Key].Row.RowNumber];
                    duplicates[node.Key] = rowNumbers;
                }

                rowNumbers.Add(node.Row.RowNumber);
            }
        }

        if (duplicates.Count > 0)
        {
            var detail = string.Join(
                "; ", duplicates.Select(d => $"'{d.Key}' on rows {string.Join(", ", d.Value)}"));

            throw new ImportFileException(
                $"The file names the same {keyColumn} more than once, so a row's {parentColumn} would be "
                    + $"ambiguous: {detail}. Give each row a distinct {keyColumn} and upload again.");
        }

        foreach (var node in nodes)
        {
            if (node.ParentKey is not null && byKey.TryGetValue(node.ParentKey, out var parent) && parent != node)
            {
                node.Parent = parent;
                parent.Children.Add(node);
            }
            else if (node.ParentKey is not null && node.Key is not null
                     && string.Equals(node.ParentKey, node.Key, StringComparison.OrdinalIgnoreCase))
            {
                // A row that is its own parent is a cycle of length one. Left unlinked it would sort
                // cleanly and then be rejected by the database, so it is caught here with the rest.
                node.Parent = node;
            }
        }

        var ordered = new List<ImportSheetRow>(nodes.Count);
        var emitted = new HashSet<Node>();
        var ready = new PriorityQueue<Node, int>();

        foreach (var node in nodes.Where(n => n.Parent is null))
        {
            ready.Enqueue(node, node.Row.RowNumber);
        }

        while (ready.TryDequeue(out var node, out _))
        {
            ordered.Add(node.Row);
            emitted.Add(node);
            foreach (var child in node.Children)
            {
                ready.Enqueue(child, child.Row.RowNumber);
            }
        }

        if (ordered.Count != nodes.Count)
        {
            var stuck = nodes
                .Where(n => !emitted.Contains(n))
                .Select(n => $"row {n.Row.RowNumber} ('{n.Key}' -> '{n.ParentKey}')")
                .ToList();

            throw new ImportFileException(
                $"The file's {parentColumn} references form a cycle, so no order exists in which every parent "
                    + $"is created before its children. Nothing was imported. Involved: {string.Join("; ", stuck)}.");
        }

        return ordered;
    }

    private sealed class Node(ImportSheetRow row, string? key, string? parentKey)
    {
        public ImportSheetRow Row { get; } = row;

        public string? Key { get; } = key;

        public string? ParentKey { get; } = parentKey;

        public Node? Parent { get; set; }

        public List<Node> Children { get; } = [];
    }
}

/// <summary>
/// A problem with the file as a whole rather than with one of its rows -- currently a duplicate key
/// or a cycle in a hierarchical import.
///
/// <para>Distinct from <see cref="ImportRowException"/> on purpose: that one is caught per row and
/// leaves the other rows to succeed, while this one marks the entire job Failed before anything is
/// written. The reference product's templates ask the user to avoid cycles; this is what happens
/// when they do not.</para>
/// </summary>
public sealed class ImportFileException(string message) : Exception(message);
