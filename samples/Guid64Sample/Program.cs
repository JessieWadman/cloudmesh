using System.Diagnostics;

long id1 = Guid64.NewGuid();
long id2 = Guid64.NewGuid();

// Since they're roughly time sortable, id1 should always be a less value than id2.
Debug.Assert(id1 < id2);

Console.WriteLine(id1);