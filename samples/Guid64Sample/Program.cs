using System.Diagnostics;

var id1 = Guid64.NewGuid();
var id2 = Guid64.NewGuid();

// Since they're roughly time sortable, id1 should always be a less value than id2.
Debug.Assert(id1 < id2);

Console.WriteLine(id1);