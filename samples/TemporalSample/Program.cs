using System.Text.Json;
using CloudMesh.Temporal;

var employee = new Employee();

// Set initial values for the employee. Point in time here is 0000-01-01
employee.Set(pointInTime: default, e => e.Name, "Bob");
employee.Set(pointInTime: default, p => p.Attrib3, default);

// Set values for the employee at different points in time
var pointInTime = new DateOnly(2019, 12, 01);
employee.Set(pointInTime, p => p.Roles[0].Name, "Accountant");
employee.Set(pointInTime, p => p.Contacts["Email"], "bob@domain.com");
employee.Set(pointInTime, p => p.Addresses["Home"].Street, "1 CloudMesh way");
employee.Set(pointInTime, p => p.Addresses["Home"].City, "Stockholm");

pointInTime = new DateOnly(2025, 12, 01);
employee.Set(pointInTime, p => p.Contacts["Email"], "bob@bob.com");

// This will replace the previous one before it occurs (because this is earlier in time).  
pointInTime = new DateOnly(2025, 10, 01);
// This replaces 'bob@bob.com' above
employee.Set(pointInTime, p => p.Contacts["Email"], "bob@upgraded.com");
employee.Set(pointInTime, p => p.Attrib1, null);
employee.Set(pointInTime, p => p.Attrib2, default);

pointInTime = new DateOnly(2025, 12, 01);

var final = employee.GetAt(pointInTime);

Console.WriteLine(JsonSerializer.Serialize(final));

// Expected: 3 points in time
//      0000-01-01
//      2019-12-01
//      2025-12-01
Console.WriteLine($"Employee has {employee.GetPointInTimes().Count()} points in time.");

pointInTime = new DateOnly(2025, 11, 01);
employee.ReduceTo(pointInTime);
Console.WriteLine($"Employee has {employee.GetPointInTimes().Count()} points in time.");

internal class Employee : Temporal<EmployeeSnapshot>
{
    public int Id { get; set; }

    protected override void BeforeReturnSnapshot(EmployeeSnapshot snapshot, DateOnly pointInTime)
    {
        // This property is not temporal, it's always constant, which is why we pass it through.
        snapshot.Id = this.Id;
    }
}

internal class EmployeeSnapshot
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public string? Attrib1 { get; set; }
    public DateTime Attrib2 { get; set; }
    
    public int Attrib3 { get; set; }
    public Dictionary<string, string> Contacts { get; set; } = null!;
    public Dictionary<string, Address> Addresses { get; set; } = null!;
    public List<JobRole> Roles { get; set; } = null!;
}

internal class JobRole
{
    public string Name { get; set; } = null!;
}

internal class Address
{
    public string Street { get; set; } = null!;
    public string City { get; set; } = null!;
    public string State { get; set; } = null!;
    public string Zip { get; set; } = null!;
}
