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

var final = post.GetAt(when);
Console.WriteLine(JsonSerializer.Serialize(final));
Console.WriteLine(post.ToString());

// Expected: 3 points in time
//      0000-01-01
//      2019-12-01
//      2025-12-01
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
