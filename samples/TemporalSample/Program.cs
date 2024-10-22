using System.Text.Json;
using CloudMesh.Temporal;

var post = new Employee();

var when = new DateOnly(2019, 12, 01);

post.Set(default, e => e.Name, "Bob");

post.Set(when, p => p.Roles[0].Name, "Accountant");
post.Set(when, p => p.Contacts["Email"], "bob@domain.com");
post.Set(when, p => p.Addresses["Home"].Street, "1 CloudMesh way");
post.Set(when, p => p.Addresses["Home"].City, "Stockholm");

when = new DateOnly(2025, 12, 01);
post.Set(when, p => p.Contacts["Email"], "bob@bob.com");

// This will replace the previous one before it occurs earlier in time, and we have the flag by default set
// to clear any future, pending changes to the property being set.  
when = new DateOnly(2025, 10, 01);
post.Set(when, p => p.Contacts["Email"], "bob@upgraded.com");
post.Set(when, p => p.Attrib1, null);
post.Set(when, p => p.Attrib2, default);

when = new DateOnly(2025, 12, 01);

var final = post.GetAt(when);
Console.WriteLine(JsonSerializer.Serialize(final));

// Expected: 3 points in time
//      0000-01-01
//      2019-12-01
//      2025-12-01
Console.WriteLine($"Employee has {post.GetPointInTimes().Count()} points in time.");

when = new DateOnly(2025, 11, 01);
post.ReduceTo(when);
Console.WriteLine($"Employee has {post.GetPointInTimes().Count()} points in time.");

public class Employee : Temporal<EmployeeSnapshot>
{
    public int Id { get; set; }

    protected override void BeforeReturnSnapshot(EmployeeSnapshot snapshot, DateOnly pointInTime)
    {
        // Time property is not temporal, it's constant.
        snapshot.Id = this.Id;
    }
}

public class EmployeeSnapshot
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Attrib1 { get; set; }
    public DateTime Attrib2 { get; set; }
    public Dictionary<string, string> Contacts { get; set; } = null!;
    public Dictionary<string, Address> Addresses { get; set; } = null!;
    public List<JobRole> Roles { get; set; } = null!;
}

public class JobRole
{
    public string Name { get; set; } = null!;
    
}

public class Address
{
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string State { get; set; } = null!;
    public string Zip { get; set; } = null!;
}
